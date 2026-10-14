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

    static void Ground(Terrain terrain, Transform t, float sink)
    {
        var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return;
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        float ty = terrain.transform.position.y, low = float.MaxValue;
        for (int i = 0; i <= 2; i++) for (int j = 0; j <= 2; j++)
            low = Mathf.Min(low, terrain.SampleHeight(new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 2f), 0f, Mathf.Lerp(b.min.z, b.max.z, j / 2f))) + ty);
        t.position += Vector3.up * ((low - sink * b.size.y) - b.min.y);
    }

    // Opens Level 2 (saving the current scene first), then: dark rocks, white tents, palms + brown-bark trees, big rocks on the far hills.
    [MenuItem("Tools/Levels/Level 2 - palms, trees and far big rocks (open Level 2 scene)")]
    public static void Level2Dress()
    {
        var cur = EditorSceneManager.GetActiveScene();
        if (!cur.path.EndsWith("Level 2.unity") && !cur.path.EndsWith("Level 2 Gameplay.unity"))
        {
            if (cur.isDirty && !string.IsNullOrEmpty(cur.path)) EditorSceneManager.SaveScene(cur);
            string dir = string.IsNullOrEmpty(cur.path) ? "Assets/Scenes" : System.IO.Path.GetDirectoryName(cur.path).Replace('\\', '/');
            string l2 = dir + "/Level 2.unity";
            if (!System.IO.File.Exists(l2)) { var g = AssetDatabase.FindAssets("\"Level 2\" t:Scene"); foreach (var id in g) { var pth = AssetDatabase.GUIDToAssetPath(id); if (pth.EndsWith("/Level 2.unity")) l2 = pth; } }
            EditorSceneManager.OpenScene(l2, OpenSceneMode.Single);
        }
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Levels: Level 2 terrain not found."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        var rng = new System.Random(System.Environment.TickCount);
        System.Func<float, float, float> R = (a, b) => a + (b - a) * (float)rng.NextDouble();
        float CX = PathX(14f) - 13f, CZ = 14f, WX = PathX(30f) - 7f, WZ = 30f;

        // 1. same dark rocks + white tents as the other levels
        EditorUtility.DisplayProgressBar("Level 2", "Dark rocks + white tents...", 0.05f);
        SugarcaneLevel3.DarkRocks();
        WhiteTents();

        // 2. palms (Level 1) + brown-bark trees (Level 2), spread over the bare ground, more palms out on the far hills
        EditorUtility.DisplayProgressBar("Level 2", "Planting palms and trees...", 0.3f);
        var palms = MountainEnvironmentBuilder.LoadPrefabs("Assets/Coconut Palm Tree Pack/Prefabs/", null);
        var brown = new List<Transform>();
        var treesRoot = root.Find("Trees");
        if (treesRoot != null) foreach (var lg in treesRoot.GetComponentsInChildren<LODGroup>(true)) brown.Add(lg.transform);
        var have = new List<Vector2>();
        foreach (var r in root.GetComponentsInChildren<LODGroup>(true))
        { var n0 = r.transform.name.ToLowerInvariant(); if (n0.Contains("tree") || n0.Contains("palm") || n0.Contains("birch")) have.Add(new Vector2(r.transform.position.x - cx0, r.transform.position.z - cz0)); }
        var old = root.Find("Palms and Trees"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var grp = new GameObject("Palms and Trees").transform; grp.SetParent(root, false);
        float half = S / 2f - 10f; int np = 0, nt = 0;
        for (float gz = -half; gz < half; gz += 20f)
            for (float gx = -half; gx < half; gx += 20f)
            {
                float x = gx + R(0f, 20f), z = gz + R(0f, 20f);
                float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                bool far = edge > 175f;
                if (Mathf.Abs(x - PathX(z)) < 8f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(CX, CZ)) < 22f || Vector2.Distance(new Vector2(x, z), new Vector2(WX, WZ)) < 18f) continue;
                if (td.GetSteepness((x + S / 2f) / S, (z + S / 2f) / S) > (far ? 38f : 32f)) continue;
                float near = 1e9f; foreach (var h in have) near = Mathf.Min(near, (h - new Vector2(x, z)).magnitude);
                if (near < (far ? 14f : 20f)) continue;
                float clump = Fbm(x * 0.02f + 3f, z * 0.02f + 9f);
                if (rng.NextDouble() > (far ? 0.75f : 0.45f) * (0.5f + clump)) continue;
                bool palm = palms.Count > 0 && (brown.Count == 0 || rng.NextDouble() < (far ? 0.7 : 0.5));
                var pos = new Vector3(cx0 + x, terrain.SampleHeight(new Vector3(cx0 + x, 0f, cz0 + z)) + ty, cz0 + z);
                GameObject g;
                if (palm)
                {
                    g = MountainEnvironmentBuilder.SpawnSized(palms[rng.Next(palms.Count)], pos, Quaternion.Euler(R(-5f, 5f), R(0f, 360f), R(-5f, 5f)), R(8f, 14f), grp);
                    if (g == null) continue; g.name = "Palm " + (++np).ToString("000");
                }
                else
                {
                    var tpl = brown[rng.Next(brown.Count)];
                    g = Object.Instantiate(tpl.gameObject, grp);
                    g.transform.position = pos; g.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                    g.transform.localScale = tpl.lossyScale * R(0.85f, 1.2f);
                    g.name = "Tree " + (++nt).ToString("000");
                }
                have.Add(new Vector2(x, z));
            }
        MountainEnvironmentBuilder.ConvertToURP(grp);   // palms need it
        FixWhiteTrees();                                  // but the brown trees keep their wind shader

        // 3. big dark boulders out on the far hills so the distance isn't flat
        EditorUtility.DisplayProgressBar("Level 2", "Placing far boulders...", 0.7f);
        var oldR = root.Find("Far Big Rocks"); if (oldR != null) Object.DestroyImmediate(oldR.gameObject);
        var rg = new GameObject("Far Big Rocks").transform; rg.SetParent(root, false);
        var rocks = MountainEnvironmentBuilder.LoadPrefabs("Assets/Rocks and Boulders 2/Rocks/Prefabs/", nm => !nm.Contains("LOD"));
        int nr = 0;
        if (rocks.Count > 0)
            for (float gz = -half; gz < half; gz += 40f)
                for (float gx = -half; gx < half; gx += 40f)
                {
                    float x = gx + R(0f, 40f), z = gz + R(0f, 40f);
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 160f) continue;     // only far from the centre
                    if (rng.NextDouble() < 0.4) continue;
                    int cl = rng.Next(1, 4);
                    for (int c = 0; c < cl; c++)
                    {
                        float lx = x + R(-7f, 7f), lz = z + R(-7f, 7f);
                        var g = MountainEnvironmentBuilder.SpawnSized(rocks[rng.Next(rocks.Count)], new Vector3(cx0 + lx, 0f, cz0 + lz), Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f)), c == 0 ? R(10f, 18f) : R(5f, 9f), rg);
                        if (g == null) continue;
                        Ground(terrain, g.transform, 0.15f);
                        g.name = "Far Boulder " + (++nr).ToString("000");
                    }
                }

        // 4. stay visible from the field
        foreach (var t in new[] { grp, rg })
            foreach (var lg in t.GetComponentsInChildren<LODGroup>(true))
            {
                var l = lg.GetLODs(); if (l.Length < 2) continue;
                for (int i = 0; i < l.Length; i++) l[i].screenRelativeTransitionHeight = (i == l.Length - 1) ? Mathf.Min(l[i].screenRelativeTransitionHeight, 0.002f) : l[i].screenRelativeTransitionHeight * 0.4f;
                lg.SetLODs(l);
            }
        foreach (var cam in Object.FindObjectsByType<Camera>()) if (cam.farClipPlane < 1500f) cam.farClipPlane = 1500f;

        EditorUtility.ClearProgressBar();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level 2: " + np + " palms, " + nt + " brown-bark trees, " + nr + " far boulders added; rocks dark, tents white. Scene saved.");
    }

    static float Fbm(float x, float z)
    {
        float s = 0f, a = 0.5f, f = 1f, n = 0f;
        for (int i = 0; i < 3; i++) { s += a * Mathf.PerlinNoise(x * f + 100f, z * f + 100f); n += a; a *= 0.5f; f *= 2f; }
        return s / n;
    }

    [MenuItem("Tools/Levels/Diag - tree materials")]
    public static void DiagTreeMats()
    {
        var rootGo = GameObject.Find("Mountain Environment");
        var counts = new Dictionary<string, int>();
        foreach (var lg in rootGo.GetComponentsInChildren<LODGroup>(true))
        {
            string n = lg.name.ToLowerInvariant();
            if (!(n.Contains("tree") || n.Contains("birch") || n.Contains("palm"))) continue;
            var lods = lg.GetLODs();
            for (int li = 0; li < lods.Length; li++)
                foreach (var r in lods[li].renderers)
                {
                    if (r == null) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        string key = "LOD" + li + " | " + (m == null ? "null" : m.name + " | " + m.shader.name + " | " + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "")) + " | " + r.GetType().Name;
                        int c; counts.TryGetValue(key, out c); counts[key] = c + 1;
                    }
                }
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in counts) sb.AppendLine(kv.Value + "  " + kv.Key);
        System.IO.File.WriteAllText("_treemats.txt", sb.ToString());
        Debug.Log("tree mats written");
    }

    // The URP conversion turned some brown-bark trees' wind shader into plain white Lit. Put the brown wind material back.
    [MenuItem("Tools/Levels/Fix white trees (restore brown bark)")]
    public static void FixWhiteTrees()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var good = new Dictionary<string, Material>();
        foreach (var guid in AssetDatabase.FindAssets("Tropical_ t:Material"))
        { var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)); if (m != null && m.name.StartsWith("Tropical_")) good[m.name] = m; }
        int fixedR = 0;
        foreach (var go in scene.GetRootGameObjects())
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith("URP_Tropical_")) continue;
                    Material g; if (good.TryGetValue(mats[i].name.Substring(4), out g)) { mats[i] = g; ch = true; }
                }
                if (ch) { r.sharedMaterials = mats; fixedR++; }
            }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Levels: restored brown bark on " + fixedR + " tree meshes. Scene saved.");
    }

    // A few palms lining the road and around the main camp / wall area.
    [MenuItem("Tools/Levels/Palms near the road and main area")]
    public static void RoadPalms()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Levels: no terrain."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        var rng = new System.Random(System.Environment.TickCount);
        System.Func<float, float, float> R = (a, b) => a + (b - a) * (float)rng.NextDouble();
        float CX = PathX(14f) - 13f, CZ = 14f, WX = PathX(30f) - 7f, WZ = 30f;
        var palms = MountainEnvironmentBuilder.LoadPrefabs("Assets/Coconut Palm Tree Pack/Prefabs/", null);
        if (palms.Count == 0) { Debug.LogWarning("Levels: palm prefabs not found."); return; }

        // keep clear of everything big that's already there (trees, rocks, buildings)
        var taken = new List<Vector2>();
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds; if (b.size.y < 2f) continue;
            bool bush = false;                                              // bushes don't block a palm
            for (var p = r.transform; p != null; p = p.parent) { string pn = p.name; if (pn == "Foliage" || pn == "Big Bushes" || pn == "Road Palms" || pn.StartsWith("Bush")) { bush = true; break; } }
            if (bush) continue;
            taken.Add(new Vector2(b.center.x - cx0, b.center.z - cz0));
        }
        System.Func<float, float, float, bool> free = (x, z, d) => { foreach (var t in taken) if ((t - new Vector2(x, z)).sqrMagnitude < d * d) return false; return true; };

        var old = root.Find("Road Palms"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var grp = new GameObject("Road Palms").transform; grp.SetParent(root, false);
        int n = 0;
        // along both sides of the road through the playable part
        for (float z = -150f; z < 150f; z += R(14f, 24f))
            foreach (float side in new[] { -1f, 1f })
            {
                if (rng.NextDouble() < 0.25) continue;
                float lz = z + R(-4f, 4f), lx = PathX(lz) + side * R(7f, 13f);
                if (Vector2.Distance(new Vector2(lx, lz), new Vector2(CX, CZ)) < 15f || Vector2.Distance(new Vector2(lx, lz), new Vector2(WX, WZ)) < 13f) continue;
                if (td.GetSteepness((lx + S / 2f) / S, (lz + S / 2f) / S) > 30f || !free(lx, lz, 4f)) continue;
                Spawn(palms, grp, terrain, cx0 + lx, cz0 + lz, R(7f, 12f), rng); taken.Add(new Vector2(lx, lz)); n++;
            }
        // a loose ring around the camp and the wall (the main platform)
        foreach (var c in new[] { new Vector2(CX, CZ), new Vector2(WX, WZ) })
            for (int i = 0; i < 7; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(18f, 30f), lx = c.x + Mathf.Cos(a) * d, lz = c.y + Mathf.Sin(a) * d;
                if (Mathf.Abs(lx - PathX(lz)) < 6f || td.GetSteepness((lx + S / 2f) / S, (lz + S / 2f) / S) > 30f || !free(lx, lz, 5f)) continue;
                Spawn(palms, grp, terrain, cx0 + lx, cz0 + lz, R(8f, 13f), rng); taken.Add(new Vector2(lx, lz)); n++;
            }
        MountainEnvironmentBuilder.ConvertToURP(grp);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Levels: " + n + " palms added near the road and main area. Scene saved.");
    }

    static void Spawn(List<GameObject> palms, Transform grp, Terrain terrain, float wx, float wz, float size, System.Random rng)
    {
        var pos = new Vector3(wx, terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y, wz);
        var g = MountainEnvironmentBuilder.SpawnSized(palms[rng.Next(palms.Count)], pos, Quaternion.Euler((float)rng.NextDouble() * 8f - 4f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 8f - 4f), size, grp);
        if (g != null) g.name = "Road Palm " + grp.childCount.ToString("00");
    }
}
