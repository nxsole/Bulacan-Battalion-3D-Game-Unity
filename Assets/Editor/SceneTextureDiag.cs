using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Text;

public static class SceneTextureDiag
{
    [MenuItem("Tools/Levels/Diag - missing textures in this scene")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        sb.AppendLine("scene " + scene.path);
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
        {
            var td = t.terrainData;
            sb.AppendLine("TERRAIN " + t.name + " active=" + t.gameObject.activeInHierarchy + " pos=" + t.transform.position + " size=" + (td ? td.size.ToString() : "-") + " data=" + (td ? AssetDatabase.GetAssetPath(td) : "NULL") + " mat=" + (t.materialTemplate ? t.materialTemplate.name + "/" + t.materialTemplate.shader.name : "null"));
            if (td != null)
            {
                var ls = td.terrainLayers;
                sb.AppendLine("  layers=" + (ls == null ? -1 : ls.Length) + " alphaLayers=" + td.alphamapLayers);
                if (ls != null) foreach (var l in ls) sb.AppendLine("   layer " + (l == null ? "NULL" : l.name + " path=" + AssetDatabase.GetAssetPath(l) + " diffuse=" + (l.diffuseTexture ? l.diffuseTexture.name : "NULL")));
            }
        }
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            var b = r.bounds; if (b.size.x < 8f && b.size.z < 8f) continue;   // only big surfaces
            foreach (var m in r.sharedMaterials)
            {
                string tex = "";
                if (m != null && m.HasProperty("_BaseMap")) tex = m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "NONE";
                else if (m != null && m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex") ? m.GetTexture("_MainTex").name : "NONE";
                sb.AppendLine("BIG " + r.name + " size=" + b.size + " mat=" + (m == null ? "NULL" : m.name + " | " + m.shader.name + " | path=" + AssetDatabase.GetAssetPath(m) + " | tex=" + tex));
            }
        }
        System.IO.File.WriteAllText("_scenediag.txt", sb.ToString());
        Debug.Log("diag written");
    }
}

public static class SceneTextureDiag2
{
    [MenuItem("Tools/Levels/Diag - list terrain layers + decals")]
    public static void Run()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in AssetDatabase.FindAssets("t:TerrainLayer"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g); var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
            sb.AppendLine(p + " | diffuse=" + (l && l.diffuseTexture ? AssetDatabase.GetAssetPath(l.diffuseTexture) : "NULL") + " | tile=" + (l ? l.tileSize.ToString() : ""));
        }
        foreach (var c in Object.FindObjectsByType<Component>(FindObjectsInactive.Include))
        {
            if (c == null || c.GetType().Name != "DecalProjector") continue;
            var mp = c.GetType().GetProperty("material"); var m = mp != null ? mp.GetValue(c) as Material : null;
            sb.AppendLine("DECAL " + c.name + " active=" + c.gameObject.activeInHierarchy + " pos=" + c.transform.position + " mat=" + (m ? m.name + " | " + m.shader.name + " | " + AssetDatabase.GetAssetPath(m) : "NULL"));
        }
        var t = Object.FindFirstObjectByType<Terrain>();
        if (t != null)
        {
            var td = t.terrainData; var a = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
            for (int l = 0; l < a.GetLength(2); l++)
            {
                double sum = 0; float minx = 1e9f, maxx = -1e9f, minz = 1e9f, maxz = -1e9f;
                for (int z = 0; z < a.GetLength(0); z++) for (int x = 0; x < a.GetLength(1); x++)
                {
                    float w = a[z, x, l]; sum += w;
                    if (w > 0.5f) { minx = Mathf.Min(minx, x); maxx = Mathf.Max(maxx, x); minz = Mathf.Min(minz, z); maxz = Mathf.Max(maxz, z); }
                }
                sb.AppendLine("alpha layer " + l + " total=" + sum.ToString("F0") + " bbox x " + minx + "-" + maxx + " z " + minz + "-" + maxz + " (of " + td.alphamapWidth + ")");
            }
        }
        System.IO.File.WriteAllText("_layers.txt", sb.ToString());
        Debug.Log("layers written");
    }
}

public static class MissingTextureFix
{
    static int FixScene(UnityEngine.SceneManagement.Scene scene, TerrainLayer filler, System.Text.StringBuilder log)
    {
        int n = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Terrain>(true))
            {
                var td = t.terrainData; if (td == null) continue;
                var ls = td.terrainLayers; bool ch = false;
                for (int i = 0; i < ls.Length; i++) if (ls[i] == null) { ls[i] = filler; ch = true; n++; }
                if (ch) { td.terrainLayers = ls; EditorUtility.SetDirty(td); log.AppendLine(scene.name + ": filled empty ground slot on " + t.name); }
            }
            // a Decal Projector still using URP's placeholder material just draws a dark square
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "DecalProjector") continue;
                var mp = c.GetType().GetProperty("material"); var m = mp != null ? mp.GetValue(c) as Material : null;
                if (m == null || AssetDatabase.GetAssetPath(m).StartsWith("Packages/"))
                { c.gameObject.SetActive(false); n++; log.AppendLine(scene.name + ": turned off placeholder decal " + c.name); }
            }
        }
        if (n > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        return n;
    }

    [MenuItem("Tools/Levels/Fix missing ground texture (Level 1 + main menu)")]
    public static void Run()
    {
        string src = "Assets/_TerrainAutoUpgrade/layer_dirt01b3407d0e55802f81.terrainlayer";
        string dst = "Assets/_TerrainAutoUpgrade/layer_dirt01_camp.terrainlayer";
        var filler = AssetDatabase.LoadAssetAtPath<TerrainLayer>(dst);
        if (filler == null) { AssetDatabase.CopyAsset(src, dst); filler = AssetDatabase.LoadAssetAtPath<TerrainLayer>(dst); }
        if (filler == null) { Debug.LogWarning("Fix: dirt layer not found."); return; }
        var log = new System.Text.StringBuilder();
        var active = EditorSceneManager.GetActiveScene();
        FixScene(active, filler, log);
        foreach (var g in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (p == active.path) continue;
            string nm = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            if (!(nm == "main menu" || nm == "level 1")) continue;
            var s = EditorSceneManager.OpenScene(p, OpenSceneMode.Additive);
            FixScene(s, filler, log);
            EditorSceneManager.CloseScene(s, true);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Missing texture fix:\n" + (log.Length > 0 ? log.ToString() : "nothing to fix"));
    }
}
