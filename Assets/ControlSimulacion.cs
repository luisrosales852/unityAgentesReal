
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(WebClient))]
public class ControlSimulacion : MonoBehaviour
{
    [Header("Camaras")]
    [Tooltip("Crea las tres camaras al arrancar si no existen ya en la escena.")]
    public bool crearCamaras = true;

    [Tooltip("Grados de inclinacion de la camara en 3/4.")]
    public float inclinacionTresCuartos = 50f;

    [Tooltip("Margen alrededor del almacen al encuadrarlo, en tantos por uno.")]
    public float margenEncuadre = 1.15f;

    [Tooltip("Con que vista arranca: 0 cenital, 1 en 3/4, 2 siguiendo a un AGV.")]
    public int vistaInicial = 1;

    [Header("Comparativa de flotas")]
    [Tooltip("Paso en el que se congela la foto de cada reparto para poder " +
             "compararlos con el mismo reloj. Se vuelve a tomar cada corrida.")]
    public int pasoDeComparacion = 600;

    [Header("Velocidad")]
    [Tooltip("Segundos entre pasos mas rapido posible.")]
    public float intervaloMinimo = 0.05f;

    [Tooltip("Segundos entre pasos mas lento posible.")]
    public float intervaloMaximo = 1.5f;

    WebClient cliente;
    readonly List<Camera> camaras = new List<Camera>();
    readonly string[] nombresDeVista = { "Cenital", "3/4", "Siguiendo AGV" };
    int vistaActual;

    Transform seguido;
    Camera camSeguimiento;
    Vector3 centroAlmacen;
    float radioAlmacen = 12f;

    Text textoEstado, textoAgvs, textoAyuda, textoComparativa;
    Text etiquetaPausa, etiquetaVelocidad, etiquetaVista, etiquetaFlota;

    class Marca
    {
        public int corrida, paso, entregadas, conflictos, bandaPeor, desbalance;
        public float bandaEspera, bandaCiclo;
    }

    readonly Dictionary<string, Marca> marcas = new Dictionary<string, Marca>();
    readonly string[] repartos = { "dedicada", "libre" };

    void Awake()
    {
        cliente = GetComponent<WebClient>();
    }

    void Start()
    {
        MideElAlmacen();
        if (crearCamaras) PreparaCamaras();
        ConstruyeInterfaz();
        Vista(vistaInicial);
    }

    void MideElAlmacen()
    {
        Transform raiz = cliente.raiz != null ? cliente.raiz : transform;
        var mallas = raiz.GetComponentsInChildren<MeshRenderer>();
        if (mallas.Length == 0)
        {
            centroAlmacen = raiz.position;
            return;
        }

        Bounds caja = mallas[0].bounds;
        foreach (var m in mallas) caja.Encapsulate(m.bounds);

        centroAlmacen = caja.center;
        radioAlmacen = Mathf.Max(caja.extents.x, caja.extents.z);
    }

    void PreparaCamaras()
    {

        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (c.gameObject.scene.IsValid()) c.gameObject.SetActive(false);

        camaras.Clear();
        camaras.Add(HazCamara("Camara_Cenital", Cenital(), Quaternion.Euler(90f, 0f, 0f)));

        Quaternion giro = Quaternion.Euler(inclinacionTresCuartos, 0f, 0f);
        camaras.Add(HazCamara("Camara_TresCuartos",
                              centroAlmacen - (giro * Vector3.forward) * Distancia(),
                              giro));

        camSeguimiento = HazCamara("Camara_Seguimiento", Cenital(),
                                   Quaternion.Euler(35f, 0f, 0f));
        camaras.Add(camSeguimiento);
    }

    Vector3 Cenital()
    {
        return centroAlmacen + Vector3.up * Distancia();
    }

    float Distancia()
    {
        return (radioAlmacen * margenEncuadre) / Mathf.Tan(30f * Mathf.Deg2Rad);
    }

    Camera HazCamara(string nombre, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(nombre);
        go.transform.SetPositionAndRotation(pos, rot);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.farClipPlane = 500f;
        go.SetActive(false);
        return cam;
    }

    public void Vista(int indice)
    {
        if (camaras.Count == 0) return;
        vistaActual = (indice % camaras.Count + camaras.Count) % camaras.Count;
        for (int i = 0; i < camaras.Count; i++)
            if (camaras[i] != null) camaras[i].gameObject.SetActive(i == vistaActual);
        if (etiquetaVista != null) etiquetaVista.text = "Vista: " + nombresDeVista[vistaActual];
    }

    public void SiguienteVista() { Vista(vistaActual + 1); }

    void LateUpdate()
    {
        if (camSeguimiento == null || !camSeguimiento.gameObject.activeSelf) return;

        if (seguido == null) seguido = BuscaUnAgv();
        if (seguido == null) return;

        Vector3 destino = seguido.position - seguido.forward * 6f + Vector3.up * 4.5f;
        camSeguimiento.transform.position = Vector3.Lerp(
            camSeguimiento.transform.position, destino, Time.deltaTime * 3f);
        camSeguimiento.transform.rotation = Quaternion.Slerp(
            camSeguimiento.transform.rotation,
            Quaternion.LookRotation(seguido.position + Vector3.up * 0.5f
                                    - camSeguimiento.transform.position),
            Time.deltaTime * 3f);
    }

    Transform BuscaUnAgv()
    {
        if (cliente.nombresDeAgvs != null)
            foreach (var n in cliente.nombresDeAgvs)
            {
                var go = GameObject.Find(n);
                if (go != null) return go.transform;
            }
        return null;
    }

    public void AlternarPausa()
    {
        cliente.pausado = !cliente.pausado;
        if (etiquetaPausa != null)
            etiquetaPausa.text = cliente.pausado ? "Reanudar" : "Pausa";
    }

    public void MasRapido() { CambiaIntervalo(-0.05f); }
    public void MasLento() { CambiaIntervalo(0.05f); }

    void CambiaIntervalo(float delta)
    {
        cliente.stepInterval = Mathf.Clamp(cliente.stepInterval + delta,
                                           intervaloMinimo, intervaloMaximo);
        RefrescaVelocidad();
    }

    void RefrescaVelocidad()
    {
        if (etiquetaVelocidad == null) return;
        float pasos = cliente.stepInterval > 0f ? 1f / cliente.stepInterval : 0f;
        etiquetaVelocidad.text = $"Velocidad: {pasos:0.#} pasos/s";
    }

    public void Reiniciar() { cliente.Reiniciar(); }

    public void AlternarFlota()
    {
        string otra = cliente.OtraFlota();
        if (string.IsNullOrEmpty(otra))
        {
            Debug.LogWarning("[ControlSimulacion] el servidor no dice que repartos " +
                             "de flota tiene; arranca Iniciar_AGV.command otra vez.");
            return;
        }
        cliente.CambiarFlota(otra);
    }

    void Update()
    {
        var teclado = Keyboard.current;
        if (teclado == null) return;

        if (teclado.digit1Key.wasPressedThisFrame) Vista(0);
        if (teclado.digit2Key.wasPressedThisFrame) Vista(1);
        if (teclado.digit3Key.wasPressedThisFrame) Vista(2);
        if (teclado.cKey.wasPressedThisFrame) SiguienteVista();
        if (teclado.spaceKey.wasPressedThisFrame) AlternarPausa();
        if (teclado.equalsKey.wasPressedThisFrame ||
            teclado.numpadPlusKey.wasPressedThisFrame) MasRapido();
        if (teclado.minusKey.wasPressedThisFrame ||
            teclado.numpadMinusKey.wasPressedThisFrame) MasLento();
        if (teclado.fKey.wasPressedThisFrame) AlternarFlota();

        RefrescaTextos();
    }

    void RefrescaTextos()
    {
        var s = cliente.Ultimo;
        if (textoEstado == null) return;

        if (s == null)
        {
            textoEstado.text = cliente.Conectado
                ? "Esperando el primer estado..."
                : "Sin conexion con el servidor.\nArranca Iniciar_AGV.command";
            if (textoAgvs != null) textoAgvs.text = "";
            return;
        }

        int entregadas = 0, enRuta = 0, guardadas = 0;
        if (s.boxes != null)
            foreach (var c in s.boxes)
            {
                if (c.status == "DELIVERED") entregadas++;
                else if (c.status == "IN_TRANSIT") enRuta++;
                else guardadas++;
            }

        var st = s.stats;
        textoEstado.text =
            $"<b>ALMACEN AGV</b>\n" +
            $"Conexion: {(cliente.pausado ? "EN PAUSA" : cliente.Conectado ? "activa" : "cortada")}\n" +
            $"Paso: {s.step}\n" +
            $"Politica: {s.mode}\n" +
            Flota(s) +
            $"\n" +
            $"Cajas entregadas: {entregadas}\n" +
            $"En transporte: {enRuta}\n" +
            $"En estanteria: {guardadas}\n" +
            (st != null
                ? $"\nConflictos: {st.conflicts}\nEspera acumulada: {st.total_wait_time}"
                : "");

        if (textoAgvs != null && s.agents != null)
        {
            var sb = new System.Text.StringBuilder("<b>AGVs</b>\n");
            foreach (var a in s.agents)
                sb.Append($"{a.id}  {a.state,-8} {(a.carrying != null ? "caja" : "----")}  " +
                          $"{a.battery,3:0}%\n");
            textoAgvs.text = sb.ToString();
        }

        ApuntaLaMarca(s, entregadas);
        RefrescaComparativa();
        if (etiquetaFlota != null && s.fleet != null)
            etiquetaFlota.text = "Flota: " + s.fleet.mode;
    }

    string Flota(WebClient.Snapshot s)
    {
        var f = s.fleet;
        if (f == null) return "";

        string quien = (f.dedicated != null && f.dedicated.Length > 0)
            ? $"AGV {string.Join(",", f.dedicated)} a la banda"
            : $"{(s.agents != null ? s.agents.Length : 0)} AGV sin dedicar";

        string banda = f.belt_pending > 0
            ? $"\nBanda: {f.belt_pending} caja(s) esperando"
              + (f.belt_bonus > 0f ? $", puja +{f.belt_bonus:0}" : "")
              + (f.belt_oldest > 0 ? $" (la mas vieja lleva {f.belt_oldest})" : "")
            : "\nBanda: al dia";

        return $"Flota: {f.mode} ({quien}){banda}";
    }

    void ApuntaLaMarca(WebClient.Snapshot s, int entregadas)
    {
        var f = s.fleet;
        if (f == null || string.IsNullOrEmpty(f.mode) || s.step < pasoDeComparacion)
            return;

        int corrida = s.stats != null ? s.stats.run : 0;
        if (marcas.TryGetValue(f.mode, out var vieja) && vieja.corrida == corrida)
            return;

        int alto = 0, bajo = 0;
        if (f.per_agv != null && f.per_agv.Length > 0)
        {
            alto = bajo = f.per_agv[0];
            foreach (int n in f.per_agv)
            {
                if (n > alto) alto = n;
                if (n < bajo) bajo = n;
            }
        }

        marcas[f.mode] = new Marca
        {
            corrida = corrida,
            paso = s.step,
            entregadas = entregadas,
            conflictos = s.stats != null ? s.stats.conflicts : 0,
            bandaEspera = f.belt_wait_avg,
            bandaPeor = f.belt_wait_max,
            bandaCiclo = f.belt_cycle_avg,
            desbalance = alto - bajo,
        };
    }

    void RefrescaComparativa()
    {
        if (textoComparativa == null) return;

        var sb = new System.Text.StringBuilder(
            $"<b>COMPARATIVA</b>  al paso {pasoDeComparacion}\n");

        foreach (string reparto in repartos)
        {
            if (!marcas.TryGetValue(reparto, out var m))
            {
                sb.Append($"\n<b>{reparto}</b>  sin datos todavia\n");
                continue;
            }
            sb.Append($"\n<b>{reparto}</b>  corrida {m.corrida}\n" +
                      $"  entregadas {m.entregadas}   conflictos {m.conflictos}\n" +
                      $"  banda: espera {m.bandaEspera:0.#}, peor {m.bandaPeor}\n" +
                      $"  banda -> estanteria: {m.bandaCiclo:0.#}\n" +
                      $"  desnivel entre AGV: {m.desbalance}\n");
        }

        if (marcas.Count < repartos.Length)
            sb.Append("\nPulsa F para correr el otro reparto.");

        textoComparativa.text = sb.ToString();
    }

    void ConstruyeInterfaz()
    {
        var canvasGO = new GameObject("Canvas_Simulacion");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var escala = canvasGO.AddComponent<CanvasScaler>();
        escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        escala.referenceResolution = new Vector2(1280f, 720f);
        escala.matchWidthOrHeight = 1f;
        canvas.pixelPerfect = true;
        canvasGO.AddComponent<GraphicRaycaster>();

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var ev = new GameObject("EventSystem");
            ev.AddComponent<UnityEngine.EventSystems.EventSystem>();
            ev.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        textoEstado = Panel(canvasGO.transform, "Estado",
                            new Vector2(0f, 1f), new Vector2(18f, -18f),
                            new Vector2(400f, 360f), 26);
        textoAgvs = Panel(canvasGO.transform, "Agvs",
                          new Vector2(0f, 1f), new Vector2(18f, -392f),
                          new Vector2(400f, 232f), 23);
        textoAyuda = Panel(canvasGO.transform, "Ayuda",
                           new Vector2(1f, 1f), new Vector2(-18f, -18f),
                           new Vector2(370f, 226f), 22);
        textoAyuda.text =
            "<b>CONTROLES</b>\n" +
            "1 / 2 / 3   vista\n" +
            "C   siguiente vista\n" +
            "Espacio   pausa\n" +
            "+ / -   velocidad\n" +
            "R   reiniciar corrida\n" +
            "F   dedicada / libre";

        textoComparativa = Panel(canvasGO.transform, "Comparativa",
                                 new Vector2(1f, 1f), new Vector2(-18f, -258f),
                                 new Vector2(370f, 300f), 21);

        BarraDeBotones(canvasGO.transform);
        RefrescaVelocidad();
    }

    Text Panel(Transform padre, string nombre, Vector2 ancla, Vector2 sitio,
               Vector2 tam, int cuerpo)
    {
        var go = new GameObject("Panel_" + nombre);
        go.transform.SetParent(padre, false);

        var fondo = go.AddComponent<Image>();
        fondo.color = new Color(0.05f, 0.06f, 0.09f, 0.82f);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = ancla;
        rt.pivot = new Vector2(ancla.x, ancla.y);
        rt.anchoredPosition = sitio;
        rt.sizeDelta = tam;

        var txtGO = new GameObject("Texto");
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.font = Tipografia();
        txt.fontSize = cuerpo;
        txt.color = new Color(0.94f, 0.96f, 1f);
        txt.supportRichText = true;
        txt.alignment = TextAnchor.UpperLeft;

        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(14f, 12f);
        trt.offsetMax = new Vector2(-14f, -12f);
        return txt;
    }

    void BarraDeBotones(Transform padre)
    {
        var barra = new GameObject("Barra");
        barra.transform.SetParent(padre, false);
        var fondo = barra.AddComponent<Image>();
        fondo.color = new Color(0.05f, 0.06f, 0.09f, 0.82f);

        var rt = barra.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 24f);
        rt.sizeDelta = new Vector2(940f, 108f);

        etiquetaVista = Etiqueta(barra.transform, new Vector2(-340f, 28f), 240f);
        etiquetaVelocidad = Etiqueta(barra.transform, new Vector2(-90f, 28f), 240f);

        etiquetaPausa = Boton(barra.transform, "Pausa", new Vector2(-400f, -18f), AlternarPausa);
        Boton(barra.transform, "-", new Vector2(-300f, -18f), MasLento);
        Boton(barra.transform, "+", new Vector2(-200f, -18f), MasRapido);
        Boton(barra.transform, "Reiniciar", new Vector2(-70f, -18f), Reiniciar);
        etiquetaFlota = Boton(barra.transform, "Flota: dedicada",
                              new Vector2(145f, -18f), AlternarFlota, 200f);
        Boton(barra.transform, "Cambiar vista", new Vector2(330f, -18f), SiguienteVista);
    }

    Text Etiqueta(Transform padre, Vector2 sitio, float ancho)
    {
        var go = new GameObject("Etiqueta");
        go.transform.SetParent(padre, false);
        var txt = go.AddComponent<Text>();
        txt.font = Tipografia();
        txt.fontSize = 21;
        txt.color = new Color(0.78f, 0.85f, 0.97f);
        txt.alignment = TextAnchor.MiddleLeft;

        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = sitio;
        rt.sizeDelta = new Vector2(ancho, 26f);
        return txt;
    }

    Text Boton(Transform padre, string rotulo, Vector2 sitio,
               UnityEngine.Events.UnityAction alPulsar, float ancho = 0f)
    {
        var go = new GameObject("Boton_" + rotulo);
        go.transform.SetParent(padre, false);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.16f, 0.34f, 0.60f, 0.95f);

        var boton = go.AddComponent<Button>();
        boton.targetGraphic = img;
        boton.onClick.AddListener(alPulsar);

        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = sitio;
        rt.sizeDelta = new Vector2(
            ancho > 0f ? ancho : (rotulo.Length > 6 ? 150f : 90f), 42f);

        var txtGO = new GameObject("Texto");
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.font = Tipografia();
        txt.fontSize = 22;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.text = rotulo;

        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return txt;
    }

    static Font tipografia;

    static Font Tipografia()
    {
        if (tipografia != null) return tipografia;
        tipografia = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (tipografia == null) tipografia = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return tipografia;
    }
}
