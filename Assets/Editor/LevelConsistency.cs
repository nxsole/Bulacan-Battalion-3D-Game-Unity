// Small tools that keep Levels 1-3 looking the same: white tents, extra (sparse) trees on Level 2.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LevelConsistency
{
    static float PathX(float z) { return 12f * Mathf.Sin(z * 0.045f) + 5f * Mathf.Sin(z * 0.11f + 1f) - z * 0.1f; }

    static bool UnderTent(Transform t)
    {
        for (var p = t; p != null; p = p.parent) if (p.name.ToLowerInvariant().Contains("tent")) return true;
        return false;
    }

    [MenuItem("Tools/Levels/White tents (same as Level 1)")]
    public static void WhiteTents()
    {
        var scene = EditorSceneManager.GetActiveScene();
        int n = 0; var mats = new HashSet<Material>();
        var canvas = AssetDatabase.LoadAssetAtPath<Material>("Assets/MountainEnvironment/Materials/MtnCanvas.mat");
        if (canvas != null) mats.Add(canvas);
        foreach (var go in scene.GetRootGameObjects())
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!UnderTent(r.transform)) continue;
                r.SetPropertyBlock(null);                                   // drop any tint painted on top
                foreach (var m in r.sharedMaterials) if (m != null && AssetDatabase.Contains(m)) mats.Add(m);
                n++;
            }
        foreach (var m in mats)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Levels: " + n + " tent parts set to white (" + mats.Count + " materials). Scene saved.");
    }

    [MenuItem("Tools/Levels/Level 2 - add some trees (sparse)")]
    public static void Level2SparseTrees()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Levels: no Mountain Environment terrain in this scene."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        var rng = new System.Random(System.Environment.TickCount);
        System.Func<float, float, float> R = (a, b) => a + (b - a) * (float)rng.NextDouble();

        // templates = the brown-bark trees already in Level 2 (same look, same materials)
        var templates = new List<Transform>();
        var treesRoot = root.Find("Trees");
        if (treesRoot == null) { Debug.LogWarning("Levels: Level 2 has no Trees group to copy from."); return; }
        foreach (var r in treesRoot.GetComponentsInChildren<LODGroup>(true)) templates.Add(r.transform);
        if (templates.Count == 0) foreach (Transform t in treesRoot) if (t.GetComponentInChildren<Renderer>() != null && t.name != "Added" && t.name != "Extra") templates.Add(t);
        if (templates.Count == 0) { Debug.LogWarning("Levels: no tree to copy."); return; }

        // where trees already stand
        var have = new List<Vector2>();
        foreach (var t in templates) have.Add(new Vector2(t.position.x - cx0, t.position.z - cz0));
        var fill = root.Find("Fill-in"); if (fill != null) { var ft = fill.Find("Trees"); if (ft != null) foreach (Transform t in ft) have.Add(new Vector2(t.position.x - cx0, t.position.z - cz0)); }

        var old = treesRoot.Find("Extra"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var grp = new GameObject("Extra").transform; grp.SetParent(treesRoot, false);
        float CX = PathX(14f) - 13f, CZ = 14f, WX = PathX(30f) - 7f, WZ = 30f;
        float half = S / 2f - 16f, cell = 22f; int n = 0;
        for (float gz = -half; gz < half; gz += cell)
            for (float gx = -half; gx < half; gx += cell)
            {
                if (n >= 90) break;
                float x = gx + R(0f, cell), z = gz + R(0f, cell);
                if (Mathf.Abs(x - PathX(z)) < 8f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(CX, CZ)) < 20f || Vector2.Distance(new Vector2(x, z), new Vector2(WX, WZ)) < 18f) continue;
                if (td.GetSteepness((x + S / 2f) / S, (z + S / 2f) / S) > 32f) continue;
                float near = 1e9f; foreach (var h in have) near = Mathf.Min(near, (h - new Vector2(x, z)).magnitude);
                if (near < 30f) continue;                                   // only where it's bare, and never thick
                if (rng.NextDouble() < 0.35) continue;
                var tpl = templates[rng.Next(templates.Count)];
                var g = Object.Instantiate(tpl.gameObject, grp);
                Undo.RegisterCreatedObjectUndo(g, "Sparse trees");
                g.transform.position = new Vector3(cx0 + x, terrain.SampleHeight(new Vector3(cx0 + x, 0f, cz0 + z)) + ty, cz0 + z);
                g.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                g.transform.localScale = tpl.lossyScale * R(0.85f, 1.15f);
                g.name = "Tree Extra " + (++n).ToString("00");
                have.Add(new Vector2(x, z));
            }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Levels: added " + n + " brown-bark trees in the bare areas of Level 2 (sparse). Scene saved.");
    }
}
