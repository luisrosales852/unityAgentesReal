
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MontaLaFlota
{
    const string PrefabOriginal = "Assets/Texturas/AGV_Aaron.prefab";
    const string PrefabEstatico = "Assets/Prefab/AGV_Estatico.prefab";
    const string MallasDelAgv = "Assets/Prefab/AGV_Estatico_mallas.asset";
    const string PrefabDeLaCaja = "Assets/Prefab/Caja.prefab";
    const string NombreDelGrupo = "Flota_AGV";

    static readonly Vector3[] Salidas =
    {
        new Vector3( 3.036f, 0f, 5.116f),
        new Vector3(-1.364f, 0f, 5.116f),
        new Vector3( 5.236f, 0f, 2.916f),
        new Vector3( 3.036f, 0f, 0.716f),
        new Vector3(-1.364f, 0f, 0.716f),
    };

    [MenuItem("AGV/Montar los cinco AGV en la escena")]
    public static void Montar()
    {
        WebClient cliente = BuscaElCliente();
        if (cliente == null) { Debug.LogError("[Flota] No hay WebClient en la escena."); return; }
        if (cliente.raiz == null) { Debug.LogError("[Flota] Al WebClient le falta 'raiz'."); return; }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabEstatico) ?? Hornea();
        if (prefab == null) return;

        Transform raiz = cliente.raiz;

        Transform modelo = ElAgvQueYaEstaba(raiz);
        Vector3 escala = modelo != null ? modelo.localScale : Vector3.one;
        Quaternion rumbo = modelo != null ? modelo.localRotation : Quaternion.identity;
        float altura = modelo != null ? modelo.localPosition.y : 0f;

        Transform viejo = raiz.Find(NombreDelGrupo);
        if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);

        var grupo = new GameObject(NombreDelGrupo);
        Undo.RegisterCreatedObjectUndo(grupo, "Montar la flota");
        grupo.transform.SetParent(raiz, false);

        var flota = new List<Transform>();
        var nombres = new List<string>();
        for (int i = 0; i < Salidas.Length; i++)
        {
            var copia = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grupo.transform);
            Undo.RegisterCreatedObjectUndo(copia, "Montar la flota");
            copia.name = "AGV_" + (i + 1);
            copia.transform.localPosition = Salidas[i] + Vector3.up * altura;
            copia.transform.localRotation = rumbo;
            copia.transform.localScale = escala;
            flota.Add(copia.transform);
            nombres.Add(copia.name);
        }

        if (modelo != null && modelo.gameObject.activeSelf)
        {
            Undo.RecordObject(modelo.gameObject, "Montar la flota");
            modelo.gameObject.SetActive(false);
            EditorUtility.SetDirty(modelo.gameObject);
        }

        Undo.RecordObject(cliente, "Montar la flota");
        cliente.agvsEnEscena = flota.ToArray();
        cliente.nombresDeAgvs = nombres.ToArray();
        cliente.completarConClones = false;
        if (cliente.cajaPrefab == null)
            cliente.cajaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDeLaCaja);
        EditorUtility.SetDirty(cliente);

        EditorSceneManager.MarkSceneDirty(cliente.gameObject.scene);
        EditorSceneManager.SaveScene(cliente.gameObject.scene);
        Debug.Log($"[Flota] {flota.Count} AGV puestos bajo '{NombreDelGrupo}'.");
    }

    [MenuItem("AGV/Hornear el AGV a mallas compartidas")]
    public static GameObject Hornea()
    {
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabOriginal);
        if (original == null) { Debug.LogError($"[Flota] Falta {PrefabOriginal}."); return null; }

        var copia = (GameObject)PrefabUtility.InstantiatePrefab(original);
        try
        {
            var filtros = copia.GetComponentsInChildren<MeshFilter>(true);
            var mallas = new List<Mesh>();
            foreach (var f in filtros)
            {
                if (f.sharedMesh == null)
                {
                    Debug.LogError("[Flota] ProBuilder no genero las mallas; abre la escena y reintenta.");
                    return null;
                }
                var m = Object.Instantiate(f.sharedMesh);
                m.name = f.gameObject.name;
                mallas.Add(m);
            }

            AssetDatabase.DeleteAsset(MallasDelAgv);
            AssetDatabase.CreateAsset(mallas[0], MallasDelAgv);
            for (int i = 1; i < mallas.Count; i++) AssetDatabase.AddObjectToAsset(mallas[i], MallasDelAgv);
            AssetDatabase.SaveAssets();

            foreach (var c in copia.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Namespace != null &&
                    c.GetType().Namespace.StartsWith("UnityEngine.ProBuilder"))
                    Object.DestroyImmediate(c);

            for (int i = 0; i < filtros.Length; i++)
            {
                filtros[i].sharedMesh = mallas[i];
                var col = filtros[i].GetComponent<MeshCollider>();
                if (col != null) col.sharedMesh = mallas[i];
            }

            foreach (var luz in copia.GetComponentsInChildren<Light>(true)) luz.enabled = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(copia, PrefabEstatico);
            Debug.Log($"[Flota] {mallas.Count} mallas horneadas en {MallasDelAgv}.");
            return prefab;
        }
        finally { Object.DestroyImmediate(copia); }
    }

    static WebClient BuscaElCliente()
    {
        WebClient elegido = null;
        foreach (var c in Object.FindObjectsByType<WebClient>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.raiz != null) return c;
            elegido = elegido ?? c;
        }
        return elegido;
    }

    static Transform ElAgvQueYaEstaba(Transform raiz)
    {
        foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("AGV") && !t.name.StartsWith("AGB")) continue;
            if (t.name == NombreDelGrupo) continue;
            if (t.parent != null && t.parent.name == NombreDelGrupo) continue;
            return t;
        }
        return null;
    }
}
