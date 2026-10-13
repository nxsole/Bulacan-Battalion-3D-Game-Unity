// Level 3 - sugarcane field. Creates hand-built sugarcane assets (mesh + textures + material + prefabs)
// and turns a COPY of the Level 2 scene into a flat sugarcane plantation. Level 2 itself is not touched.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class SugarcaneLevel3
{
    const string Dir = "Assets/Sugarcane";
    const float OriginX = 0f;

    // ---------------------------------------------------------------- noise
    static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }
    static float VNoise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
    }
    static float Fbm(float x, float y, int oct)
    {
        float s = 0, a = 0.5f, f = 1, n = 0;
        for (int i = 0; i < oct; i++) { s += a * VNoise(x * f, y * f); n += a; a *= 0.5f; f *= 2.03f; }
        return s / n;
    }
    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float PathX(float z) { return 12f * Mathf.Sin(z * 0.045f) + 5f * Mathf.Sin(z * 0.11f + 1f) - z * 0.1f; }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    // ---------------------------------------------------------------- textures
    static Texture2D MakeTex(string name, int w, int h, System.Func<int, int, Color> f, bool srgb = true)
    {
        EnsureFolder(Dir + "/Textures");
        string path = Dir + "/Textures/" + name + ".png";
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
        t.SetPixels(px); t.Apply();
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null) { imp.sRGBTexture = srgb; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 4; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Color Lc(Color a, Color b, float t) { return Color.Lerp(a, b, t); }

    static Texture2D CaneAtlas()
    {
        // 768 x 256: [stalk | green leaf | dry leaf]
        return MakeTex("Sugarcane_Atlas", 768, 256, (x, y) =>
        {
            int tile = x / 256; float u = (x % 256) / 255f, v = y / 255f;
            if (tile == 0)
            {
                float streak = VNoise(u * 24f, v * 3f), fine = VNoise(u * 90f, v * 12f);
                Color c = Lc(new Color(0.40f, 0.55f, 0.16f), new Color(0.70f, 0.66f, 0.24f), streak * 0.8f);
                c = Lc(c, new Color(0.45f, 0.30f, 0.45f), Mathf.Clamp01(VNoise(u * 6f + 4f, v * 1.5f) - 0.55f) * 1.2f); // faint purple blush
                c *= 0.88f + 0.2f * fine;
                float fv = (v * 4f) % 1f;                                       // 4 nodes up the stalk
                float node = Mathf.Max(1f - Mathf.Abs(fv - 0.0f) * 28f, 1f - Mathf.Abs(fv - 1f) * 28f);
                if (node > 0f) c = Lc(c, new Color(0.30f, 0.30f, 0.10f), Mathf.Clamp01(node) * 0.75f);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(fv - 0.06f) * 22f);   // lighter growth ring just above the node
                c = Lc(c, new Color(0.85f, 0.82f, 0.55f), ring * 0.35f);
                c.a = 1f; return c;
            }
            if (tile == 1)
            {
                float d = Mathf.Abs(u - 0.5f);
                float streak = VNoise(u * 40f, v * 4f);
                Color c = Lc(new Color(0.20f, 0.46f, 0.10f), new Color(0.38f, 0.62f, 0.18f), streak);
                c *= 0.85f + 0.25f * (1f - v);
                if (d < 0.04f) c = Lc(c, new Color(0.78f, 0.88f, 0.55f), 1f - d / 0.04f);
                c *= 1f - Mathf.Clamp01((d - 0.36f) * 3f) * 0.35f;
                c.a = 1f; return c;
            }
            else
            {
                float d = Mathf.Abs(u - 0.5f);
                float streak = VNoise(u * 36f, v * 5f);
                Color c = Lc(new Color(0.52f, 0.40f, 0.18f), new Color(0.74f, 0.62f, 0.32f), streak);
                if (d < 0.035f) c = Lc(c, new Color(0.86f, 0.78f, 0.52f), 1f - d / 0.035f);
                c.a = 1f; return c;
            }
        });
    }

    static Texture2D FieldTex() { return MakeTex("CaneField_Ground", 256, 256, (x, y) =>
    {
        float u = x / 256f, v = y / 256f;
        float a = VNoise(u * 64f, v * 6f), b = VNoise(u * 18f, v * 18f), c2 = VNoise(u * 120f, v * 120f);
        Color c = Lc(new Color(0.16f, 0.30f, 0.08f), new Color(0.32f, 0.50f, 0.15f), a * 0.7f + c2 * 0.3f);
        c = Lc(c, new Color(0.38f, 0.30f, 0.14f), Mathf.Clamp01(b - 0.6f) * 1.2f); // dry litter patches
        c.a = 1f; return c;
    }); }

    static Texture2D RoadTex() { return MakeTex("RedDirt_Road", 256, 256, (x, y) =>
    {
        float u = x / 256f, v = y / 256f;
        float a = VNoise(u * 14f, v * 14f), b = VNoise(u * 60f, v * 60f), p = Hash(x / 2, y / 2);
        Color c = Lc(new Color(0.55f, 0.24f, 0.12f), new Color(0.72f, 0.36f, 0.19f), a * 0.7f + b * 0.3f);
        if (p > 0.965f) c = Lc(c, new Color(0.82f, 0.55f, 0.38f), 0.7f);   // pebbles
        if (p < 0.02f) c *= 0.8f;
        c.a = 1f; return c;
    }); }

    // ---------------------------------------------------------------- mesh
    class MB { public List<Vector3> v = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>(); }

    static void AddLeaf(MB mb, Vector3 org, float yaw, float pitchDeg, float len, float width, float droop, int tile, int segs)
    {
        float p = pitchDeg * Mathf.Deg2Rad, ya = yaw * Mathf.Deg2Rad;
        Vector3 dh = new Vector3(Mathf.Cos(ya), 0f, Mathf.Sin(ya)), side = new Vector3(-dh.z, 0f, dh.x);
        float u0 = tile * 0.3333f + 0.012f, u1 = tile * 0.3333f + 0.3333f - 0.012f;
        int b0 = mb.v.Count;
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            Vector3 pos = org + dh * (len * t * Mathf.Cos(p)) + Vector3.up * (len * t * Mathf.Sin(p)) - Vector3.up * (droop * len * t * t * 0.55f);
            float w = width * Mathf.Clamp01(t * 4f + 0.3f) * (1f - t * t * t);
            mb.v.Add(pos - side * w); mb.v.Add(pos + side * w);
            mb.uv.Add(new Vector2(u0, t)); mb.uv.Add(new Vector2(u1, t));
        }
        for (int i = 0; i < segs; i++)
        {
            int a = b0 + i * 2;
            mb.t.Add(a); mb.t.Add(a + 2); mb.t.Add(a + 1);
            mb.t.Add(a + 1); mb.t.Add(a + 2); mb.t.Add(a + 3);
        }
    }

    static void AddStalk(MB mb, Vector3 b, float h, Vector2 lean, float r, System.Random rng)
    {
        const int sides = 5, nodes = 4;
        var tt = new List<float>(); var rm = new List<float>();
        for (int k = 0; k <= nodes; k++)
        {
            if (k > 0) { tt.Add(k / (float)nodes - 0.012f); rm.Add(1.14f); }
            if (k < nodes) { tt.Add(k / (float)nodes + 0.012f); rm.Add(1f); }
        }
        System.Func<float, Vector3> centre = t => b + Vector3.up * (t * h) + new Vector3(lean.x, 0f, lean.y) * (t * t * h * 0.12f);
        int b0 = mb.v.Count;
        for (int i = 0; i < tt.Count; i++)
        {
            float t = tt[i]; Vector3 c = centre(t); float rad = r * (1f - 0.35f * t) * rm[i];
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f;
                mb.v.Add(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad);
                mb.uv.Add(new Vector2(0.015f + 0.30f * s / sides, Mathf.Clamp01(t)));
            }
        }
        int row = sides + 1;
        for (int i = 0; i < tt.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = b0 + i * row + s, bb = a + row;
                mb.t.Add(a); mb.t.Add(bb); mb.t.Add(a + 1);
                mb.t.Add(a + 1); mb.t.Add(bb); mb.t.Add(bb + 1);
            }
        System.Func<float> R = () => (float)rng.NextDouble();
        // old dry leaves hanging low on the stalk
        AddLeaf(mb, centre(0.30f), R() * 360f, 12f, h * 0.30f, 0.07f, 1.7f, 2, 3);
        // green blades up the stalk, arching out
        for (int k = 0; k < 2; k++)
        {
            float t = 0.55f + 0.2f * k + R() * 0.05f;
            AddLeaf(mb, centre(t) , R() * 360f, 38f + R() * 15f, h * (0.38f + R() * 0.10f), 0.09f, 1.1f, 1, 3);
        }
        // crown tuft
        for (int k = 0; k < 5; k++)
            AddLeaf(mb, centre(1f), k * 72f + R() * 30f, 50f + R() * 25f, h * (0.40f + R() * 0.10f), 0.085f, 0.8f, 1, 3);
    }

    static Mesh MakeClump(string name, int seed, int stalks)
    {
        var rng = new System.Random(seed);
        var mb = new MB();
        for (int i = 0; i < stalks; i++)
        {
            float ang = (i / (float)stalks) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.8f, rad = 0.05f + (float)rng.NextDouble() * 0.28f;
            Vector3 b = new Vector3(Mathf.Cos(ang) * rad, -0.05f, Mathf.Sin(ang) * rad);
            float h = 2.5f + (float)rng.NextDouble() * 0.9f;
            Vector2 lean = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (0.5f + (float)rng.NextDouble() * 0.7f);
            AddStalk(mb, b, h, lean, 0.030f + (float)rng.NextDouble() * 0.012f, rng);
        }
        var m = new Mesh { name = name };
        m.SetVertices(mb.v); m.SetUVs(0, mb.uv); m.SetTriangles(mb.t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        // push normals up a little so leaves read as soft foliage rather than hard sheets
        var n = m.normals; for (int i = 0; i < n.Length; i++) n[i] = (n[i] * 0.5f + Vector3.up * 0.8f).normalized; m.normals = n;
        return m;
    }

    public static GameObject[] CreateAssets()
    {
        EnsureFolder(Dir); EnsureFolder(Dir + "/Meshes"); EnsureFolder(Dir + "/Prefabs"); EnsureFolder(Dir + "/Materials");
        var atlas = CaneAtlas();
        string mp = Dir + "/Materials/Sugarcane.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, mp); }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", atlas); mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.12f); mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Cull", 0f);                    // double sided
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);

        var prefabs = new List<GameObject>();
        string[] names = { "Sugarcane_Clump_A", "Sugarcane_Clump_B", "Sugarcane_Clump_C" };
        int[] seeds = { 11, 27, 43 }; int[] counts = { 3, 4, 3 };
        for (int i = 0; i < 3; i++)
        {
            string mesh = Dir + "/Meshes/" + names[i] + ".asset";
            AssetDatabase.DeleteAsset(mesh);
            var m = MakeClump(names[i], seeds[i], counts[i]);
            AssetDatabase.CreateAsset(m, mesh);
            var go = new GameObject(names[i]);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            string pp = Dir + "/Prefabs/" + names[i] + ".prefab";
            var pf = PrefabUtility.SaveAsPrefabAsset(go, pp);
            Object.DestroyImmediate(go);
            prefabs.Add(pf);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Sugarcane: assets created in " + Dir);
        return prefabs.ToArray();
    }

    [MenuItem("Tools/Level 3/Create sugarcane assets only")]
    public static void CreateOnly() { CreateAssets(); }

    // ---------------------------------------------------------------- Level 3 scene
    static TerrainLayer MakeLayer(string name, Texture2D tex, float tile)
    {
        EnsureFolder(Dir + "/Layers");
        string p = Dir + "/Layers/" + name + ".terrainlayer";
        var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
        if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, p); }
        l.diffuseTexture = tex; l.tileSize = new Vector2(tile, tile); l.smoothness = 0f; l.metallic = 0f;
        EditorUtility.SetDirty(l);
        return l;
    }

    static long PlantCane(Terrain terrain, TerrainData td, Transform root, GameObject[] cane)
    {
        float S = td.size.x;
        int aw = td.alphamapWidth, ah = td.alphamapHeight;
        var alpha = td.GetAlphamaps(0, 0, aw, ah);
        td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);   // values below are instance counts, not % coverage
        var protos = new DetailPrototype[cane.Length];
        for (int i = 0; i < cane.Length; i++)
        {
            protos[i] = new DetailPrototype
            {
                usePrototypeMesh = true, prototype = cane[i], useInstancing = true, renderMode = DetailRenderMode.VertexLit,
                minWidth = 1.2f, maxWidth = 1.8f, minHeight = 0.9f, maxHeight = 1.3f, noiseSpread = 6f, density = 1f
            };
        }
        td.detailPrototypes = protos;
        int dr = 512;
        td.SetDetailResolution(dr, 16);
        // keep-clear rectangles around every building / wall / fence
        var clear = new List<Rect>();
        foreach (var n in new[] { "Camp", "Fence", "Stone Wall", "Stone Wall (1)", "Stone Wall (2)", "Stone Wall (3)", "Stone Wall (4)", "Rocks" })
        {
            var g = root.Find(n); if (g == null) continue;
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            { Bounds b = r.bounds; clear.Add(new Rect(b.min.x - 1.8f, b.min.z - 1.8f, b.size.x + 3.6f, b.size.z + 3.6f)); }
        }
        var maps = new int[protos.Length][,];
        for (int k = 0; k < protos.Length; k++) maps[k] = new int[dr, dr];
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        long planted = 0;
        for (int zi = 0; zi < dr; zi++)
        {
            float z = -S / 2f + (zi + 0.5f) / dr * S;
            int az = Mathf.Clamp(Mathf.RoundToInt((zi + 0.5f) / dr * (ah - 1)), 0, ah - 1);
            for (int xi = 0; xi < dr; xi++)
            {
                float x = -S / 2f + (xi + 0.5f) / dr * S;
                int ax = Mathf.Clamp(Mathf.RoundToInt((xi + 0.5f) / dr * (aw - 1)), 0, aw - 1);
                if (alpha[az, ax, 1] > 0.18f) continue;                      // road / camp ground stays clear
                bool blocked = false; var wp = new Vector2(cx0 + x, cz0 + z);
                for (int c = 0; c < clear.Count; c++) if (clear[c].Contains(wp)) { blocked = true; break; }
                if (blocked) continue;
                float patch = Fbm(x * 0.02f + 7f, z * 0.02f, 2);
                int cnt = patch < 0.2f ? 0 : (Hash(xi, zi) < 0.45f ? 1 : 2);   // a few small gaps, otherwise a solid field
                if (cnt == 0) continue;
                maps[(xi * 7 + zi * 13) % protos.Length][zi, xi] = cnt; planted += cnt;
            }
        }
        for (int k = 0; k < protos.Length; k++) td.SetDetailLayer(0, 0, k, maps[k]);
        terrain.detailObjectDistance = 90f; terrain.detailObjectDensity = 1f;
        terrain.heightmapPixelError = 5f; terrain.basemapDistance = 1500f; terrain.drawInstanced = true;
        EditorUtility.SetDirty(td);
        return planted;
    }

    [MenuItem("Tools/Level 3/Replant cane + remove mountain rocks (current scene)")]
    public static void Replant()
    {
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Level 3: scene has no terrain."); return; }
        var root = rootGo.transform;
        var rocks = root.Find("Rocks"); if (rocks != null) Object.DestroyImmediate(rocks.gameObject);
        var cane = new List<GameObject>();
        foreach (var n in new[] { "Sugarcane_Clump_A", "Sugarcane_Clump_B", "Sugarcane_Clump_C" })
            cane.Add(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/" + n + ".prefab"));
        long planted = PlantCane(terrain, terrain.terrainData, root, cane.ToArray());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Level 3: replanted " + planted + " clumps.");
    }

    [MenuItem("Tools/Level 3/Build Level 3 sugarcane field (new scene)")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Level 3: open the Level 2 scene first."); return; }
        var root = rootGo.transform;

        // 1. Save Level 2 as it is, then continue in a NEW scene file named Level 3
        EditorUtility.DisplayProgressBar("Level 3", "Saving Level 2 and creating Level 3...", 0.02f);
        string oldPath = scene.path;
        if (string.IsNullOrEmpty(oldPath)) { Debug.LogWarning("Level 3: save the scene first."); EditorUtility.ClearProgressBar(); return; }
        EditorSceneManager.SaveScene(scene);
        string newPath = System.IO.Path.GetDirectoryName(oldPath).Replace('\\', '/') + "/Level 3.unity";
        if (!EditorSceneManager.SaveScene(scene, newPath, false)) { Debug.LogWarning("Level 3: could not create " + newPath); EditorUtility.ClearProgressBar(); return; }

        // 2. remember where the camp / walls / fences stand so they can be put back on the new flat ground
        var units = new List<Transform>(); var oldY = new List<float>();
        float ty = terrain.transform.position.y;
        foreach (Transform g in root)
        {
            string n = g.name;
            if (!(n == "Camp" || n == "Fence" || n.StartsWith("Stone Wall") || n == "Rocks")) continue;
            if (n == "Rocks")
            {
                foreach (Transform it in g)
                    if (it.name != "Big Boulders" && it.name != "Little Rocks" && it.GetComponentInChildren<Renderer>() != null) { units.Add(it); oldY.Add(terrain.SampleHeight(it.position) + ty); }
            }
            else if (g.childCount == 0) { units.Add(g); oldY.Add(terrain.SampleHeight(g.position) + ty); }
            else foreach (Transform it in g) { units.Add(it); oldY.Add(terrain.SampleHeight(it.position) + ty); }
        }

        // 3. assets
        EditorUtility.DisplayProgressBar("Level 3", "Building sugarcane assets...", 0.1f);
        var cane = CreateAssets();
        var fieldLayer = MakeLayer("CaneField", FieldTex(), 6f);
        var roadLayer = MakeLayer("RedDirtRoad", RoadTex(), 5f);

        // 4. own copy of the terrain data (Level 2's terrain stays exactly as it was)
        EditorUtility.DisplayProgressBar("Level 3", "Flattening the field...", 0.3f);
        string srcData = AssetDatabase.GetAssetPath(terrain.terrainData);
        string dstData = Dir + "/Level3TerrainData.asset";
        AssetDatabase.DeleteAsset(dstData);
        AssetDatabase.CopyAsset(srcData, dstData);
        var td = AssetDatabase.LoadAssetAtPath<TerrainData>(dstData);
        terrain.terrainData = td;
        var tc = terrain.GetComponent<TerrainCollider>(); if (tc != null) tc.terrainData = td;

        int res = td.heightmapResolution; float S = 720f; float sy = 60f;
        td.size = new Vector3(S, sy, S);
        float WX = PathX(30f) - 7f, WZ = 30f, CX = PathX(14f) - 13f, CZ = 14f;
        var hts = new float[res, res];
        for (int zi = 0; zi < res; zi++)
        {
            float z = -S / 2f + zi / (res - 1f) * S;
            for (int xi = 0; xi < res; xi++)
            {
                float x = -S / 2f + xi / (res - 1f) * S;
                float m = 5f + 2.2f * Fbm(x * 0.008f + 3f, z * 0.008f + 1f, 3) + 0.5f * Fbm(x * 0.05f, z * 0.05f, 2);
                float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                m += Smooth01((edge - 290f) / 70f) * (22f + 10f * Fbm(x * 0.02f, z * 0.02f, 2));   // low hills ring the plantation
                hts[zi, xi] = m / sy;
            }
        }
        td.SetHeights(0, 0, hts);

        // 5. ground layers: layer 0 = cane-field floor, layer 1 = red dirt road, layer 2 = (kept) rock
        var old = td.terrainLayers;
        var layers = new List<TerrainLayer> { fieldLayer, roadLayer };
        if (old != null && old.Length > 2 && old[2] != null) layers.Add(old[2]);
        td.terrainLayers = layers.ToArray();
        int aw = td.alphamapWidth, ah = td.alphamapHeight, L = layers.Count;
        var alpha = new float[ah, aw, L];
        for (int zi = 0; zi < ah; zi++)
        {
            float z = -S / 2f + zi / (ah - 1f) * S;
            for (int xi = 0; xi < aw; xi++)
            {
                float x = -S / 2f + xi / (aw - 1f) * S;
                float road = 1f - Smooth01((Mathf.Abs(x - PathX(z)) - 2.8f) / 1.8f);
                float camp = (1f - Smooth01((Mathf.Sqrt((x - CX) * (x - CX) + (z - CZ) * (z - CZ)) - 8f) / 4f)) * 0.9f;
                float wall = (1f - Smooth01((Mathf.Sqrt((x - WX) * (x - WX) + (z - WZ) * (z - WZ)) - 7f) / 4f)) * 0.9f;
                float d = Mathf.Clamp01(Mathf.Max(road, Mathf.Max(camp, wall)) * (0.85f + 0.15f * VNoise(x * 0.4f, z * 0.4f)));
                alpha[zi, xi, 0] = 1f - d; alpha[zi, xi, 1] = d;
            }
        }
        td.SetAlphamaps(0, 0, alpha);

        // 6. take the mountain out: bushes, trees, boulders, hill markers
        EditorUtility.DisplayProgressBar("Level 3", "Clearing the mountain props...", 0.5f);
        foreach (var n in new[] { "Foliage", "Trees", "Big Bushes", "Outer Boulders", "Fill-in", "_ClimbApplied", "_World2Applied" })
        { var t = root.Find(n); if (t != null) Object.DestroyImmediate(t.gameObject); }
        var rocks0 = root.Find("Rocks"); if (rocks0 != null) Object.DestroyImmediate(rocks0.gameObject);
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] == null) continue;
            float ny = terrain.SampleHeight(units[i].position) + ty;
            units[i].position += Vector3.up * (ny - oldY[i]);
        }

        // 7. sugarcane everywhere except the road and the camp
        long planted = PlantCane(terrain, td, root, cane);

        // 8. clear blue day like the reference photo
        EditorUtility.DisplayProgressBar("Level 3", "Lighting...", 0.9f);
        MountainEnvironmentBuilder.ApplyLevel1Skybox();
        RenderSettings.fog = false;
        var sun = RenderSettings.sun;
        if (sun == null) foreach (var l in Object.FindObjectsByType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) { sun.color = new Color(1f, 0.96f, 0.88f); sun.intensity = 1.5f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(52f, -30f, 0f); }
        var haze = GameObject.Find("Golden Haze Volume"); if (haze != null) haze.SetActive(false);

        // 9. test player on the road, looking up the field
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        var sp = root.Find("Sample Player");
        if (sp != null)
        {
            float pz = -120f, px = PathX(pz);
            float y = terrain.SampleHeight(new Vector3(cx0 + px, 0f, cz0 + pz)) + ty + 0.2f;
            sp.position = new Vector3(cx0 + px, y, cz0 + pz);
            sp.rotation = Quaternion.LookRotation(new Vector3(PathX(pz + 30f) - px, 0f, 30f), Vector3.up);
        }

        // 10. add to Build Settings and save
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool has = false; foreach (var s in list) if (s.path == newPath) has = true;
        if (!has) { list.Add(new EditorBuildSettingsScene(newPath, true)); EditorBuildSettings.scenes = list.ToArray(); }
        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("Level 3: sugarcane field built and saved as " + newPath + " (" + planted + " clumps planted).");
    }

    // ---------------------------------------------------------------- polish: Level 2 colours, bumpy ground, rocks on the road, bushes
    static void GroundObj(Terrain terrain, Transform t, float sink)
    {
        var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return;
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        float ty = terrain.transform.position.y, low = float.MaxValue;
        for (int i = 0; i <= 2; i++) for (int j = 0; j <= 2; j++)
            low = Mathf.Min(low, terrain.SampleHeight(new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 2f), 0f, Mathf.Lerp(b.min.z, b.max.z, j / 2f))) + ty);
        t.position += Vector3.up * ((low - sink * b.size.y) - b.min.y);
    }

    [MenuItem("Tools/Level 3/Match Level 2 colours + bumpy ground + road rocks + bushes")]
    public static void Polish()
    {
        var l3 = EditorSceneManager.GetActiveScene();
        if (!l3.path.EndsWith("Level 3.unity")) { Debug.LogWarning("Level 3: open the Level 3 scene first."); return; }
        string l2Path = System.IO.Path.GetDirectoryName(l3.path).Replace('\\', '/') + "/Level 2.unity";

        // ---- 1. copy Level 2's sky, sun, ambient light and colour grading
        EditorUtility.DisplayProgressBar("Level 3", "Reading Level 2 lighting...", 0.05f);
        Material sky = null; AmbientMode amb = AmbientMode.Skybox; Color aSky = Color.gray, aEq = Color.gray, aGr = Color.gray; float aInt = 1f;
        bool fog = false; Color fogC = Color.gray; FogMode fogM = FogMode.ExponentialSquared; float fogD = 0.01f, fogS = 0f, fogE = 300f;
        bool haveSun = false; Color sunC = Color.white; float sunI = 1f; Quaternion sunR = Quaternion.identity; LightShadows sunSh = LightShadows.Soft;
        bool hazeOn = false; bool haveL2 = System.IO.File.Exists(l2Path);
        if (haveL2)
        {
            var l2 = EditorSceneManager.OpenScene(l2Path, OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(l2);
            sky = RenderSettings.skybox; amb = RenderSettings.ambientMode; aSky = RenderSettings.ambientSkyColor; aEq = RenderSettings.ambientEquatorColor; aGr = RenderSettings.ambientGroundColor; aInt = RenderSettings.ambientIntensity;
            fog = RenderSettings.fog; fogC = RenderSettings.fogColor; fogM = RenderSettings.fogMode; fogD = RenderSettings.fogDensity; fogS = RenderSettings.fogStartDistance; fogE = RenderSettings.fogEndDistance;
            foreach (var go in l2.GetRootGameObjects())
            {
                foreach (var l in go.GetComponentsInChildren<Light>(true))
                    if (!haveSun && l.type == LightType.Directional) { haveSun = true; sunC = l.color; sunI = l.intensity; sunR = l.transform.rotation; sunSh = l.shadows; }
                if (go.name == "Golden Haze Volume") hazeOn = go.activeSelf;
            }
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(l3);
            EditorSceneManager.CloseScene(l2, true);
        }
        if (haveL2)
        {
            RenderSettings.skybox = sky; RenderSettings.ambientMode = amb; RenderSettings.ambientSkyColor = aSky; RenderSettings.ambientEquatorColor = aEq; RenderSettings.ambientGroundColor = aGr; RenderSettings.ambientIntensity = aInt;
            RenderSettings.fog = fog; RenderSettings.fogColor = fogC; RenderSettings.fogMode = fogM; RenderSettings.fogDensity = fogD; RenderSettings.fogStartDistance = fogS; RenderSettings.fogEndDistance = fogE;
            foreach (var go in l3.GetRootGameObjects())
            {
                if (haveSun) foreach (var l in go.GetComponentsInChildren<Light>(true))
                    if (l.type == LightType.Directional) { l.color = sunC; l.intensity = sunI; l.transform.rotation = sunR; l.shadows = sunSh; }
                if (go.name == "Golden Haze Volume") go.SetActive(hazeOn);
            }
            DynamicGI.UpdateEnvironment();
        }

        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { EditorUtility.ClearProgressBar(); Debug.LogWarning("Level 3: no terrain."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;

        // ---- 2. same ground textures as Level 2 (grass + the Level 2 dirt path + rock)
        var l2data = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/MountainEnvironment/MountainTerrainData.asset");
        if (l2data != null && l2data.terrainLayers != null && l2data.terrainLayers.Length >= 2)
        {
            var nl = new TerrainLayer[td.terrainLayers.Length];
            for (int i = 0; i < nl.Length; i++) nl[i] = i < l2data.terrainLayers.Length ? l2data.terrainLayers[i] : td.terrainLayers[i];
            td.terrainLayers = nl;
        }
        // sugarcane picks up the same warm late-afternoon tone
        var caneMat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/Sugarcane.mat");
        if (caneMat != null) { caneMat.SetColor("_BaseColor", new Color(0.93f, 0.92f, 0.78f)); EditorUtility.SetDirty(caneMat); }

        // ---- 3. bumpy ground (once)
        EditorUtility.DisplayProgressBar("Level 3", "Making the ground bumpy...", 0.3f);
        var units = new List<Transform>(); var oldY = new List<float>();
        foreach (Transform g in root)
        {
            string n = g.name;
            if (n == "Camp" || n == "Fence" || n.StartsWith("Stone Wall") || n == "Sample Player")
            {
                if (g.childCount == 0 || n == "Sample Player") { units.Add(g); oldY.Add(terrain.SampleHeight(g.position) + ty); }
                else foreach (Transform it in g) { units.Add(it); oldY.Add(terrain.SampleHeight(it.position) + ty); }
            }
        }
        if (root.Find("_Bumpy") == null)
        {
            int res = td.heightmapResolution; float sy = td.size.y;
            var h = td.GetHeights(0, 0, res, res);
            float WX = PathX(30f) - 7f, WZ = 30f, CX = PathX(14f) - 13f, CZ = 14f;
            for (int zi = 0; zi < res; zi++)
            {
                float z = -S / 2f + zi / (res - 1f) * S;
                for (int xi = 0; xi < res; xi++)
                {
                    float x = -S / 2f + xi / (res - 1f) * S;
                    float bump = 2.6f * (Fbm(x * 0.022f + 31f, z * 0.022f + 17f, 3) - 0.5f) + 0.8f * (Fbm(x * 0.07f + 5f, z * 0.07f, 2) - 0.5f);
                    float road = Smooth01((Mathf.Abs(x - PathX(z)) - 3f) / 8f);                 // road itself stays gentle
                    float pad = Mathf.Min(Smooth01((Mathf.Sqrt((x - CX) * (x - CX) + (z - CZ) * (z - CZ)) - 12f) / 10f),
                                          Smooth01((Mathf.Sqrt((x - WX) * (x - WX) + (z - WZ) * (z - WZ)) - 10f) / 10f));
                    float k = Mathf.Lerp(0.35f, 1f, road) * pad;
                    h[zi, xi] = Mathf.Clamp01(h[zi, xi] + bump * k / sy);
                }
            }
            td.SetHeights(0, 0, h);
            var mk = new GameObject("_Bumpy"); mk.transform.SetParent(root, false);
        }
        for (int i = 0; i < units.Count; i++)
            if (units[i] != null) units[i].position += Vector3.up * (terrain.SampleHeight(units[i].position) + ty - oldY[i]);

        // ---- 4. small rocks on the road (the same little rocks used in Level 2)
        EditorUtility.DisplayProgressBar("Level 3", "Scattering road rocks...", 0.6f);
        var rng = new System.Random(System.Environment.TickCount);
        System.Func<float, float, float> R = (a, b) => a + (b - a) * (float)rng.NextDouble();
        foreach (var n in new[] { "Road Rocks", "Bushes" }) { var t = root.Find(n); if (t != null) Object.DestroyImmediate(t.gameObject); }
        var rockGrp = new GameObject("Road Rocks").transform; rockGrp.SetParent(root, false);
        var rocks = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("SM_LittleRock t:Prefab"))
        { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (pf != null) rocks.Add(pf); }
        int nr = 0;
        if (rocks.Count > 0)
            for (float z = -S / 2f + 15f; z < S / 2f - 15f; z += R(3f, 7f))
            {
                int cl = rng.NextDouble() < 0.3 ? rng.Next(2, 4) : 1;
                float side = R(-1f, 1f);
                for (int c = 0; c < cl; c++)
                {
                    float lz = z + R(-0.8f, 0.8f), lx = PathX(lz) + side * R(0.3f, 3.2f) + R(-0.5f, 0.5f);
                    float size = c == 0 ? R(0.25f, 0.7f) : R(0.15f, 0.4f);
                    var g = MountainEnvironmentBuilder.SpawnSized(rocks[rng.Next(rocks.Count)], new Vector3(cx0 + lx, 0f, cz0 + lz), Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), size, rockGrp);
                    if (g == null) continue;
                    g.transform.rotation = Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f));
                    GroundObj(terrain, g.transform, 0.3f);
                    g.name = "Road Rock " + (++nr).ToString("000");
                }
            }

        // ---- 5. bushes along the road edges and around the camp (the same bushes as Level 2)
        EditorUtility.DisplayProgressBar("Level 3", "Planting bushes...", 0.8f);
        var bushGrp = new GameObject("Bushes").transform; bushGrp.SetParent(root, false);
        var bushes = MountainEnvironmentBuilder.LoadPrefabs("Assets/YughuesFreeBushes2018/Prefabs/", nm => nm.StartsWith("P_Bush"));
        int nb = 0;
        if (bushes.Count > 0)
        {
            for (float z = -S / 2f + 20f; z < S / 2f - 20f; z += R(7f, 14f))
                foreach (float sgn in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.35) continue;
                    float lz = z + R(-3f, 3f), lx = PathX(lz) + sgn * R(3.8f, 5.5f);
                    var g = MountainEnvironmentBuilder.SpawnSized(bushes[rng.Next(bushes.Count)], new Vector3(cx0 + lx, terrain.SampleHeight(new Vector3(cx0 + lx, 0f, cz0 + lz)) + ty, cz0 + lz), Quaternion.Euler(0f, R(0f, 360f), 0f), R(1.0f, 2.0f), bushGrp);
                    if (g != null) g.name = "Bush " + (++nb).ToString("000");
                }
            float CX = PathX(14f) - 13f, CZ = 14f;
            for (int i = 0; i < 18; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(13f, 20f), lx = CX + Mathf.Cos(a) * d, lz = CZ + Mathf.Sin(a) * d;
                if (Mathf.Abs(lx - PathX(lz)) < 4f) continue;
                var g = MountainEnvironmentBuilder.SpawnSized(bushes[rng.Next(bushes.Count)], new Vector3(cx0 + lx, terrain.SampleHeight(new Vector3(cx0 + lx, 0f, cz0 + lz)) + ty, cz0 + lz), Quaternion.Euler(0f, R(0f, 360f), 0f), R(1.2f, 2.2f), bushGrp);
                if (g != null) g.name = "Bush " + (++nb).ToString("000");
            }
            MountainEnvironmentBuilder.ConvertToURP(bushGrp);
        }

        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        EditorSceneManager.MarkSceneDirty(l3);
        EditorSceneManager.SaveScene(l3);
        Debug.Log("Level 3: Level 2 lighting + ground colours applied" + (haveL2 ? "" : " (Level 2 scene not found, lighting unchanged)") + ", ground made bumpy, " + nr + " road rocks, " + nb + " bushes. Scene saved.");
    }
}
