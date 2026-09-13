using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConstruirEntornoAlmacen
{
    const int ANCHO = 20;
    const int ALTO  = 18;
    const float Y_SUELO = 0.1f;
    const string RUTA_ESCENA = "Assets/Scenes/Almacen_AGV.unity";
    const int SEMILLA = 42;

    struct Zona
    {
        public string id; public float x, y, w, h;
        public Zona(string id, float x, float y, float w, float h)
        { this.id = id; this.x = x; this.y = y; this.w = w; this.h = h; }
    }

    static readonly Zona[] LINEAS_PRODUCCION =
    {
        new Zona("PL-1", 1, 15, 4, 1), new Zona("PL-2", 1, 11, 4, 1),
        new Zona("PL-3", 1,  7, 4, 1), new Zona("PL-4", 1,  3, 4, 1),
    };

    static readonly Zona[] RACKS =
    {
        new Zona("R-A1",  7, 10, 1, 5), new Zona("R-A2",  7, 3, 1, 5),
        new Zona("R-B1", 10, 10, 1, 5), new Zona("R-B2", 10, 3, 1, 5),
        new Zona("R-C1", 13, 10, 1, 5), new Zona("R-C2", 13, 3, 1, 5),
    };

    static readonly Zona[] TRUCK_DOCKS =
    {
        new Zona("DOCK-1", 16, 14, 3, 2), new Zona("DOCK-2", 16, 9, 3, 2),
        new Zona("DOCK-3", 16,  4, 3, 2),
    };

    static readonly Zona[] ESTACIONES_CARGA =
    {
        new Zona("CS-1", 5, 1, 1, 1), new Zona("CS-2", 15, 1, 1, 1),
        new Zona("CS-3", 15, 16, 1, 1),
    };

    static readonly Zona[] OBSTACULOS =
    {
        new Zona("OFICINA", 1, 1, 3, 1),   new Zona("MANTTO", 17, 1, 2, 1),
        new Zona("COLUMNA-1", 5, 8, 1, 1), new Zona("COLUMNA-2", 14, 8, 1, 1),
    };

    static readonly (string id, int x, int y)[] PALLETS =
    {
        ("P-PL1", 5, 15), ("P-PL2", 5, 11), ("P-PL3", 5, 7), ("P-PL4", 5, 3),
        ("P-RA", 8, 12), ("P-RB", 11, 5), ("P-RC", 14, 12),
        ("P-D1", 15, 14), ("P-D2", 15, 9), ("P-D3", 15, 4),
    };

    static readonly (string id, int x, int y, float bat, string color)[] AGVS =
    {
        ("AGV-1",  6,  1, 100f, "#E24A33"), ("AGV-2", 12,  1, 85f, "#348ABD"),
        ("AGV-3", 12, 16,  60f, "#8EBA42"), ("AGV-4",  9,  8, 40f, "#FBC15E"),
        ("AGV-5",  6, 16, 100f, "#B07AA1"),
    };

    const string P_PISO     = "Assets/Prefab/Piso.prefab";
    const string P_ESTANTE  = "Assets/Prefab/Estanteria.prefab";
    const string P_CAJA     = "Assets/Prefab/Reto/Caja_Reto.prefab";
    const string P_PALLETM  = "Assets/Prefab/Reto/PalletMadera_Reto.prefab";
    const string P_DOCK     = "Assets/Prefab/Reto/PlataformaDock_Reto.prefab";
    const string P_CARGADOR = "Assets/Prefab/Reto/Cargador_Reto.prefab";
    const string P_BANDA    = "Assets/Prefab/Reto/Banda_Reto.prefab";
    const string P_PARED    = "Assets/Prefab/Reto/Pared_Reto.prefab";
    const string P_CAJA_OBS = "Assets/Prefab/Caja.prefab";
    const string P_AGV      = "Assets/Texturas/AGV_Aaron.prefab";

    const float EST_LARGO = 1.25f;
    const float EST_FONDO = 0.45f;
    const float NIVEL_BAJO = 0.12f;
    const float NIVEL_ALTO = 0.82f;

    const int RACK_HILERAS = 2;
    const int RACK_LARGO   = 4;
    const int BANDAS_POR_LINEA = 4;

    const float AGV_LARGO = 1.32f;
    const float AGV_ANCHO = 0.89f;

    const string C_OBST   = "#4D4D4D";
    const string DIR_MATS = "Assets/Materials_Entorno";

    [MenuItem("Reto AGV/Construir entorno de almacen")]
    public static void Construir()
    {
        var escena = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                 NewSceneMode.Single);
        Random.InitState(SEMILLA);

        var raiz = new GameObject("ENTORNO_ALMACEN");
        var gSuelo   = Padre("Suelo_y_Paredes", raiz);
        var gLineas  = Padre("LineasProduccion", raiz);
        var gRacks   = Padre("Racks", raiz);
        var gDocks   = Padre("TruckDocks", raiz);
        var gCarga   = Padre("EstacionesCarga", raiz);
        var gObst    = Padre("Obstaculos", raiz);
        var gPallets = Padre("PosicionesPallet", raiz);
        var gAgvs    = Padre("AGVs", raiz);

        ConstruirLineas(gLineas);
        ConstruirRacks(gRacks);
        ConstruirDocks(gDocks);
        ConstruirCargadores(gCarga);
        ConstruirObstaculos(gObst);
        ConstruirPosicionesPallet(gPallets);
        ConstruirAgvs(gAgvs);

        ConstruirSuelo(gSuelo);
        ConstruirParedes(gSuelo);

        CrearCamara();
        AjustarLuz();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(escena, RUTA_ESCENA);
        AssetDatabase.Refresh();

        Debug.Log($"[Reto AGV] Entorno construido en {RUTA_ESCENA}. Grid {ANCHO}x{ALTO}, 1 celda = 1 unidad, piezas a tamano nativo.");
    }

    static void ConstruirLineas(GameObject padre)
    {
        foreach (var z in LINEAS_PRODUCCION)
        {
            var grupo = Padre(z.id, padre);
            float paso = z.w / BANDAS_POR_LINEA;

            for (int i = 0; i < BANDAS_POR_LINEA; i++)
            {
                var b = Instanciar(P_BANDA, grupo, $"{z.id}_M{i}");
                if (b == null) continue;
                CentrarEn(b, new Vector2(z.x + (i + 0.5f) * paso, z.y + z.h / 2f));
            }
        }
    }

    static void ConstruirRacks(GameObject padre)
    {
        foreach (var z in RACKS)
        {
            var grupo = Padre(z.id, padre);

            float usadoX = RACK_HILERAS * EST_FONDO;
            float usadoZ = RACK_LARGO   * EST_LARGO;
            float x0 = z.x + (z.w - usadoX) / 2f;
            float z0 = z.y + (z.h - usadoZ) / 2f;

            for (int c = 0; c < RACK_HILERAS; c++)
            for (int f = 0; f < RACK_LARGO; f++)
            {
                var e = Instanciar(P_ESTANTE, grupo, $"{z.id}_E{c}{f}");
                if (e == null) continue;

                e.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                CentrarEn(e, new Vector2(x0 + (c + 0.5f) * EST_FONDO,
                                         z0 + (f + 0.5f) * EST_LARGO));
                LlenarEstanteria(e, c, f);
            }
        }
    }

    static void LlenarEstanteria(GameObject estante, int col, int fila)
    {
        var b = BoundsDe(estante);
        float largo  = b.size.z;
        int ranuras  = Mathf.Max(1, Mathf.FloorToInt(largo / 0.41f));
        float paso   = largo / ranuras;

        foreach (float nivel in new[] { NIVEL_BAJO, NIVEL_ALTO })
        {
            if (Random.value < 0.25f) continue;

            for (int i = 0; i < ranuras; i++)
            {
                if (Random.value < 0.35f) continue;

                Vector3 pos = new Vector3(b.center.x, b.min.y + nivel, b.min.z + paso * (i + 0.5f));

                var pal = Instanciar(P_PALLETM, estante, $"Pallet_{col}{fila}_{i}");
                if (pal != null) ApoyarEn(pal, pos);

                var caja = Instanciar(P_CAJA, estante, $"Caja_{col}{fila}_{i}");
                if (caja != null) ApoyarEn(caja, pos + new Vector3(0f, 0.04f, 0f));
            }
        }
    }

    static void ConstruirDocks(GameObject padre)
    {
        foreach (var z in TRUCK_DOCKS)
        {
            var go = Instanciar(P_DOCK, padre, z.id);
            if (go == null) continue;
            CentrarEn(go, new Vector2(z.x + z.w / 2f, z.y + z.h / 2f));
        }
    }

    static void ConstruirCargadores(GameObject padre)
    {
        foreach (var z in ESTACIONES_CARGA)
        {
            var go = Instanciar(P_CARGADOR, padre, z.id);
            if (go == null) continue;
            CentrarEn(go, new Vector2(z.x + z.w / 2f, z.y + z.h / 2f));
        }
    }

    static void ConstruirObstaculos(GameObject padre)
    {
        foreach (var z in OBSTACULOS)
        {
            var go = Instanciar(P_CAJA_OBS, padre, z.id);
            if (go == null) continue;
            float alto = Mathf.Min(z.w, z.h) < 2f ? 1.2f : 1.5f;
            AjustarABox(go, new Vector3(z.x, 0f, z.y), new Vector3(z.w, alto, z.h));
            Pintar(go, Mat("Mat_Obstaculo", C_OBST));
        }
    }

    static void ConstruirPosicionesPallet(GameObject padre)
    {
        foreach (var (id, x, y) in PALLETS)
        {
            var go = Instanciar(P_PALLETM, padre, id);
            if (go == null) continue;
            CentrarEn(go, new Vector2(x + 0.5f, y + 0.5f));
        }
    }

    static void ConstruirAgvs(GameObject padre)
    {
        foreach (var (id, x, y, bat, color) in AGVS)
        {
            var pivote = Padre(id, padre);
            Vector3 destino = new Vector3(x + 0.5f, Y_SUELO, y + 0.5f);
            pivote.transform.position = destino;

            var modelo = Instanciar(P_AGV, pivote, "Modelo");
            if (modelo == null) continue;

            var b = BoundsDe(modelo);
            if (b.size.x > 1e-4f && b.size.z > 1e-4f)
            {
                float f = Mathf.Min(AGV_LARGO / b.size.x, AGV_ANCHO / b.size.z);
                modelo.transform.localScale = modelo.transform.localScale * f;
            }

            b = BoundsDe(modelo);
            modelo.transform.position += destino - new Vector3(b.center.x, b.min.y, b.center.z);
        }
    }

    static void ConstruirSuelo(GameObject padre)
    {
        var piso = Instanciar(P_PISO, padre, "Piso");
        if (piso == null) return;
        var b = BoundsDe(piso);
        if (b.size.x < 1e-4f || b.size.z < 1e-4f) return;

        var s = piso.transform.localScale;
        piso.transform.localScale = new Vector3(s.x * (ANCHO / b.size.x), s.y, s.z * (ALTO / b.size.z));

        b = BoundsDe(piso);
        piso.transform.position += new Vector3(ANCHO / 2f, 0f, ALTO / 2f)
                                 - new Vector3(b.center.x, b.max.y, b.center.z);
    }

    static void ConstruirParedes(GameObject padre)
    {
        var muros = new (string id, Vector3 pos, float rotY, float largo)[]
        {
            ("Pared_Sur",   new Vector3(ANCHO / 2f, 0f, 0f),     0f, ANCHO),
            ("Pared_Norte", new Vector3(ANCHO / 2f, 0f, ALTO),   0f, ANCHO),
            ("Pared_Oeste", new Vector3(0f, 0f, ALTO / 2f),     90f, ALTO),
            ("Pared_Este",  new Vector3(ANCHO, 0f, ALTO / 2f),  90f, ALTO),
        };

        foreach (var m in muros)
        {
            var p = Instanciar(P_PARED, padre, m.id);
            if (p == null) continue;

            p.transform.rotation = Quaternion.Euler(0f, m.rotY, 0f);

            var b = BoundsDe(p);
            float largoActual = m.rotY == 0f ? b.size.x : b.size.z;
            if (largoActual > 1e-4f)
            {
                float f = m.largo / largoActual;
                var s = p.transform.localScale;
                p.transform.localScale = new Vector3(s.x * f, s.y, s.z);
            }

            b = BoundsDe(p);
            p.transform.position += new Vector3(m.pos.x, Y_SUELO, m.pos.z)
                                  - new Vector3(b.center.x, b.min.y, b.center.z);
        }
    }

    const string RUTA_JSON = "Assets/Config/entorno_almacen.json";

    [MenuItem("Reto AGV/Exportar coordenadas a JSON")]
    public static void ExportarCoordenadas()
    {
        var raiz = GameObject.Find("ENTORNO_ALMACEN");
        if (raiz == null)
        {
            Debug.LogError("[Reto AGV] No hay ENTORNO_ALMACEN en la escena. Construye el entorno primero.");
            return;
        }

        var grid = new int[ALTO, ANCHO];
        foreach (var grupo in new[] { LINEAS_PRODUCCION, RACKS, OBSTACULOS })
            foreach (var z in grupo)
                for (int j = (int)z.y; j < (int)(z.y + z.h); j++)
                for (int i = (int)z.x; i < (int)(z.x + z.w); i++)
                    if (j >= 0 && j < ALTO && i >= 0 && i < ANCHO) grid[j, i] = 1;

        int bloq = 0;
        foreach (var v in grid) bloq += v;

        var sb = new System.Text.StringBuilder();
        sb.Append("{\n  \"_meta\": {\n");
        sb.Append("    \"descripcion\": \"Entorno logistico AGV - Centro de distribucion virtual (M3)\",\n");
        sb.Append("    \"fuente\": \"exportado desde la escena Unity; layout derivado del tamano nativo de las piezas de Prefab-Reto\",\n");
        sb.Append($"    \"escena\": \"{RUTA_ESCENA}\",\n");
        sb.Append("    \"piezas\": \"todas de Prefab-Reto, a tamano nativo. Ninguna se deforma: se repiten o se centran.\",\n");
        sb.Append("    \"grid\": { \"ancho\": " + ANCHO + ", \"alto\": " + ALTO +
                  ", \"celdas_totales\": " + (ANCHO * ALTO) +
                  ", \"transitables\": " + (ANCHO * ALTO - bloq) +
                  ", \"bloqueadas\": " + bloq + " },\n");
        sb.Append("    \"tamanos_nativos\": { \"agv\": [1.32, 0.89], \"estanteria\": [1.25, 0.45], \"cargador\": [1.0, 1.0], \"caja\": [0.4, 0.4], \"dock\": [2.71, 1.83], \"banda\": [0.97, 1.25] },\n");
        sb.Append("    \"convencion\": {\n");
        sb.Append("      \"escala\": \"1 celda = 1 unidad Unity\",\n");
        sb.Append("      \"mapeo_ejes\": \"grid(x,y) -> Unity(x, altura, z=y). El eje Y de la figura es el eje Z de Unity.\",\n");
        sb.Append($"      \"origen\": \"celda (0,0) = Unity (0,0,0); el almacen ocupa X 0..{ANCHO}, Z 0..{ALTO}\",\n");
        sb.Append($"      \"y_suelo\": {F(Y_SUELO)},\n");
        sb.Append("      \"destino_agv\": \"para ir a la celda (x,y) usar Unity (x+0.5, y_suelo, y+0.5)\"\n");
        sb.Append("    }\n  },\n");

        EscribirZonas(sb, "lineas_produccion", LINEAS_PRODUCCION, raiz, "LineasProduccion", "linea_produccion", P_BANDA,    true);
        EscribirZonas(sb, "racks",            RACKS,             raiz, "Racks",            "rack",             P_ESTANTE,  true);
        EscribirZonas(sb, "truck_docks",      TRUCK_DOCKS,       raiz, "TruckDocks",       "truck_dock",       P_DOCK,     false);
        EscribirZonas(sb, "estaciones_carga", ESTACIONES_CARGA,  raiz, "EstacionesCarga",  "estacion_carga",   P_CARGADOR, false);
        EscribirZonas(sb, "obstaculos",       OBSTACULOS,        raiz, "Obstaculos",       "obstaculo",        P_CAJA_OBS, true);

        sb.Append("  \"posiciones_pallet\": [\n");
        for (int i = 0; i < PALLETS.Length; i++)
        {
            var (id, x, y) = PALLETS[i];
            var t = raiz.transform.Find("PosicionesPallet/" + id);
            string grupo = id.StartsWith("P-PL") ? "produccion"
                         : (id == "P-RA" || id == "P-RB" || id == "P-RC") ? "rack" : "dock";
            sb.Append("    { \"id\": \"" + id + "\", \"tipo\": \"posicion_pallet\"");
            sb.Append(", \"celda\": [" + x + ", " + y + "]");
            sb.Append(", \"centro_celda\": [" + F(x + 0.5f) + ", " + F(y + 0.5f) + "]");
            if (t != null) sb.Append(", \"unity\": { \"position\": " + V(t.position) + " }");
            sb.Append(", \"grupo\": \"" + grupo + "\"");
            sb.Append(", \"transitable\": " + (grid[y, x] == 0 ? "true" : "false"));
            sb.Append(", \"prefab\": \"" + P_PALLETM + "\" }");
            sb.Append(i < PALLETS.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n");

        sb.Append("  \"agvs\": [\n");
        for (int i = 0; i < AGVS.Length; i++)
        {
            var (id, x, y, bat, color) = AGVS[i];
            var t = raiz.transform.Find("AGVs/" + id);
            sb.Append("    { \"id\": \"" + id + "\"");
            sb.Append(", \"celda_inicio\": [" + x + ", " + y + "]");
            sb.Append(", \"centro_celda\": [" + F(x + 0.5f) + ", " + F(y + 0.5f) + "]");
            if (t != null) sb.Append(", \"unity\": { \"position\": " + V(t.position) + " }");
            sb.Append(", \"bateria_inicial\": " + F(bat));
            sb.Append(", \"color\": \"" + color + "\"");
            sb.Append(", \"transitable\": " + (grid[y, x] == 0 ? "true" : "false"));
            sb.Append(", \"prefab\": \"" + P_AGV + "\" }");
            sb.Append(i < AGVS.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n");

        sb.Append("  \"docks_in\": [\"DOCK-3\"],\n");
        sb.Append("  \"docks_out\": [\"DOCK-1\", \"DOCK-2\"],\n");
        sb.Append("  \"flujos\": [\n");
        sb.Append("    { \"nombre\": \"Produccion -> Rack\",     \"origenes\": [\"P-PL1\",\"P-PL2\",\"P-PL3\",\"P-PL4\"], \"destinos\": [\"P-RA\",\"P-RB\",\"P-RC\"] },\n");
        sb.Append("    { \"nombre\": \"Produccion -> Dock OUT\", \"origenes\": [\"P-PL1\",\"P-PL2\",\"P-PL3\",\"P-PL4\"], \"destinos\": [\"P-D1\",\"P-D2\"] },\n");
        sb.Append("    { \"nombre\": \"Rack -> Dock OUT\",       \"origenes\": [\"P-RA\",\"P-RB\",\"P-RC\"],            \"destinos\": [\"P-D1\",\"P-D2\"] },\n");
        sb.Append("    { \"nombre\": \"Dock IN -> Rack\",        \"origenes\": [\"P-D3\"],                            \"destinos\": [\"P-RA\",\"P-RB\",\"P-RC\"] }\n");
        sb.Append("  ],\n");

        sb.Append("  \"grid_bloqueado\": [\n");
        for (int j = 0; j < ALTO; j++)
        {
            sb.Append("    [");
            for (int i = 0; i < ANCHO; i++) sb.Append(grid[j, i] + (i < ANCHO - 1 ? "," : ""));
            sb.Append("]" + (j < ALTO - 1 ? ",\n" : "\n"));
        }
        sb.Append("  ]\n}\n");

        System.IO.Directory.CreateDirectory("Assets/Config");
        System.IO.File.WriteAllText(RUTA_JSON, sb.ToString());
        AssetDatabase.Refresh();
        Debug.Log($"[Reto AGV] Coordenadas exportadas a {RUTA_JSON}");
    }

    static void EscribirZonas(System.Text.StringBuilder sb, string clave, Zona[] zonas,
                              GameObject raiz, string grupo, string tipo, string prefab, bool bloquea)
    {
        sb.Append("  \"" + clave + "\": [\n");
        for (int i = 0; i < zonas.Length; i++)
        {
            var z = zonas[i];
            var t = raiz.transform.Find(grupo + "/" + z.id);
            sb.Append("    { \"id\": \"" + z.id + "\", \"tipo\": \"" + tipo + "\"");
            sb.Append(", \"celda_origen\": [" + (int)z.x + ", " + (int)z.y + "]");
            sb.Append(", \"ancho_celdas\": " + (int)z.w + ", \"alto_celdas\": " + (int)z.h);
            sb.Append(", \"centro_celda\": [" + F(z.x + z.w / 2f) + ", " + F(z.y + z.h / 2f) + "]");
            if (t != null)
            {
                sb.Append(", \"unity\": { \"position\": " + V(t.position));
                var b = BoundsDe(t.gameObject);
                sb.Append(", \"bounds_min\": " + V(b.min) + ", \"bounds_max\": " + V(b.max));
                sb.Append(", \"bounds_size\": " + V(b.size) + " }");
                if (clave == "racks") sb.Append(", \"estanterias\": " + (RACK_HILERAS * RACK_LARGO));
            }
            sb.Append(", \"prefab\": \"" + prefab + "\"");
            sb.Append(", \"bloquea_navegacion\": " + (bloquea ? "true" : "false") + " }");
            sb.Append(i < zonas.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n");
    }

    static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + "]";

    static void CentrarEn(GameObject go, Vector2 centroXZ)
    {
        var b = BoundsDe(go);
        go.transform.position += new Vector3(centroXZ.x, Y_SUELO, centroXZ.y)
                              - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    static void ApoyarEn(GameObject go, Vector3 baseXYZ)
    {
        var b = BoundsDe(go);
        go.transform.position += baseXYZ - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    static void AjustarABox(GameObject go, Vector3 origenCelda, Vector3 tam)
    {
        var b = BoundsDe(go);
        if (b.size.x < 1e-5f || b.size.z < 1e-5f)
        {
            go.transform.position = origenCelda + new Vector3(tam.x / 2f, Y_SUELO, tam.z / 2f);
            return;
        }

        Vector3 s = go.transform.localScale;
        go.transform.localScale = new Vector3(
            s.x * (tam.x / b.size.x),
            s.y * (b.size.y > 1e-5f ? tam.y / b.size.y : 1f),
            s.z * (tam.z / b.size.z));

        b = BoundsDe(go);
        go.transform.position += origenCelda + new Vector3(tam.x / 2f, Y_SUELO, tam.z / 2f)
                               - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    static Bounds BoundsDe(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    static Material Mat(string nombre, string hex)
    {
        string ruta = $"{DIR_MATS}/{nombre}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        bool nuevo = m == null;

        if (nuevo)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh);
        }

        if (ColorUtility.TryParseHtmlString(hex, out var c))
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);

        if (nuevo)
        {
            System.IO.Directory.CreateDirectory(DIR_MATS);
            AssetDatabase.CreateAsset(m, ruta);
        }
        else EditorUtility.SetDirty(m);
        return m;
    }

    static void Pintar(GameObject go, Material m)
    {
        if (go == null || m == null) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = m;
            r.sharedMaterials = mats;
        }
    }

    static GameObject Padre(string nombre, GameObject padre)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre != null ? padre.transform : null, false);
        return go;
    }

    static GameObject Instanciar(string ruta, GameObject padre, string nombre)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
        if (prefab == null) { Debug.LogError($"[Reto AGV] No se encontro el prefab: {ruta}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = nombre;
        go.transform.SetParent(padre.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        return go;
    }

    static void AjustarLuz()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.intensity = 1.1f;
            l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            l.shadows = LightShadows.Soft;
        }
        RenderSettings.ambientMode      = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight     = new Color(0.55f, 0.57f, 0.60f);
        RenderSettings.ambientIntensity = 1f;
    }

    static void CrearCamara()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            cam = go.GetComponent<Camera>();
        }
        cam.orthographic = false;
        cam.fieldOfView  = 48f;
        cam.transform.position = new Vector3(ANCHO / 2f, 18f, -7f);
        cam.transform.rotation = Quaternion.Euler(46f, 0f, 0f);
        cam.farClipPlane = 300f;
    }
}
