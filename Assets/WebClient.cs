
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

public class WebClient : MonoBehaviour
{
    [Header("Servidor")]
    [Tooltip("Host y puerto del servidor Python. Solo la base, sin ruta.")]
    public string baseUrl = "http://127.0.0.1:5055";

    [Tooltip("Segundos entre cada POST /step. Bajalo para que corra mas rapido.")]
    public float stepInterval = 0.4f;

    [Tooltip("Arranca el bucle solo al entrar en Play.")]
    public bool autoStart = true;

    [Tooltip("En pausa deja de pedir pasos al servidor, pero no corta la conexion: " +
             "al despausar sigue la misma corrida. Lo maneja el panel de control.")]
    public bool pausado = false;

    [Tooltip("Pedir /reset en cuanto se conecta, para que cada Play empiece una " +
             "corrida limpia. El servidor Python no se entera de que Unity salio " +
             "de Play y sigue avanzando: sin esto los AGV aparecen a mitad de " +
             "corrida, repartidos y con cajas encima. Nota: el /reset del " +
             "simulador original no restaura la bateria; para eso hay que " +
             "reiniciar el proceso de Python.")]
    public bool reiniciarAlEntrar = true;

    [Min(0.1f)]
    public float intervaloReconexion = 2f;

    [Header("Escena")]
    [Tooltip("Prefab del AGV. Se instancia uno por cada id que mande el servidor.")]
    public GameObject agvPrefab;

    [Tooltip("Raiz del almacen (Prefab_reto). Las coordenadas del servidor son " +
             "locales a este objeto. Si se deja vacia se usa 'worldOrigin'.")]
    public Transform raiz;

    [Tooltip("Solo si 'raiz' esta vacia: posicion de mundo de Prefab_reto, que es " +
             "el origen al que se refieren las coordenadas del servidor.")]
    public Vector3 worldOrigin = new Vector3(45.21769f, 0f, -4.63f);

    [Tooltip("config.UNITY_SCALE del servidor. Hoy vale 1.")]
    public float escala = 1f;

    [Tooltip("AGVs de la escena, en orden: el primero es el AGV 1 del servidor. " +
             "Si se deja vacio se buscan por 'nombresDeAgvs'.")]
    public Transform[] agvsEnEscena;

    [Tooltip("Nombres a buscar en la escena si 'agvsEnEscena' esta vacio, en el " +
             "orden de los ids del servidor: el primero es el AGV 1. Anade mas " +
             "segun se metan a la escena, y sube --agents en el servidor.")]
    public string[] nombresDeAgvs = { "AGV_Aaron" };

    [Tooltip("Completar los AGV que falten clonando el primero que si este en la " +
             "escena (o 'agvPrefab', si se le pone uno). Apagado: los agentes de " +
             "mas se ignoran y solo se avisa en consola.")]
    public bool completarConClones = true;

    [Tooltip("Altura a sumar. El servidor siempre manda y=0 (el suelo); los " +
             "modelos de la escena estan entre 0.05 y 0.4 segun el pivote.")]
    public float alturaY = 0f;

    [Tooltip("Deducir solo hacia donde mira el modelo, usando la horquilla: en un " +
             "montacargas el mastil va delante. Si acierta, 'ajusteDeGiro' sobra.")]
    public bool ajusteAutomatico = true;

    [Tooltip("Grados a sumar en Y. El servidor manda el rumbo suponiendo que el " +
             "modelo mira hacia +Z; el AGV de Aaron mira hacia +X (las horquillas " +
             "salen por ahi), asi que necesita -90. Cambialo en pleno Play hasta " +
             "que el AGV avance de frente.")]
    public float ajusteDeGiro = -90f;

    [Tooltip("Rumbo con el que se pinta un AGV que todavia no ha empezado a " +
             "andar. El servidor manda rotacion 0 mientras no hay siguiente nodo, " +
             "y con la base en el muro norte eso los deja mirando a la pared. En " +
             "cuanto el servidor da un tramo, manda el rumbo de verdad.")]
    public float rumboInicial = 180f;

    [Header("Modelo del AGV")]
    [Tooltip("Recentra el AGV cuando el pivote del prefab no cae en el centro del " +
             "modelo. El de Aaron esta a ~1 m del centro, y sin esto el AGV se " +
             "pinta desplazado y roza las estanterias.")]
    public bool corregirPivote = true;

    [Tooltip("Objeto que sube y baja dentro del AGV (la horquilla).")]
    public string nombreHorquilla = "Arch";

    [Tooltip("Altura local de la horquilla en reposo.")]
    public float alturaHorquillaAbajo = 0.15f;

    [Tooltip("Altura local de la horquilla llevando caja.")]
    public float alturaHorquillaTransporte = 0.5f;

    [Tooltip("Metros por segundo a los que sube y baja la horquilla. Solo se usa " +
             "en los AGV que no tienen Animator.")]
    public float velocidadHorquilla = 1.5f;

    [Tooltip("Estado del Animator que sube la horquilla al recoger.")]
    public string animacionSubir = "animMover";

    [Tooltip("Estado del Animator que baja la horquilla al soltar.")]
    public string animacionBajar = "animAbajo";

    [Header("Anti-choque (solo visual)")]
    [Tooltip("Impide que el AGV se dibuje dentro de una estanteria, una banda, un " +
             "cargador o un muro. El servidor sigue decidiendo igual: esto solo " +
             "corrige donde se pinta el modelo. Hace falta porque 33 de los 62 nodos " +
             "del mapa caen dentro de la geometria: los de estanteria, cargador y " +
             "muelle son posiciones de trabajo, no de aparcamiento.")]
    public bool evitarAtravesar = true;

    [Tooltip("Prefijos de los objetos que el AGV no debe atravesar, buscados dentro " +
             "de 'raiz'. Las cajas quedan fuera a proposito: son el objetivo a recoger. " +
             "Es 'Pared' y no 'ParedFondo' porque la nave tambien tiene ParedIzquierda: " +
             "con el prefijo largo esa pared no entraba en la lista y se colaban por ella.")]
    public string[] obstaculos = { "Cube", "Pallet", "Pared", "Bandas", "cargador", "Obstaculo" };

    [Tooltip("Medio ancho del AGV en metros: la holgura con la que se aparta. " +
             "0.65 sale de medir el modelo en la escena: 1.55 m de largo por 1.31 " +
             "de ancho. Con 0.45 se trataba al montacargas como un circulo de 90 cm " +
             "y la carroceria entraba hasta 33 cm dentro de la estanteria aunque el " +
             "centro quedara fuera: eso era el 'atraviesan un poco'.")]
    public float radioDelAgv = 0.65f;

    [Tooltip("Altura a partir de la cual algo estorba de verdad. Por debajo es piso " +
             "por el que se circula: las plataformas de los muelles miden 0.17 m y " +
             "las de carga 0.10 m, y el AGV tiene que poder subirse encima. " +
             "0.20 y no 0.25 porque las bandas miden 0.24: por un centimetro se " +
             "colaban de la lista y los AGV las cruzaban por encima.")]
    public float alturaMinimaDeObstaculo = 0.20f;

    [Tooltip("Lo maximo que se puede apartar un AGV para salir de un obstaculo. " +
             "Si no cabe en ese margen se deja donde lo puso el servidor.")]
    public float empujeMaximo = 1.5f;

    [Tooltip("Impide que dos AGV se dibujen uno encima del otro. Python ya evita " +
             "que compartan nodo; esto arregla el solape mientras uno cruza su " +
             "tramo, que es cuando se veian atravesarse.")]
    public bool separarEntreAgvs = true;

    [Tooltip("Distancia minima entre los centros de dos AGV, en metros. 1.45 sale " +
             "del modelo: mide 1.55 de largo por 1.31 de ancho, asi que por debajo " +
             "de eso las carrocerias ya se tocan. Los nodos vecinos estan a 2.2 m, " +
             "asi que no estorba a la circulacion normal.")]
    public float separacionMinima = 1.45f;

    [Tooltip("Limitar el AGV al rectangulo del suelo. Los nodos de banda B1-B3 estan " +
             "en z=-7.06 y el suelo acaba en -6.434, asi que sin esto sale por el muro.")]
    public bool limitarAlSuelo = true;

    [Tooltip("Esquina minima de la zona transitable, en coordenadas locales del " +
             "almacen. En x llega a -11.1 y no a los -8.514 del 'floor_bounds' del " +
             "mapa: los muelles M1-M3 estan en x=-9.71, fuera de la nave, y con el " +
             "limite en el borde del piso el AGV no podia entrar a dejar la caja.")]
    public Vector2 sueloMin = new Vector2(-11.1f, -6.434f);

    [Tooltip("Esquina maxima del suelo en coordenadas locales del almacen.")]
    public Vector2 sueloMax = new Vector2(7.644f, 7.566f);

    [Header("Cajas")]
    [Tooltip("Mover las cajas de la escena segun el snapshot. Se enlazan por el " +
             "nombre que manda el servidor en 'unity_object'.")]
    public bool moverCajas = true;

    [Tooltip("Coloca las cajas negras donde diga el servidor y las esconde cuando " +
             "dice que las han retirado. El servidor las reparte por pasillos al " +
             "azar en cada corrida y le veta esos nodos a su A*, asi que los AGV " +
             "las rodean de verdad en vez de que solo les estorben al dibujarse.")]
    public bool obstaculosDinamicos = true;

    [Tooltip("Donde se apoya la caja respecto a la horquilla, en ejes del AGV. " +
             "Con 'apoyarCajaEnHorquilla' puesto, la altura se calcula sola y de " +
             "esto solo se usan X y Z para ajustar el encaje.")]
    public Vector3 desfaseDeLaCaja = new Vector3(0f, 0.1f, 0f);

    [Tooltip("Apoyar la caja sobre la cara de arriba de la horquilla midiendo las " +
             "dos mallas, en vez de fiarlo al desfase fijo. Hace falta porque el " +
             "pivote de las cajas esta en el centro del cubo y no en la base: con " +
             "el desfase de 0.1 la caja quedaba 24 cm por debajo del tope de la " +
             "horquilla, o sea metida dentro del modelo.")]
    public bool apoyarCajaEnHorquilla = true;

    [Tooltip("Prefab de caja a instanciar cuando la escena no tenga el objeto que " +
             "nombra el servidor. Vacio: solo se mueven las cajas que ya existan.")]
    public GameObject cajaPrefab;

    [Tooltip("Material para las cajas creadas en marcha. Vacio: se usa el del " +
             "prefab. Las piezas del almacen son de ProBuilder y su textura la " +
             "reconstruye el Editor, no el runtime: al instanciar una caja en " +
             "Play se queda con el material blanco por defecto de ProBuilder. " +
             "Aqui se reasigna despues de Instantiate para que salga con la " +
             "misma madera que las demas.")]
    public Material materialCaja;

    [Tooltip("Suavizar entre snapshots. El servidor ya interpola dentro del tramo, " +
             "asi que a 10 Hz sobra; ayuda si se pide /step mas despacio.")]
    public bool suavizar = true;

    [Tooltip("Metros a partir de los cuales un cambio de posicion se considera un " +
             "teletransporte (reinicio de corrida) y se aplica de golpe, sin deslizar.")]
    public float saltoMaximo = 3f;

    [Tooltip("Tope de giro en grados por segundo. El servidor puede mandar vueltas " +
             "de 180 grados en un solo paso; sin tope el AGV da un latigazo.")]
    public float gradosPorSegundo = 360f;

    [Header("Linea de entrada")]
    [Tooltip("Posar sobre el riel las cajas recien llegadas y hacerlas entrar " +
             "deslizando. El nodo de banda cae fuera del suelo de la nave, asi " +
             "que la caja se posa sobre el riel medido, no sobre el nodo.")]
    public bool cajasEnLaBanda = true;

    [Tooltip("Prefijo de los rieles dentro de 'raiz'. En esta nave son tres y se " +
             "llaman Bandas, Bandas (1) y Bandas (2).")]
    public string nombreDeLaBanda = "Bandas";

    [Tooltip("Segundos que tarda una caja en recorrer el riel al entrar. Con 0 " +
             "aparece ya puesta en su sitio, sin animacion.")]
    public float duracionDeLlegada = 1.5f;

    [Tooltip("Hueco entre dos cajas en cola en el mismo riel, en metros.")]
    public float separacionEnLaBanda = 0.45f;

    class Vista
    {
        public Transform t;
        public Vector3 desdePos, haciaPos;
        public Quaternion desdeRot, haciaRot;
        public Transform horquilla;
        public Vector3 centroLocal;
        public float ajuste;
        public float alturaHorquilla;
        public float objetivoHorquilla;
        public Animator animator;
        public Transform sube;
        public bool usaAnimator;
        public string ultimaAnimacion;
        public bool haAndado;
    }

    readonly Dictionary<int, Vista> vistas = new Dictionary<int, Vista>();
    readonly HashSet<int> sinVista = new HashSet<int>();
    readonly Dictionary<string, Transform> cajas = new Dictionary<string, Transform>();
    readonly HashSet<string> cajasSinVista = new HashSet<string>();
    readonly List<GameObject> cajasCreadas = new List<GameObject>();

    Bounds[] rieles;
    readonly Dictionary<string, float> entroEnLaBanda = new Dictionary<string, float>();
    readonly Dictionary<int, int> colaDelRiel = new Dictionary<int, int>();

    Coroutine bucle;
    float tInterp;
    float instanteDelUltimo;
    float duracionDelPaso;
    bool peticionEnCurso;
    string ultimoError;
    public bool Conectado { get; private set; }

    public Snapshot Ultimo { get; private set; }

    public event System.Action<Snapshot> AlActualizar;

    void OnEnable()
    {
        volumenes = null;
        rieles = null;
        if (autoStart) Conectar();
    }

    void OnDisable() { Detener(); }

    void Update()
    {

        float duracion = duracionDelPaso > 0.01f ? duracionDelPaso : stepInterval;
        tInterp = (suavizar && duracion > 0f)
            ? Mathf.Min(1f, tInterp + Time.deltaTime / duracion)
            : 1f;

        foreach (var v in vistas.Values)
        {
            if (v.t == null) continue;

            Quaternion objetivo = Quaternion.Slerp(v.desdeRot, v.haciaRot, tInterp);
            Quaternion giro = gradosPorSegundo > 0f
                ? Quaternion.RotateTowards(v.t.rotation, objetivo,
                                           gradosPorSegundo * Time.deltaTime)
                : objetivo;
            Vector3 centro = Vector3.Lerp(v.desdePos, v.haciaPos, tInterp);

            v.t.SetPositionAndRotation(centro - giro * v.centroLocal, giro);

            if (v.horquilla != null && !v.usaAnimator)
            {
                v.alturaHorquilla = Mathf.MoveTowards(
                    v.alturaHorquilla, v.objetivoHorquilla,
                    velocidadHorquilla * Time.deltaTime);
                Vector3 lp = v.horquilla.localPosition;
                lp.y = v.alturaHorquilla;
                v.horquilla.localPosition = lp;
            }
        }

        if (separarEntreAgvs) SepararAgvs();

        if (moverCajas) ColocaLasCajas();

        if (obstaculosDinamicos) MuestraLosObstaculos();

        Atajos();
    }

    void SepararAgvs()
    {
        var cuerpos = new List<Vista>();
        foreach (var v in vistas.Values)
            if (v.t != null) cuerpos.Add(v);

        float minimo = separacionMinima;
        for (int i = 0; i < cuerpos.Count; i++)
            for (int j = i + 1; j < cuerpos.Count; j++)
            {
                Vector3 a = cuerpos[i].t.position, b = cuerpos[j].t.position;
                Vector3 d = b - a;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist >= minimo) continue;

                Vector3 dir = dist > 0.001f ? d / dist : Vector3.right;
                Vector3 empuje = dir * ((minimo - dist) * 0.5f);

                cuerpos[i].t.position = DentroDelSuelo(a - empuje);
                cuerpos[j].t.position = DentroDelSuelo(b + empuje);
            }
    }

    void Atajos()
    {
        var teclado = Keyboard.current;
        if (teclado == null) return;

        if (teclado.rKey.wasPressedThisFrame) Reiniciar();
        if (teclado.gKey.wasPressedThisFrame) GiraNoventa();
    }

    [ContextMenu("Girar 90 grados")]
    public void GiraNoventa()
    {
        ajusteAutomatico = false;
        ajusteDeGiro = Mathf.Repeat(ajusteDeGiro + 90f, 360f);
        if (ajusteDeGiro > 180f) ajusteDeGiro -= 360f;
        Debug.Log($"[WebClient] ajusteDeGiro = {ajusteDeGiro}. Cuando el AGV avance " +
                  "de frente, para el Play y copia ese numero al Inspector.");

        if (Ultimo?.agents != null)
            foreach (var a in Ultimo.agents)
                if (vistas.TryGetValue(a.id, out var v) && v.t != null)
                {
                    v.ajuste = ajusteDeGiro;
                    v.desdeRot = v.haciaRot = GiroDeMundo(v, a);
                    v.t.rotation = v.haciaRot;
                }
    }

    [ContextMenu("Reiniciar corrida")]
    public void Reiniciar() { StartCoroutine(Reset()); }

    [ContextMenu("Conectar / continuar")]
    public void Conectar()
    {
        if (bucle == null && isActiveAndEnabled)
            bucle = StartCoroutine(BucleDePasos());
    }

    IEnumerator BucleDePasos()
    {

        bool reiniciePendiente = reiniciarAlEntrar;

        while (true)
        {
            if (Conectado && reiniciePendiente)
            {
                reiniciePendiente = false;
                yield return Reset();
                continue;
            }
            if (pausado)
            {

                yield return new WaitForSecondsRealtime(0.1f);
                continue;
            }

            if (Conectado) yield return Step();
            else yield return State();
            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.02f, Conectado ? stepInterval : intervaloReconexion));
        }
    }

    public void Detener()
    {
        StopAllCoroutines();
        bucle = null;
        peticionEnCurso = false;
        Conectado = false;
    }

    public IEnumerator State()
    {
        yield return Pedir("GET", "/state", null, Aplicar);
    }

    public IEnumerator Step()
    {
        yield return Pedir("POST", "/step", "{}", Aplicar);
    }

    public IEnumerator Reset()
    {
        LimpiaLasCajas();
        yield return Pedir("POST", "/reset", "{}", null);
        yield return State();
    }

    void LimpiaLasCajas()
    {
        foreach (var go in cajasCreadas)
        {
            if (go == null) continue;

            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null) Destroy(f.sharedMesh);

            Destroy(go);
        }

        cajasCreadas.Clear();
        cajas.Clear();
        cajasSinVista.Clear();
    }

    public IEnumerator Mode(string modo)
    {
        yield return Pedir("POST", "/mode", "{\"mode\":\"" + modo + "\"}", null);
        yield return State();
    }

    public IEnumerator Fleet(string reparto)
    {
        LimpiaLasCajas();
        yield return Pedir("POST", "/fleet", "{\"fleet\":\"" + reparto + "\"}", null);
        yield return State();
    }

    public void CambiarFlota(string reparto) { StartCoroutine(Fleet(reparto)); }

    public string FlotaActual
    {
        get { return Ultimo?.fleet != null ? Ultimo.fleet.mode : ""; }
    }

    public string OtraFlota()
    {
        var f = Ultimo?.fleet;
        if (f == null || f.modes == null || f.modes.Length == 0) return "";
        for (int i = 0; i < f.modes.Length; i++)
            if (f.modes[i] == f.mode)
                return f.modes[(i + 1) % f.modes.Length];
        return f.modes[0];
    }

    IEnumerator Pedir(string metodo, string ruta, string cuerpo, System.Action<string> alRecibir)
    {

        while (peticionEnCurso) yield return null;
        peticionEnCurso = true;
        try
        {
            string url = baseUrl.TrimEnd('/') + ruta;

            using (var www = new UnityWebRequest(url, metodo))
            {
                if (cuerpo != null)
                {
                    www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(cuerpo));
                    www.SetRequestHeader("Content-Type", "application/json");
                }
                www.downloadHandler = new DownloadHandlerBuffer();
                www.timeout = 10;

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    AvisarError($"{url}: {www.error} ({www.responseCode}). " +
                                "Ejecuta Iniciar_AGV.command; se reintentara automaticamente.");
                    yield break;
                }

                string texto = www.downloadHandler.text;
                alRecibir?.Invoke(texto);
            }
        }
        finally { peticionEnCurso = false; }
    }

    void AvisarError(string mensaje)
    {
        Conectado = false;
        if (ultimoError != mensaje) Debug.LogWarning("[WebClient] " + mensaje);
        ultimoError = mensaje;
    }

    void Aplicar(string json)
    {
        Snapshot s;
        try
        {
            s = JsonUtility.FromJson<Snapshot>(json);
        }
        catch (System.Exception e)
        {
            AvisarError($"No pude leer el snapshot: {e.Message}");
            return;
        }

        if (s == null || s.agents == null || s.agents.Length == 0)
        {
            AvisarError("El servidor no devolvio un snapshot con agentes.");
            return;
        }

        bool reinicio = Ultimo == null || s.step < Ultimo.step ||
            (s.stats != null && Ultimo.stats != null && s.stats.run != Ultimo.stats.run);

        if (reinicio) entroEnLaBanda.Clear();
        if (!Conectado)
            Debug.Log($"[WebClient] Conectado a {baseUrl}: {s.agents.Length} AGV(s), " +
                      $"{(s.boxes == null ? 0 : s.boxes.Length)} cajas, modo {s.mode}.");
        Conectado = true;
        ultimoError = null;
        Ultimo = s;

        foreach (var a in s.agents)
        {
            Vista v = VistaDe(a.id);
            if (v == null) continue;

            v.desdePos = v.t.position + v.t.rotation * v.centroLocal;
            v.desdeRot = v.t.rotation;
            v.haciaPos = SinAtravesar(AMundo(a), v.desdePos);
            if (!string.IsNullOrEmpty(a.next_node)) v.haAndado = true;
            v.haciaRot = v.haAndado
                ? GiroDeMundo(v, a)
                : RumboDeSalida(v);
            v.objetivoHorquilla = ObjetivoHorquilla(a, s);
            AnimaLaHorquilla(v, a);

            if (reinicio || Vector3.Distance(v.desdePos, v.haciaPos) > saltoMaximo)
            {
                v.desdePos = v.haciaPos;
                v.desdeRot = v.haciaRot;
                v.t.SetPositionAndRotation(v.haciaPos - v.haciaRot * v.centroLocal,
                                           v.haciaRot);
            }
        }

        float ahora = Time.time;
        if (instanteDelUltimo > 0f)
        {
            float dt = ahora - instanteDelUltimo;
            duracionDelPaso = duracionDelPaso <= 0f ? dt : Mathf.Lerp(duracionDelPaso, dt, 0.3f);
        }
        instanteDelUltimo = ahora;

        tInterp = 0f;
        AlActualizar?.Invoke(s);
    }

    Vector3 AMundo(AgentState a)
    {
        Vector3 local = new Vector3(a.x, a.y, a.z) * escala + Vector3.up * alturaY;
        return raiz != null ? raiz.TransformPoint(local) : worldOrigin + local;
    }

    Bounds[] volumenes;

    void PrepararObstaculos()
    {
        var lista = new List<Bounds>();
        Transform padre = raiz != null ? raiz : transform;

        foreach (var r in padre.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (EsDeUnAgv(r.transform)) continue;
            if (!EsObstaculo(r.transform.name)) continue;

            Bounds b = r.bounds;
            if (b.size.x > 25f || b.size.z > 25f) continue;

            if (b.max.y < alturaMinimaDeObstaculo) continue;
            lista.Add(b);
        }

        volumenes = lista.ToArray();
        Debug.Log($"[WebClient] Anti-choque: {volumenes.Length} obstaculos medidos.");
    }

    static bool EsDeUnAgv(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (p.name.StartsWith("AGV") || p.name.StartsWith("AGB")) return true;
        return false;
    }

    bool EsObstaculo(string nombre)
    {
        if (obstaculos == null) return false;
        foreach (var prefijo in obstaculos)
            if (!string.IsNullOrEmpty(prefijo) && nombre.StartsWith(prefijo)) return true;
        return false;
    }

    Vector3 SinAtravesar(Vector3 mundo, Vector3 referencia)
    {
        if (!evitarAtravesar) return mundo;
        if (volumenes == null) PrepararObstaculos();

        mundo = DentroDelSuelo(mundo);

        for (int pasada = 0; pasada < 4; pasada++)
        {
            Bounds? choque = null;
            foreach (var b in volumenes)
                if (Solapa(b, mundo)) { choque = b; break; }
            if (choque == null) break;

            Vector3 salida = MejorSalida(choque.Value, mundo, referencia);
            if (salida == Vector3.zero) break;
            mundo = DentroDelSuelo(mundo + salida);
        }
        return mundo;
    }

    Vector3 DentroDelSuelo(Vector3 mundo)
    {
        if (!limitarAlSuelo || raiz == null) return mundo;

        Vector3 local = raiz.InverseTransformPoint(mundo);
        local.x = Mathf.Clamp(local.x, sueloMin.x + radioDelAgv, sueloMax.x - radioDelAgv);
        local.z = Mathf.Clamp(local.z, sueloMin.y + radioDelAgv, sueloMax.y - radioDelAgv);
        return raiz.TransformPoint(local);
    }

    bool CabeEnElSuelo(Vector3 mundo)
    {
        if (!limitarAlSuelo || raiz == null) return true;

        Vector3 local = raiz.InverseTransformPoint(mundo);
        return local.x >= sueloMin.x + radioDelAgv && local.x <= sueloMax.x - radioDelAgv &&
               local.z >= sueloMin.y + radioDelAgv && local.z <= sueloMax.y - radioDelAgv;
    }

    bool Solapa(Bounds b, Vector3 p)
    {
        return p.x > b.min.x - radioDelAgv && p.x < b.max.x + radioDelAgv &&
               p.z > b.min.z - radioDelAgv && p.z < b.max.z + radioDelAgv;
    }

    Vector3 MejorSalida(Bounds b, Vector3 p, Vector3 referencia)
    {
        var lados = new Vector3[4];
        lados[0] = new Vector3((b.min.x - radioDelAgv) - p.x, 0f, 0f);
        lados[1] = new Vector3((b.max.x + radioDelAgv) - p.x, 0f, 0f);
        lados[2] = new Vector3(0f, 0f, (b.min.z - radioDelAgv) - p.z);
        lados[3] = new Vector3(0f, 0f, (b.max.z + radioDelAgv) - p.z);

        Vector3 mejor = Vector3.zero;
        float mejorCoste = float.MaxValue;

        foreach (var d in lados)
        {
            float largo = d.magnitude;
            if (largo > empujeMaximo) continue;
            if (!CabeEnElSuelo(p + d)) continue;

            float coste = largo + 0.5f * Vector3.Distance(p + d, referencia);
            if (coste < mejorCoste) { mejorCoste = coste; mejor = d; }
        }
        return mejor;
    }

    Quaternion RumboDeSalida(Vista v)
    {

        Quaternion local = Quaternion.Euler(0f, rumboInicial + v.ajuste, 0f);
        return raiz != null ? raiz.rotation * local : local;
    }

    Quaternion GiroDeMundo(Vista v, AgentState a)
    {
        Quaternion local = Quaternion.Euler(0f, a.rotation + v.ajuste, 0f);
        return raiz != null ? raiz.rotation * local : local;
    }

    float AjusteDe(Transform t, Transform horquilla, Vector3 centroLocal)
    {
        if (!ajusteAutomatico || horquilla == null) return ajusteDeGiro;

        Vector3 frente = t.InverseTransformPoint(horquilla.position) - centroLocal;
        frente.y = 0f;
        if (frente.sqrMagnitude < 0.01f) return ajusteDeGiro;

        float grados = -Mathf.Atan2(frente.x, frente.z) * Mathf.Rad2Deg;
        return Mathf.Round(grados / 90f) * 90f;
    }

    Vista VistaDe(int id)
    {
        if (vistas.TryGetValue(id, out var v) && v.t != null) return v;
        if (sinVista.Contains(id)) return null;

        Transform t = BuscaEnLaEscena(id) ?? Instancia(id);
        if (t == null)
        {
            sinVista.Add(id);
            Debug.LogWarning($"[WebClient] El AGV {id} no tiene cuerpo en la escena y " +
                             "no se pudo clonar ninguno. Anadelo a 'agvsEnEscena', " +
                             "ponle un 'agvPrefab', o arranca el servidor con menos " +
                             "agentes (--agents).");
            return null;
        }

        Transform horquilla = BuscaHijo(t, nombreHorquilla);
        Vector3 centroReal = CentroDelModelo(t);
        Vector3 centro = corregirPivote ? Vector3.Scale(centroReal, t.lossyScale) : Vector3.zero;
        float ajuste = AjusteDe(t, horquilla, centroReal);
        Debug.Log($"[WebClient] AGV {id}: ajuste de giro {ajuste} grados" +
                  (ajusteAutomatico && horquilla != null ? " (deducido de la horquilla)" : ""));

        v = new Vista
        {
            t = t,
            desdePos = t.position,
            haciaPos = t.position,
            desdeRot = t.rotation,
            haciaRot = t.rotation,
            horquilla = horquilla,
            centroLocal = centro,
            alturaHorquilla = horquilla != null ? horquilla.localPosition.y : 0f,
            objetivoHorquilla = alturaHorquillaAbajo,
            animator = t.GetComponentInChildren<Animator>(true),
            ajuste = ajuste,
        };
        v.usaAnimator = TieneLosClips(v.animator);
        // Con Animator sube el objeto animado ('garras'); sin el, la horquilla del lerp
        // ('Arch'). El rumbo se sigue deduciendo de 'Arch' pase lo que pase.
        v.sube = v.usaAnimator ? v.animator.transform : horquilla;
        if (horquilla == null && !string.IsNullOrEmpty(nombreHorquilla))
            Debug.LogWarning($"[WebClient] El AGV {id} no tiene ningun hijo " +
                             $"'{nombreHorquilla}': no habra animacion de horquilla.");

        vistas[id] = v;
        return v;
    }

    // Un Animator con el controller vacio no mueve nada, asi que en ese caso hay que
    // dejarle la horquilla al lerp en vez de quedarnosla y congelarla.
    bool TieneLosClips(Animator animator)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        return animator.HasState(0, Animator.StringToHash(animacionSubir))
            || animator.HasState(0, Animator.StringToHash(animacionBajar));
    }

    // El AGV con los clips puestos los usa; el que no, sigue con el lerp.
    void AnimaLaHorquilla(Vista v, AgentState a)
    {
        if (!v.usaAnimator) return;

        // Igual que hacia el lerp: arriba mientras lleve caja (o la este recogiendo),
        // abajo en cuanto la suelta. Asi se queda quieta en su sitio entre misiones.
        bool arriba = a.state == "picking" || !string.IsNullOrEmpty(a.carrying);
        string estado = arriba ? animacionSubir : animacionBajar;

        if (estado == v.ultimaAnimacion) return;

        v.animator.Play(estado, 0, 0f);
        v.ultimaAnimacion = estado;
    }

    float ObjetivoHorquilla(AgentState a, Snapshot s)
    {
        if (a.state == "picking" || a.state == "dropping")
        {
            BoxState caja = Caja(s, a.box);
            if (caja != null) return Mathf.Max(caja.y, alturaHorquillaAbajo);
        }
        if (!string.IsNullOrEmpty(a.carrying)) return alturaHorquillaTransporte;
        return alturaHorquillaAbajo;
    }

    static BoxState Caja(Snapshot s, string id)
    {
        if (s.boxes == null || string.IsNullOrEmpty(id)) return null;
        foreach (var caja in s.boxes)
            if (caja.id == id) return caja;
        return null;
    }

    static float CaraDeArriba(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r != null ? r.bounds.max.y : t.position.y;
    }

    static float DelPivoteALaBase(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r != null ? t.position.y - r.bounds.min.y : 0f;
    }

    void ColocaLasCajas()
    {
        Snapshot s = Ultimo;
        if (s == null || s.boxes == null) return;

        colaDelRiel.Clear();

        foreach (var caja in s.boxes)
        {
            Transform t = VistaDeCaja(caja);
            if (t == null) continue;

            Vista porta = caja.status == "IN_TRANSIT" ? QuienLaLleva(s, caja.id) : null;
            if (porta != null && porta.sube != null)
            {
                Vector3 sitio = porta.sube.position + porta.t.rotation * desfaseDeLaCaja;
                if (apoyarCajaEnHorquilla)
                    sitio.y = CaraDeArriba(porta.sube) + DelPivoteALaBase(t);
                t.SetPositionAndRotation(sitio, porta.t.rotation);
            }
            else if (caja.conveyor && cajasEnLaBanda && PonEnLaBanda(caja, t))
            {

            }
            else
            {
                t.position = AMundoCaja(caja);
            }
        }
    }

    void MuestraLosObstaculos()
    {
        Snapshot s = Ultimo;
        if (s == null || s.obstacles == null) return;

        // Se guardan la primera vez: GameObject.Find no ve lo desactivado.
        if (cajasNegras == null)
        {
            cajasNegras = new Dictionary<string, GameObject>();
            foreach (var o in s.obstacles)
            {
                if (string.IsNullOrEmpty(o.id) || cajasNegras.ContainsKey(o.id)) continue;
                GameObject go = GameObject.Find(o.id);
                if (go != null) cajasNegras[o.id] = go;
            }
        }

        bool cambio = false;

        foreach (var o in s.obstacles)
        {
            if (string.IsNullOrEmpty(o.id)) continue;
            if (!cajasNegras.TryGetValue(o.id, out GameObject go) || go == null) continue;

            if (go.activeSelf != o.active) { go.SetActive(o.active); cambio = true; }
            if (!o.active) continue;

            Vector3 sitio = AMundoObstaculo(o, go.transform.position.y);
            if ((go.transform.position - sitio).sqrMagnitude > 1e-6f)
            {
                go.transform.position = sitio;
                cambio = true;
            }
        }

        // A null para remedir: si un obstaculo se mueve, los volumenes ya no valen.
        if (cambio) volumenes = null;
    }

    // El servidor manda el pasillo, no la altura: esa la pone la escena.
    Vector3 AMundoObstaculo(ObstacleState o, float alturaEnEscena)
    {
        Vector3 local = new Vector3(o.x, 0f, o.z) * escala;
        Vector3 mundo = raiz != null ? raiz.TransformPoint(local) : worldOrigin + local;
        mundo.y = alturaEnEscena;
        return mundo;
    }

    Dictionary<string, GameObject> cajasNegras;

    void PreparaLosRieles()
    {
        var lista = new List<Bounds>();
        Transform padre = raiz != null ? raiz : transform;

        foreach (var t in padre.GetComponentsInChildren<Transform>(false))
        {
            if (!t.name.StartsWith(nombreDeLaBanda)) continue;
            if (EsDeUnAgv(t)) continue;

            var mallas = t.GetComponentsInChildren<MeshRenderer>(false);
            if (mallas.Length == 0) continue;

            Bounds b = mallas[0].bounds;
            for (int i = 1; i < mallas.Length; i++) b.Encapsulate(mallas[i].bounds);
            lista.Add(b);
        }

        lista.Sort((a, b) => a.center.x.CompareTo(b.center.x));
        rieles = lista.ToArray();
        Debug.Log($"[WebClient] Linea de entrada: {rieles.Length} riel(es) medidos.");
    }

    int RielDeLaCaja(BoxState caja)
    {
        if (rieles.Length == 0) return -1;

        int numero = NumeroFinal(caja.node);
        if (numero >= 1 && numero <= rieles.Length) return numero - 1;
        return RielMasCerca(AMundoCaja(caja));
    }

    static int NumeroFinal(string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return 0;

        int i = nombre.Length;
        while (i > 0 && char.IsDigit(nombre[i - 1])) i--;
        if (i == nombre.Length) return 0;
        return int.TryParse(nombre.Substring(i), out int n) ? n : 0;
    }

    int RielMasCerca(Vector3 mundo)
    {
        int mejor = -1;
        float mejorDistancia = float.MaxValue;
        for (int i = 0; i < rieles.Length; i++)
        {
            Vector3 d = rieles[i].center - mundo;
            d.y = 0f;
            if (d.sqrMagnitude < mejorDistancia) { mejorDistancia = d.sqrMagnitude; mejor = i; }
        }
        return mejor;
    }

    bool PonEnLaBanda(BoxState caja, Transform t)
    {
        if (rieles == null) PreparaLosRieles();
        if (rieles.Length == 0) return false;

        int i = RielDeLaCaja(caja);
        if (i < 0) return false;

        Bounds riel = rieles[i];
        colaDelRiel.TryGetValue(i, out int turno);
        colaDelRiel[i] = turno + 1;

        bool porX = riel.size.x >= riel.size.z;
        Vector3 eje = porX ? Vector3.right : Vector3.forward;
        float largo = porX ? riel.size.x : riel.size.z;

        Vector3 centroDeLaNave = raiz != null ? raiz.position : transform.position;
        Vector3 haciaDentro = centroDeLaNave - riel.center;
        haciaDentro.y = 0f;
        float sentido = Vector3.Dot(haciaDentro, eje) >= 0f ? 1f : -1f;

        float mediaCaja = DelPivoteALaBase(t);
        float avanceEnElRiel = largo * 0.5f - separacionEnLaBanda * 0.5f
                             - turno * separacionEnLaBanda;

        Vector3 sitio = riel.center + eje * (sentido * avanceEnElRiel);
        sitio.y = riel.max.y + mediaCaja;

        Vector3 entrada = sitio - eje * (sentido * (largo + separacionEnLaBanda));

        if (!entroEnLaBanda.TryGetValue(caja.id, out float cuando))
        {
            cuando = Time.time;
            entroEnLaBanda[caja.id] = cuando;
        }
        float recorrido = duracionDeLlegada > 0.01f
            ? Mathf.Clamp01((Time.time - cuando) / duracionDeLlegada)
            : 1f;

        t.SetPositionAndRotation(Vector3.Lerp(entrada, sitio, recorrido),
                                 raiz != null ? raiz.rotation : Quaternion.identity);
        return true;
    }

    Vista QuienLaLleva(Snapshot s, string idCaja)
    {
        foreach (var a in s.agents)
            if (a.carrying == idCaja && vistas.TryGetValue(a.id, out var v) && v.t != null)
                return v;
        return null;
    }

    Vector3 AMundoCaja(BoxState caja)
    {
        Vector3 local = new Vector3(caja.x, caja.y, caja.z) * escala;
        return raiz != null ? raiz.TransformPoint(local) : worldOrigin + local;
    }

    Transform VistaDeCaja(BoxState caja)
    {
        if (string.IsNullOrEmpty(caja.id)) return null;
        if (cajas.TryGetValue(caja.id, out var t) && t != null) return t;
        if (cajasSinVista.Contains(caja.id)) return null;

        if (!string.IsNullOrEmpty(caja.unity_object))
        {
            GameObject go = GameObject.Find(caja.unity_object);
            if (go != null)
            {
                cajas[caja.id] = go.transform;
                return go.transform;
            }
        }

        if (cajaPrefab != null)
        {
            GameObject nueva = Instantiate(cajaPrefab);
            nueva.name = "Caja_" + caja.id;
            nueva.transform.SetParent(raiz != null ? raiz : transform, true);
            PreparaLaCaja(nueva);
            cajasCreadas.Add(nueva);
            cajas[caja.id] = nueva.transform;
            return nueva.transform;
        }

        cajasSinVista.Add(caja.id);
        Debug.LogWarning($"[WebClient] La caja {caja.id} no tiene objeto " +
                         $"'{caja.unity_object}' en la escena y no hay 'cajaPrefab'.");
        return null;
    }

    void PreparaLaCaja(GameObject caja)
    {
        var filtros = caja.GetComponentsInChildren<MeshFilter>(true);
        var mallas = new Mesh[filtros.Length];
        for (int i = 0; i < filtros.Length; i++)
            mallas[i] = filtros[i].sharedMesh != null
                      ? Instantiate(filtros[i].sharedMesh)
                      : null;

        foreach (var c in caja.GetComponentsInChildren<Component>(true))
            if (c != null && c.GetType().Namespace != null &&
                c.GetType().Namespace.StartsWith("UnityEngine.ProBuilder"))
                Destroy(c);

        for (int i = 0; i < filtros.Length; i++)
        {
            if (mallas[i] == null) continue;
            filtros[i].sharedMesh = mallas[i];
            var col = filtros[i].GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = mallas[i];
        }

        Material material = materialCaja;
        if (material == null && cajaPrefab != null)
        {
            var modelo = cajaPrefab.GetComponentInChildren<Renderer>(true);
            if (modelo != null) material = modelo.sharedMaterial;
        }
        if (material == null) return;

        foreach (var r in caja.GetComponentsInChildren<Renderer>(true))
        {
            var copia = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < copia.Length; i++) copia[i] = material;
            r.sharedMaterials = copia;
        }
    }

    static Vector3 CentroDelModelo(Transform t)
    {
        var mallas = t.GetComponentsInChildren<Renderer>();
        if (mallas.Length == 0) return Vector3.zero;

        Bounds caja = new Bounds(t.InverseTransformPoint(mallas[0].bounds.center), Vector3.zero);
        foreach (var malla in mallas)
        {
            caja.Encapsulate(t.InverseTransformPoint(malla.bounds.min));
            caja.Encapsulate(t.InverseTransformPoint(malla.bounds.max));
        }

        Vector3 centro = caja.center;
        centro.y = 0f;
        return centro;
    }

    Transform BuscaEnLaEscena(int id)
    {
        int indice = id - 1;
        if (indice < 0) return null;

        if (agvsEnEscena != null && indice < agvsEnEscena.Length && agvsEnEscena[indice] != null)
            return agvsEnEscena[indice];

        if (nombresDeAgvs != null && indice < nombresDeAgvs.Length)
        {
            string nombre = nombresDeAgvs[indice];
            if (!string.IsNullOrEmpty(nombre))
            {
                GameObject go = GameObject.Find(nombre);
                if (go != null) return go.transform;
            }
        }
        return null;
    }

    static Transform BuscaHijo(Transform raizAgv, string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return null;
        foreach (var hijo in raizAgv.GetComponentsInChildren<Transform>(true))
            if (hijo.name == nombre) return hijo;
        return null;
    }

    Transform Instancia(int id)
    {
        if (!completarConClones) return null;

        GameObject molde = agvPrefab != null ? agvPrefab : PrimerCuerpo();
        if (molde == null) return null;

        GameObject go = Instantiate(molde);
        go.name = "AGV_" + id;
        go.transform.SetParent(raiz != null ? raiz : transform, true);
        go.SetActive(true);

        foreach (var luz in go.GetComponentsInChildren<Light>(true))
            luz.enabled = false;

        return go.transform;
    }

    GameObject PrimerCuerpo()
    {
        foreach (var v in vistas.Values)
            if (v.t != null) return v.t.gameObject;
        return null;
    }

    [System.Serializable]
    public class Snapshot
    {
        public int step;
        public string mode;
        public AgentState[] agents;
        public BoxState[] boxes;
        public Stats stats;
        public FleetState fleet;
        public ObstacleState[] obstacles;
    }

    [System.Serializable]
    public class ObstacleState
    {
        public string id;
        public bool active;
        public float x, z;
    }

    [System.Serializable]
    public class FleetState
    {

        public string mode;

        public string[] modes;

        public int[] dedicated;

        public float weight_box;
        public float weight_wait;

        public int belt_total;
        public int belt_arrived;

        public int belt_pending;

        public int belt_oldest;

        public float belt_bonus;

        public int belt_picked;
        public float belt_wait_avg;
        public int belt_wait_max;

        public int belt_stored;
        public float belt_cycle_avg;

        public int dock_done;
        public float dock_cycle_avg;

        public int[] per_agv;
    }

    [System.Serializable]
    public class AgentState
    {
        public int id;
        public float x, y, z;
        public float rotation;
        public string state;
        public string node;
        public string next_node;
        public string[] path;

        public int wait_time;
        public string action;
        public bool blocked;
        public string leg;
        public string mission;
        public string box;
        public string destination;
        public string carrying;
        public int busy;
        public float battery;
    }

    [System.Serializable]
    public class BoxState
    {
        public string id;
        public string node;
        public int level;
        public string status;
        public string mission;
        public string unity_object;
        public bool conveyor;
        public float x, y, z;
    }

    [System.Serializable]
    public class Stats
    {
        public int run;
        public string policy;
        public int conflicts;
        public int deadlocks;
        public int waiting;
        public int total_wait_time;
        public string finished_reason;
        public int picked;
        public int delivered;
        public int missions_pending;
        public int missions_total;
        public int charges;
    }
}
