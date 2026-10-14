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

    static float ArenaR = 0f;

    static long PlantCane(Terrain terrain, TerrainData td, Transform root, GameObject[] cane)
    {
        float S = td.size.x;
        int aw = td.alphamapWidth, ah = td.alphamapHeight;
        var alpha = td.GetAlphamaps(0, 0, aw, ah);
        td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);   // values below are instance counts, not % coverage
        var protos = new DetailPrototype[cane.Length];
        for (int i = 0; i < cane.Length; i++) protos[i] = CaneProto(cane[i]);
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
                if (ArenaR > 0f && x * x + z * z < (ArenaR + 0.5f) * (ArenaR + 0.5f)) continue;   // boss arena stays open
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
        SplitCaneShadows(td);
        EditorUtility.SetDirty(td);
        return planted;
    }

    // ---------------------------------------------------------------- cane shadows near the centre only
    // Detail meshes take their shadow setting from the prefab, so cane inside ShadowCaneR (the boss arena and the
    // cane wall around it) is moved onto shadow-casting copies of the clump prefabs; the rest of the field stays shadowless.
    const float ShadowCaneR = 70f;

    static DetailPrototype CaneProto(GameObject pf)
    {
        return new DetailPrototype
        {
            usePrototypeMesh = true, prototype = pf, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
            minWidth = 1.2f, maxWidth = 1.8f, minHeight = 0.9f, maxHeight = 1.3f, noiseSpread = 6f, density = 1f
        };
    }

    static GameObject ShadowCanePrefab(GameObject cane)
    {
        var go = Object.Instantiate(cane);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
        var pf = PrefabUtility.SaveAsPrefabAsset(go, Dir + "/Prefabs/" + cane.name + "_Shadow.prefab");
        Object.DestroyImmediate(go);
        return pf;
    }

    static long SplitCaneShadows(TerrainData td)
    {
        float S = td.size.x;
        int dw = td.detailWidth, dh = td.detailHeight;
        var protos = new List<DetailPrototype>(td.detailPrototypes);
        var layers = new List<int[,]>();
        for (int k = 0; k < protos.Count; k++) layers.Add(td.GetDetailLayer(0, 0, dw, dh, k));
        var pairs = new List<Vector2Int>();   // x = plain cane layer, y = its shadow-casting twin
        int n0 = protos.Count;
        for (int k = 0; k < n0; k++)
        {
            var pf = protos[k].prototype;
            if (pf == null || !pf.name.StartsWith("Sugarcane_Clump_") || pf.name.EndsWith("_Shadow")) continue;
            var sp = ShadowCanePrefab(pf);
            int s = protos.FindIndex(p => p.prototype != null && p.prototype.name == sp.name);
            if (s < 0) { protos.Add(CaneProto(sp)); layers.Add(new int[dh, dw]); s = protos.Count - 1; } else protos[s] = CaneProto(sp);
            pairs.Add(new Vector2Int(k, s));
        }
        td.detailPrototypes = protos.ToArray();
        long moved = 0; float r2 = ShadowCaneR * ShadowCaneR;
        for (int zi = 0; zi < dh; zi++)
        {
            float z = -S / 2f + (zi + 0.5f) / dh * S;
            for (int xi = 0; xi < dw; xi++)
            {
                float x = -S / 2f + (xi + 0.5f) / dw * S;
                bool near = x * x + z * z < r2;
                foreach (var p in pairs)
                {
                    int c = layers[p.x][zi, xi] + layers[p.y][zi, xi];
                    layers[p.x][zi, xi] = near ? 0 : c; layers[p.y][zi, xi] = near ? c : 0;
                    if (near) moved += c;
                }
            }
        }
        for (int k = 0; k < protos.Count; k++) td.SetDetailLayer(0, 0, k, layers[k]);
        EditorUtility.SetDirty(td);
        return moved;
    }

    [MenuItem("Tools/Level 3/Cane shadows near the centre only (current scene)")]
    public static void CaneShadowsNearCentre()
    {
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Level 3: scene has no terrain."); return; }
        long moved = SplitCaneShadows(terrain.terrainData);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Level 3: " + moved + " cane clumps within " + ShadowCaneR + "m of the centre now cast shadows.");
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
        { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (pf != null && !pf.name.Contains("GreenMoss")) rocks.Add(pf); }
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

    // ---------------------------------------------------------------- boss arena: wide open circle in the middle of the field
    const float BossR = 38f;

    [MenuItem("Tools/Level 3/Boss arena: open circle in the centre of the field")]
    public static void BossArena()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.path.EndsWith("Level 3.unity")) { Debug.LogWarning("Level 3: open the Level 3 scene first."); return; }
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Level 3: no terrain."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, sy = td.size.y, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        Vector3 C = new Vector3(cx0, 0f, cz0);
        EditorUtility.DisplayProgressBar("Level 3", "Clearing the arena...", 0.05f);

        // 1. buildings / walls / fences that stand inside the circle are moved (as whole groups) to just outside it
        int moved = 0;
        var groups = new List<Transform>();
        foreach (Transform g in root)
            if (g.name == "Camp" || g.name == "Fence" || g.name.StartsWith("Stone Wall")) groups.Add(g);
        var units = new List<Transform>(); var oldY = new List<float>();
        foreach (var g in groups)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 d = b.center - C; d.y = 0f;
            float ext = Mathf.Max(b.extents.x, b.extents.z);
            float dist = d.magnitude;
            if (dist - ext > BossR + 3f) continue;
            Vector3 dir = dist > 0.5f ? d / dist : Vector3.right;
            // record ground height of each piece before the move
            var pieces = new List<Transform>();
            if (g.childCount == 0) pieces.Add(g); else foreach (Transform it in g) pieces.Add(it);
            var before = new List<float>();
            foreach (var it in pieces) before.Add(terrain.SampleHeight(it.position) + ty);
            Undo.RecordObject(g, "Boss arena");
            g.position += dir * (BossR + 6f + ext - dist);
            for (int i = 0; i < pieces.Count; i++) { units.Add(pieces[i]); oldY.Add(before[i]); }   // re-grounded after flattening below
            moved++;
        }
        var sp = root.Find("Sample Player");
        if (sp != null) { units.Add(sp); oldY.Add(terrain.SampleHeight(sp.position) + ty); }
        // pieces that moved horizontally: drop them onto the ground at their new spot
        for (int i = 0; i < units.Count; i++)
            if (units[i] != sp) { float ny = terrain.SampleHeight(units[i].position) + ty; float wasOn = oldY[i]; units[i].position += Vector3.up * (ny - wasOn); oldY[i] = ny; }

        // 2. flatten the circle into an open, level clearing (soft 10 m edge)
        EditorUtility.DisplayProgressBar("Level 3", "Levelling the arena...", 0.3f);
        int res = td.heightmapResolution;
        var h = td.GetHeights(0, 0, res, res);
        double sum = 0; int cnt = 0;
        for (int zi = 0; zi < res; zi++) for (int xi = 0; xi < res; xi++)
        {
            float x = -S / 2f + xi / (res - 1f) * S, z = -S / 2f + zi / (res - 1f) * S;
            if (x * x + z * z < BossR * BossR) { sum += h[zi, xi]; cnt++; }
        }
        float target = cnt > 0 ? (float)(sum / cnt) : 0.1f;
        for (int zi = 0; zi < res; zi++) for (int xi = 0; xi < res; xi++)
        {
            float x = -S / 2f + xi / (res - 1f) * S, z = -S / 2f + zi / (res - 1f) * S;
            float d = Mathf.Sqrt(x * x + z * z);
            if (d > BossR + 10f) continue;
            float t = Smooth01((d - BossR) / 10f);
            float inner = target + 0.25f * (Fbm(x * 0.06f + 2f, z * 0.06f, 2) - 0.5f) / sy;   // very slight unevenness
            h[zi, xi] = Mathf.Lerp(inner, h[zi, xi], t);
        }
        td.SetHeights(0, 0, h);
        for (int i = 0; i < units.Count; i++)
            if (units[i] != null) units[i].position += Vector3.up * (terrain.SampleHeight(units[i].position) + ty - oldY[i]);

        // 3. arena floor: trampled Level 2 dirt with a few grass patches, fading into the field
        int aw = td.alphamapWidth, ah = td.alphamapHeight;
        var a = td.GetAlphamaps(0, 0, aw, ah); int L = a.GetLength(2);
        for (int zi = 0; zi < ah; zi++) for (int xi = 0; xi < aw; xi++)
        {
            float x = -S / 2f + xi / (aw - 1f) * S, z = -S / 2f + zi / (ah - 1f) * S;
            float d = Mathf.Sqrt(x * x + z * z);
            if (d > BossR + 4f) continue;
            float w = (1f - Smooth01((d - (BossR - 3f)) / 6f)) * Mathf.Lerp(0.55f, 1f, Fbm(x * 0.08f + 9f, z * 0.08f, 3));
            float dirt = Mathf.Max(a[zi, xi, 1], Mathf.Clamp01(w));
            a[zi, xi, 0] = 1f - dirt; a[zi, xi, 1] = dirt;
            for (int l = 2; l < L; l++) a[zi, xi, l] = 0f;
        }
        td.SetAlphamaps(0, 0, a);

        // 4. sugarcane: replant everything with the circle left open (cane forms the wall around it)
        EditorUtility.DisplayProgressBar("Level 3", "Replanting cane around the arena...", 0.55f);
        var cane = new List<GameObject>();
        foreach (var n in new[] { "Sugarcane_Clump_A", "Sugarcane_Clump_B", "Sugarcane_Clump_C" })
        { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/" + n + ".prefab"); if (pf != null) cane.Add(pf); }
        ArenaR = BossR;
        long planted = cane.Count > 0 ? PlantCane(terrain, td, root, cane.ToArray()) : 0;
        ArenaR = 0f;

        // 5. no road rocks / bushes inside the arena; a ring of bushes along its edge instead
        int removed = 0;
        foreach (var gname in new[] { "Road Rocks", "Bushes" })
        {
            var grp = root.Find(gname); if (grp == null) continue;
            var kill = new List<GameObject>();
            foreach (Transform it in grp) { Vector3 d = it.position - C; d.y = 0f; if (d.magnitude < BossR + 1f) kill.Add(it.gameObject); else it.position += Vector3.up * 0f; }
            foreach (var k in kill) { Object.DestroyImmediate(k); removed++; }
            // re-ground what is left near the new edge
            foreach (Transform it in grp) { Vector3 d = it.position - C; d.y = 0f; if (d.magnitude < BossR + 12f) GroundObj(terrain, it, gname == "Road Rocks" ? 0.3f : 0.08f); }
        }
        var oldRim = root.Find("Arena Edge Bushes"); if (oldRim != null) Object.DestroyImmediate(oldRim.gameObject);
        var rim = new GameObject("Arena Edge Bushes").transform; rim.SetParent(root, false);
        var bushes = MountainEnvironmentBuilder.LoadPrefabs("Assets/YughuesFreeBushes2018/Prefabs/", nm => nm.StartsWith("P_Bush"));
        var rng = new System.Random(77); int nb = 0;
        if (bushes.Count > 0)
        {
            for (float ang = 0f; ang < 360f; ang += 9f + (float)rng.NextDouble() * 8f)
            {
                float rad = ang * Mathf.Deg2Rad, rr = BossR + (float)rng.NextDouble() * 2.5f - 0.5f;
                float lx = Mathf.Cos(rad) * rr, lz = Mathf.Sin(rad) * rr;
                if (Mathf.Abs(lx - PathX(lz)) < 5f) continue;                       // keep the road openings clear
                var pos = new Vector3(cx0 + lx, terrain.SampleHeight(new Vector3(cx0 + lx, 0f, cz0 + lz)) + ty, cz0 + lz);
                var g = MountainEnvironmentBuilder.SpawnSized(bushes[rng.Next(bushes.Count)], pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), 1.4f + (float)rng.NextDouble() * 1.2f, rim);
                if (g != null) g.name = "Edge Bush " + (++nb).ToString("00");
            }
            MountainEnvironmentBuilder.ConvertToURP(rim);
        }

        // 6. a marker at the exact centre for placing the boss / triggers
        var mark = root.Find("Boss Arena Center");
        if (mark == null) { mark = new GameObject("Boss Arena Center").transform; mark.SetParent(root, false); }
        mark.position = new Vector3(cx0, terrain.SampleHeight(C) + ty, cz0);

        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level 3: boss arena (radius " + BossR + " m) cleared at the centre; " + moved + " building groups moved out of the circle, " + removed + " rocks/bushes removed from it, " + nb + " edge bushes, " + planted + " cane clumps. Scene saved.");
    }

    // ---------------------------------------------------------------- 4 big cover rocks inside the boss arena
    [MenuItem("Tools/Level 3/Boss arena - add 4 big cover rocks")]
    public static void ArenaCoverRocks()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Level 3: no terrain."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;

        var old = root.Find("Arena Cover Rocks"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var grp = new GameObject("Arena Cover Rocks").transform; grp.SetParent(root, false);
        // the same big boulders used on Level 2 (single rocks, not the flat groups)
        var rocks = MountainEnvironmentBuilder.LoadPrefabs("Assets/Rocks and Boulders 2/Rocks/Prefabs/", nm => !nm.Contains("LOD") && !nm.Contains("grup"));
        if (rocks.Count == 0) { Debug.LogWarning("Level 3: rock prefabs not found."); return; }
        var rng = new System.Random(2026);

        // four positions on a ring halfway out, one per quarter, turned away from the road
        float baseAng = 45f, ring = 17f; int n = 0;
        for (int q = 0; q < 4; q++)
        {
            float best = -1f; Vector2 pick = Vector2.zero;
            for (int tries = 0; tries < 24; tries++)                      // nudge the angle until the rock is well clear of the road
            {
                float a = (baseAng + q * 90f + (tries - 12) * 3f) * Mathf.Deg2Rad;
                float lx = Mathf.Cos(a) * ring, lz = Mathf.Sin(a) * ring;
                float clear = Mathf.Abs(lx - PathX(lz));
                if (clear > best) { best = clear; pick = new Vector2(lx, lz); }
                if (clear > 9f && tries >= 12) break;
            }
            var pf = rocks[rng.Next(rocks.Count)];
            var pos = new Vector3(cx0 + pick.x, terrain.SampleHeight(new Vector3(cx0 + pick.x, 0f, cz0 + pick.y)) + ty, cz0 + pick.y);
            var g = MountainEnvironmentBuilder.SpawnSized(pf, pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), 6.5f + (float)rng.NextDouble() * 1.5f, grp);
            if (g == null) continue;
            // make sure it is tall enough to hide behind (player is ~1.8 m)
            var rs = g.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y < 3f) g.transform.localScale = Vector3.Scale(g.transform.localScale, new Vector3(1f, 3f / b.size.y, 1f));
            GroundObj(terrain, g.transform, 0.12f);
            // a collider so the player and bullets can't pass through
            if (g.GetComponentInChildren<Collider>() == null)
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>()) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; }
            g.name = "Cover Rock " + (++n);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level 3: added " + n + " big cover rocks inside the boss arena. Scene saved.");
    }

    // ---------------------------------------------------------------- arena dressing: richer ground, small rocks at the cover rocks, spiky grass
    static GameObject MakeSpikyGrass(string name, int seed, Material mat)
    {
        var rng = new System.Random(seed);
        System.Func<float> R = () => (float)rng.NextDouble();
        var mb = new MB();
        int blades = 14 + rng.Next(6);
        for (int i = 0; i < blades; i++)
        {
            Vector3 o = new Vector3((R() - 0.5f) * 0.12f, -0.02f, (R() - 0.5f) * 0.12f);
            int tile = R() < 0.18f ? 2 : 1;                               // a few dry blades for colour variation
            AddLeaf(mb, o, R() * 360f, 58f + R() * 26f, 0.45f + R() * 0.45f, 0.022f + R() * 0.012f, 0.35f + R() * 0.5f, tile, 3);
        }
        var m = new Mesh { name = name };
        m.SetVertices(mb.v); m.SetUVs(0, mb.uv); m.SetTriangles(mb.t, 0);
        m.RecalculateNormals(); var n = m.normals; for (int i = 0; i < n.Length; i++) n[i] = (n[i] * 0.4f + Vector3.up).normalized; m.normals = n;
        m.RecalculateBounds();
        string mp = Dir + "/Meshes/" + name + ".asset"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(m, mp);
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
        var pf = PrefabUtility.SaveAsPrefabAsset(go, Dir + "/Prefabs/" + name + ".prefab");
        Object.DestroyImmediate(go);
        return pf;
    }

    [MenuItem("Tools/Level 3/Boss arena - richer ground + small rocks + spiky grass")]
    public static void ArenaDressing()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Level 3: no terrain."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        const float AR = 38f;
        var rng = new System.Random(System.Environment.TickCount);
        System.Func<float, float, float> Rn = (a, b) => a + (b - a) * (float)rng.NextDouble();

        // cover rocks (local centre + radius)
        var cover = new List<Vector3>();
        var coverGrp = root.Find("Arena Cover Rocks");
        if (coverGrp != null) foreach (Transform t in coverGrp)
        {
            var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            cover.Add(new Vector3(b.center.x - cx0, b.center.z - cz0, Mathf.Max(b.extents.x, b.extents.z)));
        }

        // ---- 1. ground: grass with large worn patches, fine mottling, gravel around the rocks
        EditorUtility.DisplayProgressBar("Level 3", "Texturing the arena floor...", 0.1f);
        int aw = td.alphamapWidth, ah = td.alphamapHeight, L = td.alphamapLayers;
        var al = td.GetAlphamaps(0, 0, aw, ah);
        for (int zi = 0; zi < ah; zi++)
        {
            float z = -S / 2f + zi / (ah - 1f) * S;
            if (Mathf.Abs(z) > AR + 4f) continue;
            for (int xi = 0; xi < aw; xi++)
            {
                float x = -S / 2f + xi / (aw - 1f) * S;
                float r = Mathf.Sqrt(x * x + z * z);
                if (r > AR + 4f) continue;
                float fade = 1f - Smooth01((r - AR) / 4f);
                float road = 1f - Smooth01((Mathf.Abs(x - PathX(z)) - 2.8f) / 1.8f);
                float big = Fbm(x * 0.045f + 3f, z * 0.045f + 8f, 3);
                float fine = Fbm(x * 0.28f + 1f, z * 0.28f, 2);
                float dirt = Mathf.Clamp01((big - 0.42f) * 2.2f + (fine - 0.5f) * 0.6f);
                float rock = Mathf.Clamp01((Fbm(x * 0.14f + 20f, z * 0.14f, 2) - 0.66f) * 2.5f) * 0.45f;   // gravelly specks
                foreach (var c in cover)
                {
                    float d = Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y));
                    rock = Mathf.Max(rock, (1f - Smooth01((d - c.z) / 3.5f)) * 0.65f * (0.7f + 0.3f * fine));
                    dirt = Mathf.Max(dirt, (1f - Smooth01((d - c.z - 1f) / 4f)) * 0.5f);
                }
                dirt = Mathf.Max(dirt, road);
                float wR = L > 2 ? rock * (1f - road) : 0f;
                float wD = dirt * (1f - wR);
                float wG = Mathf.Max(0f, 1f - wD - wR);
                // blend with whatever was there towards the rim
                for (int l = 0; l < L; l++)
                {
                    float nv = l == 0 ? wG : l == 1 ? wD : l == 2 ? wR : 0f;
                    al[zi, xi, l] = Mathf.Lerp(al[zi, xi, l], nv, fade);
                }
                float sum = 0f; for (int l = 0; l < L; l++) sum += al[zi, xi, l];
                if (sum > 0f) for (int l = 0; l < L; l++) al[zi, xi, l] /= sum;
            }
        }
        td.SetAlphamaps(0, 0, al);

        // ---- 2. small rocks huddled around each big cover rock
        EditorUtility.DisplayProgressBar("Level 3", "Placing small rocks...", 0.4f);
        var oldS = root.Find("Arena Small Rocks"); if (oldS != null) Object.DestroyImmediate(oldS.gameObject);
        var sGrp = new GameObject("Arena Small Rocks").transform; sGrp.SetParent(root, false);
        var little = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("SM_LittleRock t:Prefab"))
        { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (pf != null && !pf.name.Contains("GreenMoss")) little.Add(pf); }
        int ns = 0;
        if (little.Count > 0)
            foreach (var c in cover)
            {
                int k = rng.Next(6, 10);
                for (int i = 0; i < k; i++)
                {
                    float a = Rn(0f, Mathf.PI * 2f), d = c.z * Rn(0.85f, 1.1f) + Rn(0.3f, 2.2f);
                    float lx = c.x + Mathf.Cos(a) * d, lz = c.y + Mathf.Sin(a) * d;
                    var g = MountainEnvironmentBuilder.SpawnSized(little[rng.Next(little.Count)], new Vector3(cx0 + lx, 0f, cz0 + lz), Quaternion.identity, i < 3 ? Rn(0.7f, 1.3f) : Rn(0.25f, 0.7f), sGrp);
                    if (g == null) continue;
                    g.transform.rotation = Quaternion.Euler(Rn(-12f, 12f), Rn(0f, 360f), Rn(-12f, 12f));
                    GroundObj(terrain, g.transform, 0.25f);
                    g.name = "Small Rock " + (++ns).ToString("00");
                }
            }

        // ---- 3. spiky grass tufts (made here, same leaf texture as the cane) painted as terrain detail
        EditorUtility.DisplayProgressBar("Level 3", "Growing spiky grass...", 0.6f);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/Sugarcane.mat");
        var tufts = new[] { MakeSpikyGrass("SpikyGrass_A", 501, mat), MakeSpikyGrass("SpikyGrass_B", 733, mat) };
        int dw = td.detailWidth, dh = td.detailHeight;
        var protos = new List<DetailPrototype>(td.detailPrototypes);
        var keep = new List<int[,]>();
        for (int k = 0; k < protos.Count; k++) keep.Add(td.GetDetailLayer(0, 0, dw, dh, k));
        var tIdx = new List<int>();
        foreach (var tp in tufts)
        {
            int idx = protos.FindIndex(p => p.prototype != null && p.prototype.name == tp.name);
            var dp = new DetailPrototype { usePrototypeMesh = true, prototype = tp, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
                                           minWidth = 0.8f, maxWidth = 1.5f, minHeight = 0.8f, maxHeight = 1.4f, noiseSpread = 4f, density = 1f };
            if (idx < 0) { protos.Add(dp); keep.Add(new int[dh, dw]); idx = protos.Count - 1; } else protos[idx] = dp;
            tIdx.Add(idx);
        }
        td.detailPrototypes = protos.ToArray();
        foreach (var i in tIdx) keep[i] = new int[dh, dw];
        int nt = 0;
        for (int zi = 0; zi < dh; zi++)
        {
            float z = -S / 2f + (zi + 0.5f) / dh * S;
            if (Mathf.Abs(z) > AR + 3f) continue;
            for (int xi = 0; xi < dw; xi++)
            {
                float x = -S / 2f + (xi + 0.5f) / dw * S;
                float r = Mathf.Sqrt(x * x + z * z);
                if (r > AR + 2f) continue;
                if (Mathf.Abs(x - PathX(z)) < 2.2f) continue;                          // keep the road line itself clear
                bool onRock = false;
                foreach (var c in cover) if (Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y)) < c.z * 0.8f) { onRock = true; break; }
                if (onRock) continue;
                float patch = Fbm(x * 0.09f + 40f, z * 0.09f + 3f, 2);
                float nearRock = 0f;
                foreach (var c in cover) nearRock = Mathf.Max(nearRock, 1f - Smooth01((Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y)) - c.z) / 4f));
                float rim = Smooth01((r - (AR - 8f)) / 8f);
                float p = Mathf.Clamp01((patch - 0.5f) * 3f) + nearRock * 0.8f + rim * 0.6f;
                if (Hash(xi * 3 + 7, zi * 5 + 1) > p * 0.8f) continue;
                int cnt = p > 0.9f ? 2 : 1;
                keep[tIdx[(xi + zi) % tIdx.Count]][zi, xi] = cnt; nt += cnt;
            }
        }
        for (int k = 0; k < protos.Count; k++) td.SetDetailLayer(0, 0, k, keep[k]);

        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level 3: arena floor re-textured, " + ns + " small rocks around the cover rocks, " + nt + " spiky grass tufts. Scene saved.");
    }

    // ---------------------------------------------------------------- far trees on the hills around the plantation (Level 1 palms + Level 2 brown-bark trees)
    [MenuItem("Tools/Level 3/Far trees around the field")]
    public static void FarTrees()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Level 3: no terrain."); return; }
        var root = rootGo.transform; var td = terrain.terrainData;
        float S = td.size.x, ty = terrain.transform.position.y;
        float cx0 = terrain.transform.position.x + S / 2f, cz0 = terrain.transform.position.z + S / 2f;
        var rng = new System.Random(System.Environment.TickCount);
        System.Func<float, float, float> Rn = (a, b) => a + (b - a) * (float)rng.NextDouble();

        var palms = MountainEnvironmentBuilder.LoadPrefabs("Assets/Coconut Palm Tree Pack/Prefabs/", null);
        var broad = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab birch", new[] { "Assets/R3DWorks" }))
        { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (pf != null && pf.name.StartsWith("birch")) broad.Add(pf); }
        var makeTropical = typeof(MountainEnvironmentBuilder).GetMethod("MakeTropical", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (palms.Count == 0 && broad.Count == 0) { Debug.LogWarning("Level 3: no tree prefabs found."); return; }

        var old = root.Find("Far Trees"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var grp = new GameObject("Far Trees").transform; grp.SetParent(root, false);
        EditorUtility.DisplayProgressBar("Level 3", "Planting far trees...", 0.2f);

        // a tree line on the low hills that ring the plantation, thicker towards the very edge, in natural clumps
        float half = S / 2f - 6f, cell = 15f; int n = 0;
        for (float gz = -half; gz < half; gz += cell)
            for (float gx = -half; gx < half; gx += cell)
            {
                float x = gx + Rn(0f, cell), z = gz + Rn(0f, cell);
                float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                if (edge < 255f) continue;                                         // stays out of the playable field
                float band = Smooth01((edge - 255f) / 60f);
                float clump = Fbm(x * 0.03f + 50f, z * 0.03f + 11f, 2);
                if (rng.NextDouble() > band * (0.35f + clump * 0.9f)) continue;
                bool palm = palms.Count > 0;   // the birch-based trees render white at distance, so the far tree line is palms only
                var pf = palm ? palms[rng.Next(palms.Count)] : broad[rng.Next(broad.Count)];
                var pos = new Vector3(cx0 + x, terrain.SampleHeight(new Vector3(cx0 + x, 0f, cz0 + z)) + ty, cz0 + z);
                var g = MountainEnvironmentBuilder.SpawnSized(pf, pos, Quaternion.Euler(palm ? Rn(-5f, 5f) : 0f, Rn(0f, 360f), palm ? Rn(-5f, 5f) : 0f), palm ? Rn(9f, 15f) : Rn(9f, 14f), grp);
                if (g == null) continue;
                if (!palm && makeTropical != null) makeTropical.Invoke(null, new object[] { g });
                g.name = (palm ? "Far Palm " : "Far Tree ") + (++n).ToString("000");
            }
        MountainEnvironmentBuilder.ConvertToURP(grp);

        // keep them visible from far away (no popping to nothing)
        foreach (var lg in grp.GetComponentsInChildren<LODGroup>(true))
        {
            var l = lg.GetLODs(); if (l.Length < 2) continue;
            for (int i = 0; i < l.Length; i++)
                l[i].screenRelativeTransitionHeight = (i == l.Length - 1) ? Mathf.Min(l[i].screenRelativeTransitionHeight, 0.002f) : l[i].screenRelativeTransitionHeight * 0.4f;
            lg.SetLODs(l);
        }
        foreach (var cam in Object.FindObjectsByType<Camera>())
            if (cam.farClipPlane < 1500f) { Undo.RecordObject(cam, "Far clip"); cam.farClipPlane = 1500f; }

        EditorUtility.ClearProgressBar();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level 3: planted " + n + " far trees (palms + brown-bark trees) on the hills around the field. Scene saved.");
    }

    // ---------------------------------------------------------------- swap green / mossy rocks for the plain dark rocks used in every level
    [MenuItem("Tools/Level 3/Use dark rocks (remove green moss)")]
    public static void DarkRocks()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rootGo = GameObject.Find("Mountain Environment");
        if (rootGo == null) { Debug.LogWarning("Level 3: Mountain Environment not found."); return; }
        var terrain = rootGo.GetComponentInChildren<Terrain>();
        int cleared = 0, swapped = 0;

        // 1. remove the green tint that was painted on the big rocks
        foreach (var r in rootGo.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.HasPropertyBlock()) continue;
            var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb);
            Color c = mpb.GetColor("_BaseColor"); if (c == default(Color)) c = mpb.GetColor("_Color");
            if (c.g > 1.2f && c.g > c.r) { r.SetPropertyBlock(null); cleared++; }
        }

        // 2. replace every "…_GreenMoss" rock with its plain (dark) version, same place, rotation and size
        var plain = new Dictionary<string, GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab SM_LittleRock"))
        { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (pf != null && !pf.name.Contains("GreenMoss")) plain[pf.name] = pf; }
        var plainList = new List<GameObject>(plain.Values);
        var green = new List<Transform>();
        foreach (var t in rootGo.GetComponentsInChildren<Transform>(true))
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            bool isRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) == t.gameObject;
            if (isRoot && src != null && src.name.Contains("GreenMoss")) green.Add(t);
        }
        foreach (var t in green)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            string baseName = src.name.Replace("_GreenMoss", "");
            GameObject rep; if (!plain.TryGetValue(baseName, out rep)) rep = plainList.Count > 0 ? plainList[0] : null;
            if (rep == null) continue;
            var g = PrefabUtility.InstantiatePrefab(rep, t.parent) as GameObject;
            g.transform.SetPositionAndRotation(t.position, t.rotation);
            g.transform.localScale = t.localScale;
            g.transform.SetSiblingIndex(t.GetSiblingIndex());
            g.name = t.name.Contains("GreenMoss") ? t.name.Replace("_GreenMoss", "") : t.name;
            Undo.RegisterCreatedObjectUndo(g, "Dark rocks");
            Undo.DestroyObjectImmediate(t.gameObject);
            swapped++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Level 3: removed green tint from " + cleared + " rock meshes and swapped " + swapped + " green-moss rocks for the dark ones. Scene saved.");
    }
}
