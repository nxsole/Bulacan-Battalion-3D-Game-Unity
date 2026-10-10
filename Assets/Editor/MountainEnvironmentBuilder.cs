// Tools > Mountain Environment > Build
// Builds the film-inspired mountain trail environment inside the CURRENT scene.
// Written for Unity 6000.5 + URP 17.5. Uses your own palms and rocks.
// It never touches your existing terrain: everything goes under one root object
// named "Mountain Environment", placed beside your current terrain. Undo (Ctrl+Z) works.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class MountainEnvironmentBuilder
{
    const string RootName = "Mountain Environment";
    const string AssetDir = "Assets/MountainEnvironment";
    const string PalmDir = "Assets/Coconut Palm Tree Pack/Prefabs/";
    const string RockDir = "Assets/Rocks and Boulders 2/Rocks/Prefabs/";

    const float Size = 360f;
    const int HeightRes = 513;
    const int AlphaRes = 512;
    const int PalmCount = 75;
    const string Level1TerrainGuid = "02bc0e2176f6a4e4ea3faf0c622664cf";
    const string Level1SkyboxGuid = "7594ec8956dd1234c9a955546bacf359";
    const string SundownSkyGuid = "cede08c9fc471e64884909cd4bbeaf59";

    // Tweak these if the prefabs come out too big or too small.
    const float RockScale = 1.0f;
    const float WallStoneScale = 1.0f;
    const float PalmScaleMin = 0.9f;
    const float PalmScaleMax = 1.3f;

    static System.Random rng;
    static float R() { return (float)rng.NextDouble(); }
    static float R(float a, float b) { return a + (b - a) * R(); }

    static float WX, WZ, CX, CZ, H0, H1;
    static Terrain terrain;
    static float originX;

    // ---------------------------------------------------------------- noise / height
    static float Smooth(float a, float b, float v)
    {
        float t = Mathf.Clamp01((v - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    static float Hash(int ix, int iz)
    {
        double n = System.Math.Sin(ix * 127.1 + iz * 311.7) * 43758.5453;
        return (float)(n - System.Math.Floor(n));
    }

    static float VNoise(float x, float z)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        fx = fx * fx * (3f - 2f * fx);
        fz = fz * fz * (3f - 2f * fz);
        float a = Hash(ix, iz), b = Hash(ix + 1, iz), c = Hash(ix, iz + 1), d = Hash(ix + 1, iz + 1);
        return a + (b - a) * fx + (c - a) * fz + (a - b - c + d) * fx * fz;
    }

    static float Fbm(float x, float z, int oct)
    {
        float s = 0, a = 0.5f, f = 1, n = 0;
        for (int i = 0; i < oct; i++) { s += a * VNoise(x * f, z * f); n += a; a *= 0.5f; f *= 2.03f; }
        return s / n;
    }

    static float PathX(float z) { return 12f * Mathf.Sin(z * 0.045f) + 5f * Mathf.Sin(z * 0.11f + 1f) - z * 0.1f; }
    static float TMask(float x, float z) { return 1f - Smooth(14f, 40f, Mathf.Sqrt((x + 58f) * (x + 58f) + (z + 28f) * (z + 28f))); }

    static float HBase(float x, float z)
    {
        float h = (Fbm(x * 0.009f + 5f, z * 0.009f + 2f, 4) - 0.5f) * 46f
                + (Fbm(x * 0.035f, z * 0.035f + 9f, 3) - 0.5f) * 8f
                + (Fbm(x * 0.14f, z * 0.14f, 2) - 0.5f) * 1.1f;
        h += Smooth(-10f, -100f, z) * 24f;
        h += Smooth(35f, 110f, Mathf.Abs(x)) * 14f;
        float tm = TMask(x, z);
        h += tm * 9f;
        float q = h / 1.5f, f = Mathf.Floor(q);
        float tt = f + Smooth(0.7f, 1f, q - f);
        h = h + (tt * 1.5f - h) * tm * 0.85f;
        return h;
    }

    static float HRaw(float x, float z)
    {
        float h = HBase(x, z);
        float fm = 1f - Smooth(8f, 26f, Mathf.Sqrt((x - WX) * (x - WX) + (z - WZ) * (z - WZ)));
        h = h + (H0 - h) * fm * 0.8f;
        float f2 = 1f - Smooth(7f, 24f, Mathf.Sqrt((x - CX) * (x - CX) + (z - CZ) * (z - CZ)));
        h = h + (H1 - h) * f2 * 0.85f;
        return h;
    }

    static float H(float x, float z)
    {
        float h = HRaw(x, z);
        float px = PathX(z);
        float d = Mathf.Abs(x - px);
        float w = 1f - Smooth(2.2f, 6.5f, d);
        if (w > 0f)
        {
            float hp = HRaw(px, z) - 0.12f;
            h = h + (hp - h) * w;
            float rut = (d - 0.8f) / 0.25f;
            h -= 0.11f * Mathf.Exp(-rut * rut) * (1f - Smooth(2f, 3.2f, d));
        }
        return h;
    }

    // ---------------------------------------------------------------- helpers
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, i));
        AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }

    static Material Mat(string name, Color c, float smooth = 0.1f)
    {
        EnsureFolder(AssetDir + "/Materials");
        string path = AssetDir + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh);
        m.name = name;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Vector3 P(float x, float z)
    {
        float wx = originX + x;
        float y = terrain.SampleHeight(new Vector3(wx, 0f, z)) + terrain.transform.position.y;
        return new Vector3(wx, y, z);
    }

    static float Steep(float x, float z)
    {
        float nx = Mathf.Clamp01((x + Size / 2f) / Size), nz = Mathf.Clamp01((z + Size / 2f) / Size);
        return terrain.terrainData.GetSteepness(nx, nz);
    }

    static Transform Group(string name, Transform parent)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        return g.transform;
    }

    static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, float scale, Transform parent)
    {
        var g = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (g == null) return null;
        g.transform.position = pos;
        g.transform.rotation = rot;
        g.transform.localScale = Vector3.one * scale;
        return g;
    }

    static bool TryBounds(GameObject g, out Bounds b)
    {
        b = new Bounds();
        bool has = false;
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has;
    }

    // Spawns a prefab scaled so its largest dimension is "size" meters, with its bottom sitting on pos.y.
    static GameObject SpawnSized(GameObject prefab, Vector3 pos, Quaternion rot, float size, Transform parent)
    {
        var g = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (g == null) return null;
        g.transform.position = pos;
        g.transform.rotation = Quaternion.identity;
        g.transform.localScale = Vector3.one;
        Bounds b;
        float s = 1f;
        if (TryBounds(g, out b))
        {
            float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (ext > 0.01f) s = size / ext;
        }
        g.transform.localScale = Vector3.one * s;
        g.transform.rotation = rot;
        if (TryBounds(g, out b))
            g.transform.position += Vector3.up * ((pos.y - size * 0.08f) - b.min.y);
        return g;
    }

    static List<GameObject> LoadPrefabs(string dir, System.Func<string, bool> filter)
    {
        var list = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith(dir)) continue;
            string rest = path.Substring(dir.Length);
            if (rest.Contains("/")) continue;
            if (filter != null && !filter(System.IO.Path.GetFileNameWithoutExtension(path))) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) list.Add(go);
        }
        return list;
    }

    static Mesh MakeMesh(string name, List<Vector3> v, List<int> tri)
    {
        EnsureFolder(AssetDir + "/Meshes");
        string path = AssetDir + "/Meshes/" + name + ".asset";
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old != null) return old;
        var m = new Mesh();
        m.name = name;
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        var uv = new List<Vector2>();
        for (int i = 0; i < v.Count; i++) uv.Add(new Vector2(v[i].x * 0.3f, v[i].y * 0.3f + v[i].z * 0.3f));
        m.SetUVs(0, uv);
        m.RecalculateNormals();
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    static Mesh PyramidMesh(string name, float halfWidth, float height)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        var apex = new Vector3(0, height, 0);
        var a = new Vector3(-halfWidth, 0, -halfWidth); var b = new Vector3(halfWidth, 0, -halfWidth);
        var c = new Vector3(halfWidth, 0, halfWidth); var d = new Vector3(-halfWidth, 0, halfWidth);
        Tri(v, t, a, apex, b); Tri(v, t, b, apex, c); Tri(v, t, c, apex, d); Tri(v, t, d, apex, a);
        return MakeMesh(name, v, t);
    }

    static Mesh TentMesh()
    {
        var v = new List<Vector3>(); var t = new List<int>();
        float w = 1.7f, h = 2.5f, l = 2.1f;
        var fl = new Vector3(-w, 0, l); var fr = new Vector3(w, 0, l); var fa = new Vector3(0, h, l);
        var bl = new Vector3(-w, 0, -l); var br = new Vector3(w, 0, -l); var ba = new Vector3(0, h, -l);
        Tri(v, t, fl, fa, fr);
        Tri(v, t, bl, br, ba);
        Tri(v, t, fl, bl, fa); Tri(v, t, bl, ba, fa);
        Tri(v, t, fr, fa, br); Tri(v, t, br, fa, ba);
        return MakeMesh("TentMesh", v, t);
    }

    // ---------------------------------------------------------------- build
    [MenuItem("Tools/Mountain Environment/Build (adds to current scene)")]
    public static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Mountain Environment", "A Mountain Environment already exists in this scene. Replace it?", "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(existing);
        }

        try
        {
            rng = new System.Random(23);
            WX = PathX(30f) - 7f; WZ = 30f; CX = PathX(14f) - 13f; CZ = 14f;
            H0 = HBase(WX, WZ); H1 = HBase(CX, CZ);

            // place it next to whatever terrain is already in the scene
            originX = 0f;
            TerrainLayer[] userLayers = null;
            var current = Terrain.activeTerrains;
            if (current != null && current.Length > 0)
            {
                float maxX = float.MinValue;
                foreach (var t in current)
                {
                    if (t == null || t.terrainData == null) continue;
                    maxX = Mathf.Max(maxX, t.transform.position.x + t.terrainData.size.x);
                    if (userLayers == null && t.terrainData.terrainLayers != null && t.terrainData.terrainLayers.Length > 0)
                        userLayers = t.terrainData.terrainLayers;
                }
                if (maxX > float.MinValue) originX = maxX + 60f + Size / 2f;
            }

            EnsureFolder(AssetDir);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Mountain Environment");

            // ---- terrain
            EditorUtility.DisplayProgressBar("Mountain Environment", "Sculpting terrain...", 0.05f);
            var raw = new float[HeightRes, HeightRes];
            float mn = float.MaxValue, mx = float.MinValue;
            for (int zi = 0; zi < HeightRes; zi++)
            {
                float z = -Size / 2f + Size * zi / (HeightRes - 1f);
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + Size * xi / (HeightRes - 1f);
                    float h = H(x, z);
                    raw[zi, xi] = h;
                    if (h < mn) mn = h;
                    if (h > mx) mx = h;
                }
            }
            float range = mx - mn + 0.01f;
            for (int zi = 0; zi < HeightRes; zi++)
                for (int xi = 0; xi < HeightRes; xi++)
                    raw[zi, xi] = (raw[zi, xi] - mn) / range;

            var td = new TerrainData();
            td.heightmapResolution = HeightRes;
            td.size = new Vector3(Size, range, Size);
            td.SetHeights(0, 0, raw);
            AssetDatabase.CreateAsset(td, AssetDir + "/MountainTerrainData.asset");

            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Mountain Terrain";
            tgo.transform.SetParent(root.transform, false);
            tgo.transform.position = new Vector3(originX - Size / 2f, 0f, -Size / 2f);
            terrain = tgo.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 5f;

            // ---- terrain layers (your own grass first, simple dirt/rock fallbacks)
            EditorUtility.DisplayProgressBar("Mountain Environment", "Painting terrain layers...", 0.2f);
            TerrainLayer grass = null, dirt = null, rock = null;

            // Level 1's terrain is the reference look: take its layers first so the grass matches.
            TerrainData level1Data = null;
            string l1Path = AssetDatabase.GUIDToAssetPath(Level1TerrainGuid);
            if (!string.IsNullOrEmpty(l1Path))
            {
                var l1 = AssetDatabase.LoadAssetAtPath<TerrainData>(l1Path);
                level1Data = l1;
                if (l1 != null && l1.terrainLayers != null)
                    foreach (var l in l1.terrainLayers)
                    {
                        if (l == null) continue;
                        string ln = l.name.ToLowerInvariant();
                        if (grass == null && ln.Contains("grass")) grass = l;
                        else if (dirt == null && (ln.Contains("dirt") || ln.Contains("mud") || ln.Contains("soil") || ln.Contains("path") || ln.Contains("ground"))) dirt = l;
                        else if (rock == null && (ln.Contains("rock") || ln.Contains("stone") || ln.Contains("cliff"))) rock = l;
                    }
                if (grass == null && l1 != null && l1.terrainLayers != null && l1.terrainLayers.Length > 0) grass = l1.terrainLayers[0];
                Debug.Log("Mountain Environment: using Level 1 terrain layers from " + l1Path);
            }
            else Debug.LogWarning("Mountain Environment: Level 1 terrain data not found, falling back to other layers.");

            foreach (var guid in AssetDatabase.FindAssets("t:TerrainLayer"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith(AssetDir)) continue;
                string n = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
                if (grass == null && n.Contains("grass")) grass = l;
                else if (dirt == null && (n.Contains("dirt") || n.Contains("mud") || n.Contains("soil") || n.Contains("path") || n.Contains("ground") || n.Contains("sand"))) dirt = l;
                else if (rock == null && (n.Contains("rock") || n.Contains("stone") || n.Contains("cliff"))) rock = l;
            }
            if (grass == null && userLayers != null) grass = userLayers[0];
            if (grass == null) grass = MakeLayer("MountainGrass", new Color(0.45f, 0.66f, 0.12f), 5f);
            if (dirt == null) dirt = MakeLayer("MountainDirt", new Color(0.62f, 0.38f, 0.24f), 4f);
            if (rock == null) rock = MakeLayer("MountainRock", new Color(0.42f, 0.44f, 0.48f), 6f);
            td.terrainLayers = new[] { grass, dirt, rock };

            td.alphamapResolution = AlphaRes;
            var alpha = new float[AlphaRes, AlphaRes, 3];
            for (int zi = 0; zi < AlphaRes; zi++)
            {
                float nz = zi / (AlphaRes - 1f);
                float z = -Size / 2f + nz * Size;
                for (int xi = 0; xi < AlphaRes; xi++)
                {
                    float nx = xi / (AlphaRes - 1f);
                    float x = -Size / 2f + nx * Size;
                    float steep = td.GetSteepness(nx, nz);
                    float v = Fbm(x * 0.12f, z * 0.12f, 2);
                    float path = (1f - Smooth(1.0f, 3.8f, Mathf.Abs(x - PathX(z)))) * (0.75f + 0.25f * v);
                    float campW = (1f - Smooth(5f, 10.5f, Mathf.Sqrt((x - CX) * (x - CX) + (z - CZ) * (z - CZ)))) * 0.9f;
                    float wd = Mathf.Clamp01(Mathf.Max(path, campW));
                    float wr = Smooth(26f, 44f, steep) * (1f - TMask(x, z) * 0.7f) * (1f - wd);
                    float wg = Mathf.Max(0f, 1f - wd - wr);
                    float sum = wg + wd + wr;
                    alpha[zi, xi, 0] = wg / sum; alpha[zi, xi, 1] = wd / sum; alpha[zi, xi, 2] = wr / sum;
                }
            }
            td.SetAlphamaps(0, 0, alpha);

            // ---- grass blades (same detail prototypes Level 1 uses)
            EditorUtility.DisplayProgressBar("Mountain Environment", "Planting grass blades...", 0.3f);
            if (level1Data != null && level1Data.detailPrototypes != null && level1Data.detailPrototypes.Length > 0)
            {
                td.SetDetailResolution(AlphaRes, 16);
                td.detailPrototypes = level1Data.detailPrototypes;
                int protoCount = td.detailPrototypes.Length;
                var maps = new int[protoCount][,];
                for (int k = 0; k < protoCount; k++) maps[k] = new int[AlphaRes, AlphaRes];
                for (int zi = 0; zi < AlphaRes; zi++)
                    for (int xi = 0; xi < AlphaRes; xi++)
                    {
                        float gw = alpha[zi, xi, 0];
                        if (gw < 0.5f) continue;
                        int k = (xi * 7 + zi * 13) % protoCount;
                        maps[k][zi, xi] = Mathf.RoundToInt(R(4f, 10f) * gw);
                    }
                for (int k = 0; k < protoCount; k++) td.SetDetailLayer(0, 0, k, maps[k]);
                terrain.detailObjectDistance = 160f;
                terrain.detailObjectDensity = 1f;
            }
            else Debug.LogWarning("Mountain Environment: Level 1 has no grass detail prototypes, so no grass blades were painted.");

            ApplyLevel1Skybox();

            // ---- materials
            var matThatch = Mat("MtnThatch", new Color(0.62f, 0.50f, 0.28f), 0.05f);
            var matSkirt = Mat("MtnThatchSkirt", new Color(0.80f, 0.70f, 0.45f), 0.05f);
            var matWeave = Mat("MtnWeave", new Color(0.66f, 0.54f, 0.35f), 0.05f);
            var matWood = Mat("MtnWood", new Color(0.48f, 0.40f, 0.27f), 0.05f);
            var matDark = Mat("MtnDarkWood", new Color(0.16f, 0.12f, 0.09f), 0.05f);
            var matTent = Mat("MtnCanvas", new Color(0.91f, 0.88f, 0.77f), 0.05f);
            var matBag = Mat("MtnSandbag", new Color(0.75f, 0.67f, 0.49f), 0.05f);
            var matBamboo = Mat("MtnBamboo", new Color(0.54f, 0.45f, 0.28f), 0.2f);

            // ---- palms
            EditorUtility.DisplayProgressBar("Mountain Environment", "Planting coconut palms...", 0.45f);
            var palms = LoadPrefabs(PalmDir, null);
            var palmRoot = Group("Palms", root.transform);
            if (palms.Count == 0) Debug.LogWarning("Mountain Environment: no palm prefabs found in " + PalmDir);
            else
            {
                int placed = 0, tries = 0;
                while (placed < PalmCount && tries < 8000)
                {
                    tries++;
                    float x = (R() - 0.5f) * 200f, z = R(-105f, 55f);
                    if (Mathf.Abs(x - PathX(z)) < 7f) continue;
                    if (Dist(x, z, CX, CZ) < 11f) continue;
                    if (Dist(x, z, WX, WZ) < 15f) continue;
                    if (Fbm(x * 0.05f + 3f, z * 0.05f, 2) < 0.42f && R() > 0.12f) continue;
                    if (Steep(x, z) > 38f) continue;
                    var pos = P(x, z) + Vector3.down * 0.15f;
                    var rot = Quaternion.Euler(R(-4f, 4f), R(0f, 360f), R(-4f, 4f));
                    Spawn(palms[rng.Next(palms.Count)], pos, rot, R(PalmScaleMin, PalmScaleMax), palmRoot);
                    placed++;
                }
            }

            // ---- rocks and cliffs
            EditorUtility.DisplayProgressBar("Mountain Environment", "Stacking boulders...", 0.6f);
            var singles = LoadPrefabs(RockDir, n => !n.Contains("LOD") && !n.Contains("grup"));
            var groups = LoadPrefabs(RockDir, n => n.Contains("grup") && !n.Contains("LOD"));
            if (groups.Count == 0) groups = singles;
            var rockRoot = Group("Rocks", root.transform);
            if (singles.Count == 0) Debug.LogWarning("Mountain Environment: no rock prefabs found in " + RockDir);
            else
            {
                Cliff(rockRoot, groups, singles, PathX(0f) + 17f, 0f, 5, 6f, 2.2f, 4.5f);
                Cliff(rockRoot, groups, singles, PathX(-25f) - 19f, -25f, 5, 8f, 3f, 6f);
                Cliff(rockRoot, groups, singles, -60f, -45f, 5, 10f, 3f, 7f);
                Cliff(rockRoot, groups, singles, WX + 17f, WZ + 3f, 3, 3f, 1.5f, 3f);
                Cliff(rockRoot, groups, singles, PathX(-55f) + 20f, -55f, 5, 8f, 3.5f, 8f);
                int placed = 0, tries = 0;
                while (placed < 55 && tries < 5000)
                {
                    tries++;
                    float x = (R() - 0.5f) * 220f, z = R(-120f, 45f);
                    if (Mathf.Abs(x - PathX(z)) < 6f) continue;
                    if (Dist(x, z, CX, CZ) < 10f) continue;
                    if (Steep(x, z) > 40f && R() > 0.1f) continue;
                    SpawnSized(singles[rng.Next(singles.Count)], P(x, z),
                        Quaternion.Euler(0f, R(0f, 360f), 0f), R(1.5f, 4.5f) * RockScale, rockRoot);
                    placed++;
                }
            }

            // ---- stone wall overlook
            EditorUtility.DisplayProgressBar("Mountain Environment", "Building the stone wall...", 0.75f);
            var wall = Group("Stone Wall", root.transform);
            if (singles.Count > 0)
            {
                for (int layer = 0; layer < 3; layer++)
                    for (int i = 0; i < 13; i++)
                    {
                        float sx = WX - 8.5f + i * 1.35f + (layer % 2) * 0.6f;
                        var pos = P(sx, WZ + R(0f, 0.3f));
                        pos.y += layer * 0.55f;
                        SpawnSized(singles[rng.Next(singles.Count)], pos, Quaternion.Euler(0f, R(0f, 360f), 0f),
                            R(1.1f, 1.5f) * WallStoneScale, wall);
                    }
            }
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 7; i++)
                {
                    float bx = WX - 4.5f + i * 1.55f + row * 0.7f;
                    var pos = P(bx, WZ + 1.5f);
                    pos.y += 0.28f + row * 0.5f;
                    var bag = Prim(PrimitiveType.Sphere, "Sandbag", wall, Vector3.zero, new Vector3(1.9f, 0.76f, 1.1f), matBag);
                    bag.transform.position = pos;
                    bag.transform.rotation = Quaternion.Euler(0f, R(-20f, 20f), 0f);
                }
            Stake(wall, P(WX - 10f, WZ + 0.6f), 3.2f, matBamboo, matDark);
            Stake(wall, P(WX + 9.6f, WZ + 0.8f), 3.0f, matBamboo, matDark);
            Stake(wall, P(WX + 11f, WZ + 0.3f), 2.6f, matBamboo, matDark);

            // ---- fence
            var fence = Group("Fence", root.transform);
            Fence(fence, new Vector2(WX + 13f, WZ - 3f), new Vector2(WX + 25f, WZ - 23f), 6, matWood);

            // ---- camp
            EditorUtility.DisplayProgressBar("Mountain Environment", "Pitching tents and huts...", 0.88f);
            var camp = Group("Camp", root.transform);
            GameObject tentPrefab = null;
            foreach (var guid in AssetDatabase.FindAssets("tent t:Prefab"))
            {
                tentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (tentPrefab != null) break;
            }
            var tentMesh = TentMesh();
            Vector3[] tp = { new Vector3(CX - 4f, 0, CZ - 3f), new Vector3(CX + 3.5f, 0, CZ - 5f), new Vector3(CX - 1f, 0, CZ + 5f) };
            float[] ty = { 9f, -14f, 77f };
            for (int i = 0; i < 3; i++)
            {
                var pos = P(tp[i].x, tp[i].z);
                var rot = Quaternion.Euler(0f, ty[i], 0f);
                if (tentPrefab != null) { Spawn(tentPrefab, pos, rot, 1f, camp); continue; }
                var t = new GameObject("Tent");
                t.transform.SetParent(camp, false);
                t.transform.SetPositionAndRotation(pos, rot);
                t.transform.localScale = Vector3.one * 1.1f;
                var mf = t.AddComponent<MeshFilter>(); mf.sharedMesh = tentMesh;
                var mr = t.AddComponent<MeshRenderer>(); mr.sharedMaterial = matTent;
                Prim(PrimitiveType.Cube, "Door", t.transform, new Vector3(0, 0.7f, 2.08f), new Vector3(1.1f, 1.4f, 0.06f), matDark);
            }
            for (int i = 0; i < 5; i++)
            {
                var pos = P(CX + R(2f, 7f), CZ + R(2f, 6f));
                var c = Prim(PrimitiveType.Cube, "Crate", camp, Vector3.zero, new Vector3(0.9f, 0.7f, 0.9f), matWood);
                c.transform.position = pos + Vector3.up * 0.35f;
                c.transform.rotation = Quaternion.Euler(0f, R(0f, 180f), 0f);
            }

            Hut(camp, P(PathX(-6f) - 15f, -6f), -23f, 1.1f, matThatch, matSkirt, matWeave, matWood, matDark);
            Hut(camp, P(PathX(-30f) + 17f, -30f), 46f, 1.4f, matThatch, matSkirt, matWeave, matWood, matDark);
            Hut(camp, P(PathX(50f) - 22f, 50f), 6f, 1.0f, matThatch, matSkirt, matWeave, matWood, matDark);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.LookAt(P(WX, WZ) + Vector3.up * 3f, Quaternion.LookRotation(Vector3.back), 14f);

            Debug.Log("Mountain Environment built. Press Ctrl+S to save the scene. Undo with Ctrl+Z, or Tools > Mountain Environment > Remove.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Tools/Mountain Environment/Remove")]
    public static void Remove()
    {
        var root = GameObject.Find(RootName);
        if (root == null) { Debug.Log("No Mountain Environment in this scene."); return; }
        Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // ---------------------------------------------------------------- lighting (optional)
    [MenuItem("Tools/Mountain Environment/Use Level 1 skybox")]
    public static void ApplyLevel1Skybox()
    {
        string path = AssetDatabase.GUIDToAssetPath(Level1SkyboxGuid);
        var mat = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { Debug.LogWarning("Mountain Environment: Level 1 skybox material not found."); return; }
        RenderSettings.skybox = mat;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static Light FindSun()
    {
        Light sun = RenderSettings.sun;
        if (sun != null) return sun;
        foreach (var l in Object.FindObjectsByType<Light>())
            if (l.type == LightType.Directional) return l;
        return null;
    }

    // One click: warm low sun, golden haze fog, warm sky, and a post-processing volume (warm tint, bloom, vignette).
    [MenuItem("Tools/Mountain Environment/Golden hour (film look)")]
    public static void GoldenHour()
    {
        EnsureFolder(AssetDir);

        var skyShader = Shader.Find("Skybox/Procedural");
        if (skyShader != null)
        {
            string mp = AssetDir + "/MtnGoldenSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (sky == null) { sky = new Material(skyShader); AssetDatabase.CreateAsset(sky, mp); }
            sky.SetColor("_SkyTint", new Color(0.86f, 0.66f, 0.42f));
            sky.SetColor("_GroundColor", new Color(0.52f, 0.46f, 0.30f));
            sky.SetFloat("_AtmosphereThickness", 1.6f);
            sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_SunSize", 0.08f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        Light sun = FindSun();
        if (sun != null)
        {
            Undo.RecordObject(sun, "Golden hour");
            Undo.RecordObject(sun.transform, "Golden hour");
            sun.color = new Color(1f, 0.78f, 0.48f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(18f, -35f, 0f);
            RenderSettings.sun = sun;
        }
        else Debug.LogWarning("Mountain Environment: no directional light found, sun not changed.");

        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.85f, 0.80f, 0.61f);
        RenderSettings.fogDensity = 0.006f;
        DynamicGI.UpdateEnvironment();

        var volGo = GameObject.Find("Golden Haze Volume");
        if (volGo == null)
        {
            volGo = new GameObject("Golden Haze Volume");
            Undo.RegisterCreatedObjectUndo(volGo, "Golden hour");
        }
        var vol = volGo.GetComponent<Volume>();
        if (vol == null) vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;

        string pp = AssetDir + "/GoldenHaze.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(pp);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, pp);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.colorFilter.Override(new Color(1f, 0.92f, 0.76f));
            ca.saturation.Override(12f);
            ca.contrast.Override(8f);
            var bl = profile.Add<Bloom>(true);
            bl.intensity.Override(0.45f);
            bl.threshold.Override(1f);
            var vg = profile.Add<Vignette>(true);
            vg.intensity.Override(0.28f);
            AssetDatabase.AddObjectToAsset(ca, profile);
            AssetDatabase.AddObjectToAsset(bl, profile);
            AssetDatabase.AddObjectToAsset(vg, profile);
            EditorUtility.SetDirty(profile);
        }
        vol.sharedProfile = profile;

        var cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: golden hour applied. In the Scene view, enable Post Processing in the Effects dropdown to preview the color grade.");
    }

    // Golden hour + the SkySeries "Sundown" skybox, with the sun disc and the directional light lined up
    // exactly like the SkySeries sundown demo scene. Level 1 stays noon, Level 2 is the sun about to set.
    [MenuItem("Tools/Mountain Environment/Sunset skybox (SkySeries sundown)")]
    public static void SunsetSkybox()
    {
        string path = AssetDatabase.GUIDToAssetPath(SundownSkyGuid);
        var mat = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { Debug.LogWarning("Mountain Environment: SkySeries sundown skybox (MegaSun.mat) not found."); return; }

        GoldenHour();   // warm fog, color grade, warm sun color

        RenderSettings.skybox = mat;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        Light sun = FindSun();
        if (sun != null)
        {
            Undo.RecordObject(sun.transform, "Sunset skybox");
            sun.transform.rotation = new Quaternion(0.03160753f, -0.9211548f, 0.20814508f, 0.32733855f);
        }
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: sunset skybox applied. If the sun is not in front of your player, rotate the skybox with the Rotation slider on MegaSun.mat and turn the Directional Light's Y by the same amount.");
    }

    // Thicker haze + lusher, less dry look: greener grass blades, mossy rocks, softer yellow grade.
    [MenuItem("Tools/Mountain Environment/Lush retexture (no haze)")]
    public static void LushHaze()
    {
        // haze stays as it was (not thickened); a touch less sun so the ground isn't blown out yellow
        RenderSettings.fog = false;   // haze removed
        Light sun = FindSun();
        if (sun != null) { Undo.RecordObject(sun, "Lush haze"); sun.intensity = 1.15f; sun.color = new Color(1f, 0.88f, 0.68f); }

        // colour grade: less yellow filter, more saturation
        var volGo = GameObject.Find("Golden Haze Volume");
        var vol = volGo != null ? volGo.GetComponent<Volume>() : null;
        if (vol != null && vol.sharedProfile != null && vol.sharedProfile.TryGet(out ColorAdjustments ca))
        {
            ca.colorFilter.Override(new Color(0.96f, 1f, 0.90f));
            ca.saturation.Override(25f);
            ca.contrast.Override(10f);
            EditorUtility.SetDirty(vol.sharedProfile);
        }

        // grass blades: remove the dry yellow-brown colour and make them taller + denser
        var rootGo = GameObject.Find(RootName);
        Terrain terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain != null && terrain.terrainData != null)
        {
            var td = terrain.terrainData;
            var protos = td.detailPrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                protos[i].healthyColor = new Color(0.52f, 0.72f, 0.22f);
                protos[i].dryColor = new Color(0.42f, 0.60f, 0.18f);
                protos[i].noiseSpread = 6f;
                protos[i].minHeight = Mathf.Max(protos[i].minHeight, 0.8f);
                protos[i].maxHeight = Mathf.Max(protos[i].maxHeight, 1.6f);
                protos[i].minWidth = Mathf.Max(protos[i].minWidth, 0.9f);
                protos[i].maxWidth = Mathf.Max(protos[i].maxWidth, 1.5f);
            }
            td.detailPrototypes = protos;
            terrain.detailObjectDensity = 1f;
            EditorUtility.SetDirty(td);
        }
        else Debug.LogWarning("Mountain Environment: terrain not found, grass not retextured.");

        // rocks: lighten and add a mossy green-grey tint (per-renderer, original materials untouched)
        int tinted = 0;
        var rocks = rootGo != null ? rootGo.transform.Find("Rocks") : null;
        var wall = rootGo != null ? rootGo.transform.Find("Stone Wall") : null;
        foreach (var grp in new[] { rocks, wall })
        {
            if (grp == null) continue;
            foreach (var r in grp.GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial == null) continue;
                if (r.sharedMaterial.name.StartsWith("Mtn")) { r.SetPropertyBlock(null); continue; }
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                Color moss = new Color(1.6f, 1.9f, 1.4f, 1f);
                if (r.sharedMaterial.HasProperty("_BaseColor")) { mpb.SetColor("_BaseColor", moss); tinted++; }
                else if (r.sharedMaterial.HasProperty("_Color")) { mpb.SetColor("_Color", moss); tinted++; }
                r.SetPropertyBlock(mpb);
            }
        }

        TexturizeProps();
        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: lush + thick haze applied (" + tinted + " rock renderers tinted).");
    }

    // Gives the sandbags (burlap weave) and bamboo (fibres + nodes) and wood real textures instead of flat colour.
    static void TexturizeProps()
    {
        EnsureFolder(AssetDir + "/Textures");
        ApplyTex("MtnSandbag", 128, (x, y) =>
        {
            float weave = ((x / 4 + y / 4) % 2 == 0) ? 0.88f : 1.0f;
            float n = 0.88f + 0.12f * Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
            float line = (x % 4 == 0 || y % 4 == 0) ? 0.82f : 1f;
            float v = weave * n * line;
            return new Color(0.78f * v, 0.68f * v, 0.46f * v, 1f);
        }, new Vector2(2f, 2f));
        ApplyTex("MtnBamboo", 128, (x, y) =>
        {
            float fibre = 0.86f + 0.14f * Mathf.PerlinNoise(x * 0.9f, y * 0.04f);
            float node = (y % 64 < 4) ? 0.55f : 1f;
            float v = fibre * node;
            return new Color(0.60f * v, 0.55f * v, 0.28f * v, 1f);
        }, new Vector2(1f, 3f));
        ApplyTex("MtnWood", 128, (x, y) =>
        {
            float grain = 0.8f + 0.2f * Mathf.PerlinNoise(x * 0.8f, y * 0.05f);
            return new Color(0.50f * grain, 0.40f * grain, 0.27f * grain, 1f);
        }, new Vector2(1f, 2f));
        ApplyTex("MtnWeave", 128, (x, y) =>
        {
            float w = ((x / 4 + y / 4) % 2 == 0) ? 0.85f : 1f;
            return new Color(0.68f * w, 0.55f * w, 0.35f * w, 1f);
        }, new Vector2(3f, 3f));
    }

    static void ApplyTex(string matName, int res, System.Func<int, int, Color> f, Vector2 tiling)
    {
        string mp = null;
        foreach (var g in AssetDatabase.FindAssets(matName + " t:Material", new[] { AssetDir }))
        {
            string pth = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(pth) == matName) { mp = pth; break; }
        }
        if (mp == null) return;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, true);
        for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) tex.SetPixel(x, y, f(x, y));
        tex.wrapMode = TextureWrapMode.Repeat; tex.Apply();
        string tp = AssetDir + "/Textures/" + matName + ".png";
        System.IO.File.WriteAllBytes(tp, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(tp);
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
        if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", t); mat.SetTextureScale("_BaseMap", tiling); }
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
    }

    // ---------------------------------------------------------------- level 2 foliage (bushes + small trees) and a test player
    const string BushDir = "Assets/YughuesFreeBushes2018/Prefabs/";
    const string SmallTreePath = "Assets/Fantasy Forest Environment Free Sample/Meshes/Prefabs/tree_1.prefab";
    const string PlayerGuid = "f163ce1a402ea0f468009575669d0b79";   // Entities/Pinoy/Player.prefab, the same player Level 1 uses (scale 1, 1.7 m tall)

    static bool BindTerrain()
    {
        var rootGo = GameObject.Find(RootName);
        terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Mountain Environment: build the terrain first."); return false; }
        originX = terrain.transform.position.x + Size / 2f;
        rng = new System.Random(41);
        WX = PathX(30f) - 7f; WZ = 30f; CX = PathX(14f) - 13f; CZ = 14f;
        return true;
    }

    [MenuItem("Tools/Mountain Environment/Add bushes + small trees + test player")]
    public static void AddFoliageAndPlayer()
    {
        if (!BindTerrain()) return;
        var root = GameObject.Find(RootName).transform;

        foreach (var n in new[] { "Foliage", "Sample Player" })
        {
            var old = root.Find(n);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        // ---- foliage
        var bushes = LoadPrefabs(BushDir, nm => nm.StartsWith("P_Bush"));
        var tree = AssetDatabase.LoadAssetAtPath<GameObject>(SmallTreePath);
        var fol = Group("Foliage", root);
        Undo.RegisterCreatedObjectUndo(fol.gameObject, "Add foliage");
        int nb = 0, nt = 0, tries = 0;
        if (bushes.Count == 0) Debug.LogWarning("Mountain Environment: no bush prefabs found in " + BushDir);
        else
            while (nb < 170 && tries < 12000)
            {
                tries++;
                float x = (R() - 0.5f) * 300f, z = R(-150f, 120f);
                if (Mathf.Abs(x - PathX(z)) < 3.5f) continue;
                if (Dist(x, z, CX, CZ) < 9f || Dist(x, z, WX, WZ) < 8f) continue;
                if (Steep(x, z) > 36f) continue;
                if (Fbm(x * 0.06f + 9f, z * 0.06f, 2) < 0.45f && R() > 0.15f) continue;
                SpawnSized(bushes[rng.Next(bushes.Count)], P(x, z), Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.8f), fol);
                nb++;
            }
        tries = 0;
        if (tree == null) Debug.LogWarning("Mountain Environment: small tree prefab not found at " + SmallTreePath);
        else
            while (nt < 40 && tries < 12000)
            {
                tries++;
                float x = (R() - 0.5f) * 300f, z = R(-150f, 120f);
                if (Mathf.Abs(x - PathX(z)) < 6f) continue;
                if (Dist(x, z, CX, CZ) < 11f || Dist(x, z, WX, WZ) < 10f) continue;
                if (Steep(x, z) > 32f) continue;
                if (Fbm(x * 0.05f + 3f, z * 0.05f, 2) < 0.5f && R() > 0.1f) continue;
                SpawnSized(tree, P(x, z), Quaternion.Euler(0f, R(0f, 360f), 0f), R(3.5f, 5.5f), fol);
                nt++;
            }

        ConvertToURP(fol);

        // ---- test player: the exact Player prefab used in Level 1, unscaled, standing on the trail facing the wall
        string pp = AssetDatabase.GUIDToAssetPath(PlayerGuid);
        var playerPrefab = string.IsNullOrEmpty(pp) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(pp);
        if (playerPrefab != null)
        {
            float pz = WZ - 22f;
            var pos = P(PathX(pz), pz) + Vector3.up * 0.1f;
            var pl = PrefabUtility.InstantiatePrefab(playerPrefab, root) as GameObject;
            pl.name = "Sample Player";
            pl.transform.position = pos;
            pl.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            pl.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(pl, "Add sample player");

            // only one camera / audio listener should be live, so park the old scene player and camera
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if ((go.name == "Pinoy" || go.name == "Main Camera") && go.activeSelf)
                {
                    Undo.RecordObject(go, "Park old player");
                    go.SetActive(false);
                }
        }
        else Debug.LogWarning("Mountain Environment: Player prefab not found.");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: added " + nb + " bushes, " + nt + " small trees and a Sample Player (Level 1 size). Press Play to walk around. Old Pinoy/Main Camera are switched off; tick them on again and delete 'Sample Player' when done.");
    }

    // Bush/tree packs use built-in shaders that render magenta in URP. Make URP Lit copies (alpha-clipped for leaves).
    static void ConvertToURP(Transform grp)
    {
        var urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null) return;
        EnsureFolder(AssetDir + "/Materials");
        var map = new Dictionary<Material, Material>();
        foreach (var r in grp.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name.StartsWith("Universal Render Pipeline")) continue;
                Material nm;
                if (!map.TryGetValue(m, out nm))
                {
                    string path = AssetDir + "/Materials/URP_" + m.name.Replace(" (Instance)", "") + ".mat";
                    nm = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (nm == null)
                    {
                        nm = new Material(urp);
                        Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : (m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null);
                        if (tex != null) nm.SetTexture("_BaseMap", tex);
                        Color c = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                        nm.SetColor("_BaseColor", c);
                        if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) { nm.SetTexture("_BumpMap", m.GetTexture("_BumpMap")); nm.EnableKeyword("_NORMALMAP"); }
                        string ln = (m.name + " " + m.shader.name).ToLowerInvariant();
                        bool leaf = ln.Contains("leaf") || ln.Contains("leaves") || ln.Contains("bush") || ln.Contains("grass") || ln.Contains("cutout") || ln.Contains("transparent") || ln.Contains("foliage") || m.HasProperty("_Cutoff");
                        if (leaf)
                        {
                            nm.SetFloat("_AlphaClip", 1f); nm.SetFloat("_Cutoff", 0.4f);
                            nm.EnableKeyword("_ALPHATEST_ON");
                            nm.SetFloat("_Cull", 0f);
                            nm.renderQueue = 2450;
                        }
                        nm.SetFloat("_Smoothness", 0.05f);
                        AssetDatabase.CreateAsset(nm, path);
                    }
                    map[m] = nm;
                }
                mats[i] = nm; changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------------------- tidy added trees + fix pink (non-URP) materials
    [MenuItem("Tools/Mountain Environment/Tidy added trees + fix pink materials")]
    public static void TidyAndFix()
    {
        FixPinkMaterials();
        TidyLooseTrees();
    }

    static bool IsPinkShader(Shader sh)
    {
        if (sh == null) return true;
        string n = sh.name;
        if (n == "Hidden/InternalErrorShader" || !sh.isSupported) return true;
        if (n.StartsWith("Universal Render Pipeline") || n.StartsWith("Shader Graphs/")) return false;
        if (n.StartsWith("Skybox") || n.StartsWith("UI/") || n.StartsWith("TextMeshPro") || n.StartsWith("Hidden/") ||
            n.StartsWith("Sprites/") || n.StartsWith("GUI/") || n.Contains("Terrain") || n.StartsWith("Unlit/") ||
            n.StartsWith("SkySeries") || n.StartsWith("Particles/") || n.StartsWith("Legacy Shaders/Particles") ||
            n.StartsWith("Mobile/Particles")) return false;
        // built-in / legacy / custom built-in-RP shaders
        return n.StartsWith("Standard") || n.StartsWith("Legacy Shaders/") || n.StartsWith("Nature/") ||
               n.StartsWith("Mobile/") || n.StartsWith("Custom/") || n.StartsWith("Diffuse") || n.StartsWith("Specular") ||
               n.StartsWith("Bumped") || n.StartsWith("Transparent/") || n.StartsWith("Unlit") == false && !n.Contains("/");
    }

    // Converts every built-in-pipeline material in the project (these are what shows up pink in URP) to URP/Lit in place,
    // keeping its texture, colour and normal map. Leaf/grass/bush materials become alpha-clipped and two-sided.
    [MenuItem("Tools/Mountain Environment/Fix pink materials only")]
    public static void FixPinkMaterials()
    {
        var urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null) { Debug.LogWarning("Mountain Environment: URP/Lit shader not found."); return; }
        int fixedCount = 0;
        var names = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/MountainEnvironment/")) continue;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || !IsPinkShader(m.shader)) continue;

            Texture tex = null; Texture bump = null; Color col = Color.white; bool hadCut = false;
            if (m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
            if (tex == null && m.HasProperty("_BaseMap")) tex = m.GetTexture("_BaseMap");
            if (m.HasProperty("_BumpMap")) bump = m.GetTexture("_BumpMap");
            if (m.HasProperty("_Color")) col = m.GetColor("_Color");
            if (m.HasProperty("_Cutoff")) hadCut = true;
            string ln = (m.name + " " + (m.shader != null ? m.shader.name : "") + " " + path).ToLowerInvariant();
            bool foliage = hadCut || m.IsKeywordEnabled("_ALPHATEST_ON") || ln.Contains("leaf") || ln.Contains("leaves") ||
                           ln.Contains("bush") || ln.Contains("grass") || ln.Contains("branch") || ln.Contains("fern") ||
                           ln.Contains("plant") || ln.Contains("foliage") || ln.Contains("cutout") || ln.Contains("transparent");
            float cutoff = hadCut ? m.GetFloat("_Cutoff") : 0.4f;
            if (cutoff < 0.05f) cutoff = 0.4f;

            Undo.RecordObject(m, "Fix pink material");
            m.shader = urp;
            if (tex != null) { m.SetTexture("_BaseMap", tex); }
            m.SetColor("_BaseColor", col);
            if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.05f);
            m.SetFloat("_Metallic", 0f);
            if (foliage)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.renderQueue = 2450;
            }
            EditorUtility.SetDirty(m);
            fixedCount++;
            names.Add(m.name);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Mountain Environment: converted " + fixedCount + " pink/built-in materials to URP: " + string.Join(", ", names.ToArray()));
    }

    // Takes the loose birch trees at the top of the hierarchy, puts them in a "Birch Trees" group under Mountain Environment,
    // and re-places them neatly: snapped to the ground, off the trail, clear of the camp and wall, evenly spaced in loose clumps.
    [MenuItem("Tools/Mountain Environment/Tidy added trees only")]
    public static void TidyLooseTrees()
    {
        if (!BindTerrain()) return;
        var root = GameObject.Find(RootName).transform;
        var loose = new List<GameObject>();
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            string n = go.name.ToLowerInvariant();
            if (go.transform.parent == null && go != root.gameObject && (n.StartsWith("birch") || n.StartsWith("tree") || n.StartsWith("sapling")))
                loose.Add(go);
        }
        if (loose.Count == 0) { Debug.Log("Mountain Environment: no loose trees found at the top of the hierarchy."); return; }
        loose.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        var grp = root.Find("Birch Trees");
        if (grp == null) { grp = Group("Birch Trees", root); Undo.RegisterCreatedObjectUndo(grp.gameObject, "Tidy trees"); }

        var placed = new List<Vector2>();
        int idx = 0;
        foreach (var t in loose)
        {
            Undo.SetTransformParent(t.transform, grp, "Tidy trees");
            Vector2 best = Vector2.zero; bool found = false;
            for (int tries = 0; tries < 4000 && !found; tries++)
            {
                float x = (R() - 0.5f) * 280f, z = R(-125f, 105f);
                if (Mathf.Abs(x - PathX(z)) < 8f) continue;
                if (Dist(x, z, CX, CZ) < 15f || Dist(x, z, WX, WZ) < 13f) continue;
                if (Steep(x, z) > 28f) continue;
                if (Fbm(x * 0.04f + 5f, z * 0.04f, 2) < 0.48f && R() > 0.2f) continue;   // loose clumps
                bool ok = true;
                foreach (var q in placed) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < 11f * 11f) { ok = false; break; }
                if (!ok) continue;
                best = new Vector2(x, z); found = true;
            }
            if (!found) { best = new Vector2((R() - 0.5f) * 240f, R(-110f, 90f)); }
            placed.Add(best);
            Undo.RecordObject(t.transform, "Tidy trees");
            t.transform.position = P(best.x, best.y);
            t.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            float baseScale = Mathf.Max(0.3f, t.transform.localScale.x);
            t.transform.localScale = Vector3.one * Mathf.Clamp(baseScale * R(0.9f, 1.15f), 0.3f, 0.8f);
            t.name = (t.name.IndexOf(" (") > 0 ? t.name.Substring(0, t.name.IndexOf(" (")) : t.name) + " " + (++idx).ToString("00");
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: tidied " + loose.Count + " trees into 'Birch Trees' and placed them on the terrain.");
    }

    // ---------------------------------------------------------------- big bushes, brown-trunk trees, outer boulders
    static Material BrownCloneOf(Material src)
    {
        string outMat = AssetDir + "/Materials/Brown_" + src.name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(outMat);
        if (existing != null) return existing;

        Texture baseTex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : (src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null);
        if (baseTex == null) return src;

        // recolour at 2048 px: bark (non-green, mostly white) -> brown; green leaves untouched
        int res = 2048;
        var rt = RenderTexture.GetTemporary(res, res, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(baseTex, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0); tex.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels32();
        Color dark = new Color(0.14f, 0.09f, 0.06f), mid = new Color(0.40f, 0.27f, 0.17f), light = new Color(0.58f, 0.42f, 0.28f);
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.a < 8) continue;
            bool leaf = c.g > c.b + 4 && c.g >= c.r - 6;
            if (leaf) continue;
            float lum = (c.r * 0.3f + c.g * 0.55f + c.b * 0.15f) / 255f;
            Color b = lum < 0.5f ? Color.Lerp(dark, mid, lum * 2f) : Color.Lerp(mid, light, (lum - 0.5f) * 2f);
            px[i] = new Color32((byte)(b.r * 255f), (byte)(b.g * 255f), (byte)(b.b * 255f), c.a);
        }
        tex.SetPixels32(px); tex.Apply();
        EnsureFolder(AssetDir + "/Textures");
        string tp = AssetDir + "/Textures/Brown_" + src.name + ".png";
        System.IO.File.WriteAllBytes(tp, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(tp);
        var imp = AssetImporter.GetAtPath(tp) as TextureImporter;
        if (imp != null) { imp.maxTextureSize = 2048; imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.SaveAndReimport(); }
        var nt = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);

        var nm = new Material(src);
        foreach (var prop in new[] { "_BaseMap", "_ColorMap", "_MainTex" })
            if (nm.HasProperty(prop)) nm.SetTexture(prop, nt);
        EnsureFolder(AssetDir + "/Materials");
        AssetDatabase.CreateAsset(nm, outMat);
        return nm;
    }

    static void MakeBrown(GameObject go, Dictionary<Material, Material> cache)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            bool ch = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.name.StartsWith("Brown_")) continue;
                Material b;
                if (!cache.TryGetValue(m, out b)) { b = BrownCloneOf(m); cache[m] = b; }
                if (b != m) { mats[i] = b; ch = true; }
            }
            if (ch) r.sharedMaterials = mats;
        }
    }

    static void MossTint(Transform grp)
    {
        foreach (var r in grp.GetComponentsInChildren<Renderer>())
        {
            if (r.sharedMaterial == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            Color moss = new Color(1.5f, 1.8f, 1.35f, 1f);
            if (r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", moss);
            else if (r.sharedMaterial.HasProperty("_Color")) mpb.SetColor("_Color", moss);
            r.SetPropertyBlock(mpb);
        }
    }

    [MenuItem("Tools/Mountain Environment/Add big bushes + brown trees + outer boulders")]
    public static void AddScenery()
    {
        if (!BindTerrain()) return;
        var root = GameObject.Find(RootName).transform;
        rng = new System.Random(77);
        foreach (var n in new[] { "Big Bushes", "Outer Boulders" })
        {
            var old = root.Find(n);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        // ---- trees: recolour existing birches to brown trunks and add more of the same trees
        var cache = new Dictionary<Material, Material>();
        var treeGrp = root.Find("Trees");
        if (treeGrp == null) treeGrp = root.Find("Birch Trees");
        if (treeGrp == null) { treeGrp = Group("Trees", root); Undo.RegisterCreatedObjectUndo(treeGrp.gameObject, "Add trees"); }
        treeGrp.name = "Trees";
        var occupied = new List<Vector2>();
        foreach (Transform t in treeGrp) { MakeBrown(t.gameObject, cache); occupied.Add(new Vector2(t.position.x - originX, t.position.z)); }

        var treePrefabs = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("birch t:Prefab"))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null && go.name.StartsWith("birch_")) treePrefabs.Add(go);
        }
        int addedTrees = 0, tries = 0;
        while (treePrefabs.Count > 0 && addedTrees < 35 && tries < 15000)
        {
            tries++;
            float x = (R() - 0.5f) * 290f, z = R(-130f, 110f);
            if (Mathf.Abs(x - PathX(z)) < 8f) continue;
            if (Dist(x, z, CX, CZ) < 15f || Dist(x, z, WX, WZ) < 13f) continue;
            if (Steep(x, z) > 28f) continue;
            bool ok = true;
            foreach (var q in occupied) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < 8f * 8f) { ok = false; break; }
            if (!ok) continue;
            var pf = treePrefabs[rng.Next(treePrefabs.Count)];
            var inst = PrefabUtility.InstantiatePrefab(pf, treeGrp) as GameObject;
            Undo.RegisterCreatedObjectUndo(inst, "Add trees");
            inst.transform.position = P(x, z);
            inst.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            inst.transform.localScale = Vector3.one * R(0.42f, 0.62f);
            inst.name = "Tree " + (treeGrp.childCount).ToString("00");
            MakeBrown(inst, cache);
            occupied.Add(new Vector2(x, z));
            addedTrees++;
        }

        // ---- big bushes, easy to see
        var bushes = LoadPrefabs(BushDir, nm => nm.StartsWith("P_Bush"));
        var bg = Group("Big Bushes", root);
        Undo.RegisterCreatedObjectUndo(bg.gameObject, "Add bushes");
        int nb = 0; tries = 0;
        while (bushes.Count > 0 && nb < 70 && tries < 15000)
        {
            tries++;
            float x = (R() - 0.5f) * 290f, z = R(-130f, 110f);
            float dTrail = Mathf.Abs(x - PathX(z));
            if (dTrail < 4.5f) continue;
            if (nb < 45 && dTrail > 32f) continue;               // most of them within sight of the trail
            if (Dist(x, z, CX, CZ) < 9f || Dist(x, z, WX, WZ) < 8f) continue;
            if (Steep(x, z) > 34f) continue;
            SpawnSized(bushes[rng.Next(bushes.Count)], P(x, z), Quaternion.Euler(0f, R(0f, 360f), 0f), R(2.6f, 4.6f), bg);
            nb++;
        }
        ConvertToURP(bg);

        // ---- big boulders well outside the trail
        var singles = LoadPrefabs(RockDir, nm => !nm.Contains("LOD") && !nm.Contains("grup"));
        var rg = Group("Outer Boulders", root);
        Undo.RegisterCreatedObjectUndo(rg.gameObject, "Add boulders");
        int nr = 0; tries = 0;
        var placedRocks = new List<Vector2>();
        while (singles.Count > 0 && nr < 55 && tries < 20000)
        {
            tries++;
            float x = (R() - 0.5f) * 300f, z = R(-135f, 115f);
            if (Mathf.Abs(x - PathX(z)) < 18f) continue;          // not near the trail
            if (Dist(x, z, CX, CZ) < 22f || Dist(x, z, WX, WZ) < 20f) continue;
            if (Steep(x, z) > 45f) continue;
            bool ok = true;
            foreach (var q in placedRocks) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < 14f * 14f) { ok = false; break; }
            if (!ok) continue;
            SpawnSized(singles[rng.Next(singles.Count)], P(x, z), Quaternion.Euler(R(-4f, 4f), R(0f, 360f), R(-4f, 4f)), R(4.5f, 9f), rg);
            placedRocks.Add(new Vector2(x, z));
            nr++;
        }
        MossTint(rg);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: brown trunks applied, +" + addedTrees + " trees, " + nb + " big bushes, " + nr + " outer boulders.");
    }

    // ---------------------------------------------------------------- big bushes, tropical (brown-bark) trees, big boulders
    static Dictionary<string, Material> tropicalMats;

    static Material TropicalMaterialFor(Material orig)
    {
        if (orig == null) return null;
        if (tropicalMats == null) tropicalMats = new Dictionary<string, Material>();
        Material res;
        if (tropicalMats.TryGetValue(orig.name, out res) && res != null) return res;
        string origPath = AssetDatabase.GetAssetPath(orig);
        if (string.IsNullOrEmpty(origPath)) return orig;
        EnsureFolder(AssetDir + "/Materials");
        string newPath = AssetDir + "/Materials/Tropical_" + orig.name + ".mat";
        var nm = AssetDatabase.LoadAssetAtPath<Material>(newPath);
        if (nm == null) { AssetDatabase.CopyAsset(origPath, newPath); nm = AssetDatabase.LoadAssetAtPath<Material>(newPath); }
        if (nm == null) return orig;
        bool bill = orig.name.Contains("Billboard");
        string type = orig.name.Replace("_Billboard_Mat", "").Replace("_Mat", "");
        string texPath = AssetDir + "/TropicalTrees/Tropical_" + type + (bill ? "_Billboard_Color" : "_Color") + ".png";
        AssetDatabase.ImportAsset(texPath);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex != null)
        {
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_ColorMap", "_BaseColorMap" })
                if (nm.HasProperty(prop)) nm.SetTexture(prop, tex);
        }
        else Debug.LogWarning("Mountain Environment: recoloured tree texture missing: " + texPath);
        EditorUtility.SetDirty(nm);
        tropicalMats[orig.name] = nm;
        return nm;
    }

    static void MakeTropical(GameObject tree)
    {
        foreach (var r in tree.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            bool ch = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].name.StartsWith("Tropical_") || !mats[i].name.StartsWith("birch")) continue;
                var nm = TropicalMaterialFor(mats[i]);
                if (nm != null && nm != mats[i]) { mats[i] = nm; ch = true; }
            }
            if (ch) r.sharedMaterials = mats;
        }
    }

    [MenuItem("Tools/Mountain Environment/Add big bushes + brown trees + big boulders")]
    public static void AddBigNature()
    {
        if (!BindTerrain()) return;
        rng = new System.Random(System.Environment.TickCount);
        tropicalMats = null;
        var root = GameObject.Find(RootName).transform;

        // ---- existing trees: no birches in the Philippines, so recolour the bark brown and rename
        var treeGrp = root.Find("Birch Trees");
        if (treeGrp != null) { Undo.RecordObject(treeGrp.gameObject, "Rename trees"); treeGrp.name = "Trees"; }
        treeGrp = root.Find("Trees");
        var existing = new List<Vector2>();
        if (treeGrp != null)
        {
            var added = treeGrp.Find("Added");
            if (added != null) Undo.DestroyObjectImmediate(added.gameObject);
            int n = 0;
            foreach (Transform t in treeGrp)
            {
                MakeTropical(t.gameObject);
                string nm = t.name.ToLowerInvariant();
                string kind = nm.Contains("big") ? "Tree Tall" : nm.Contains("medium") ? "Tree Medium" : nm.Contains("small") ? "Tree Small" : "Tree";
                t.name = kind + " " + (++n).ToString("00");
                existing.Add(new Vector2(t.position.x - originX, t.position.z));
            }
        }
        else { treeGrp = Group("Trees", root); Undo.RegisterCreatedObjectUndo(treeGrp.gameObject, "Add trees"); }

        // ---- more trees (same birch models, brown bark)
        var addedGrp = Group("Added", treeGrp);
        Undo.RegisterCreatedObjectUndo(addedGrp.gameObject, "Add trees");
        var treePrefabs = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab birch", new[] { "Assets/R3DWorks" }))
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (pf != null && pf.name.StartsWith("birch")) treePrefabs.Add(pf);
        }
        int nt = 0, tries = 0;
        while (treePrefabs.Count > 0 && nt < 45 && tries < 15000)
        {
            tries++;
            float x = (R() - 0.5f) * 290f, z = R(-135f, 115f);
            if (Mathf.Abs(x - PathX(z)) < 9f) continue;
            if (Dist(x, z, CX, CZ) < 15f || Dist(x, z, WX, WZ) < 13f) continue;
            if (Steep(x, z) > 30f) continue;
            bool ok = true;
            foreach (var q in existing) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < 10f * 10f) { ok = false; break; }
            if (!ok) continue;
            existing.Add(new Vector2(x, z));
            var pf = treePrefabs[rng.Next(treePrefabs.Count)];
            var g = PrefabUtility.InstantiatePrefab(pf, addedGrp) as GameObject;
            g.transform.position = P(x, z);
            g.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            g.transform.localScale = Vector3.one * R(0.42f, 0.7f);
            MakeTropical(g);
            string nm = pf.name.ToLowerInvariant();
            g.name = (nm.Contains("big") ? "Tree Tall " : nm.Contains("medium") ? "Tree Medium " : "Tree Small ") + (++nt).ToString("00");
            Undo.RegisterCreatedObjectUndo(g, "Add trees");
        }

        // ---- bigger bushes, clustered near the trail edges so they read from the player's view
        var fol = root.Find("Foliage");
        if (fol == null) { fol = Group("Foliage", root); Undo.RegisterCreatedObjectUndo(fol.gameObject, "Add bushes"); }
        var oldBig = fol.Find("Big Bushes");
        if (oldBig != null) Undo.DestroyObjectImmediate(oldBig.gameObject);
        var bigGrp = Group("Big Bushes", fol);
        Undo.RegisterCreatedObjectUndo(bigGrp.gameObject, "Add bushes");
        var bushes = LoadPrefabs(BushDir, nm => nm.StartsWith("P_Bush"));
        int nb = 0; tries = 0;
        while (bushes.Count > 0 && nb < 90 && tries < 20000)
        {
            tries++;
            float z = R(-130f, 105f);
            float off = R(4.5f, 28f) * (R() > 0.5f ? 1f : -1f);
            float x = PathX(z) + off;
            if (Mathf.Abs(x) > 150f) continue;
            if (Dist(x, z, CX, CZ) < 10f || Dist(x, z, WX, WZ) < 9f) continue;
            if (Steep(x, z) > 34f) continue;
            if (Fbm(x * 0.07f + 2f, z * 0.07f, 2) < 0.42f && R() > 0.25f) continue;
            var g = SpawnSized(bushes[rng.Next(bushes.Count)], P(x, z), Quaternion.Euler(0f, R(0f, 360f), 0f), R(2.6f, 4.6f), bigGrp);
            if (g != null) { g.name = "Big Bush " + (++nb).ToString("00"); Undo.RegisterCreatedObjectUndo(g, "Add bushes"); }
        }
        ConvertToURP(bigGrp);

        // ---- big boulders, well away from the trail
        var rocksRoot = root.Find("Rocks");
        if (rocksRoot == null) rocksRoot = Group("Rocks", root);
        var oldB = rocksRoot.Find("Big Boulders");
        if (oldB != null) Undo.DestroyObjectImmediate(oldB.gameObject);
        var boulderGrp = Group("Big Boulders", rocksRoot);
        Undo.RegisterCreatedObjectUndo(boulderGrp.gameObject, "Add boulders");
        var singles = LoadPrefabs(RockDir, nm => !nm.Contains("LOD") && !nm.Contains("grup"));
        var groups = LoadPrefabs(RockDir, nm => nm.Contains("grup") && !nm.Contains("LOD"));
        var rockSrc = new List<GameObject>(singles); rockSrc.AddRange(groups);
        int nr = 0; tries = 0;
        var sites = new List<Vector2>();
        while (rockSrc.Count > 0 && nr < 26 && tries < 20000)
        {
            tries++;
            float z = R(-135f, 115f);
            float off = R(30f, 85f) * (R() > 0.5f ? 1f : -1f);
            float x = PathX(z) + off;
            if (Mathf.Abs(x) > 155f) continue;
            if (Dist(x, z, CX, CZ) < 28f || Dist(x, z, WX, WZ) < 26f) continue;
            if (Steep(x, z) > 42f) continue;
            bool ok = true;
            foreach (var q in sites) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < 22f * 22f) { ok = false; break; }
            if (!ok) continue;
            sites.Add(new Vector2(x, z));
            int cluster = rng.Next(1, 4);
            for (int c = 0; c < cluster; c++)
            {
                float cx = x + R(-4f, 4f), cz = z + R(-4f, 4f);
                float size = c == 0 ? R(6f, 10f) : R(3.5f, 6f);
                var g = SpawnSized(rockSrc[rng.Next(rockSrc.Count)], P(cx, cz), Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f)), size, boulderGrp);
                if (g == null) continue;
                g.name = "Boulder " + (++nr).ToString("00");
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                {
                    if (r.sharedMaterial == null) continue;
                    var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb);
                    Color moss = new Color(1.6f, 1.9f, 1.4f, 1f);
                    if (r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", moss);
                    else if (r.sharedMaterial.HasProperty("_Color")) mpb.SetColor("_Color", moss);
                    r.SetPropertyBlock(mpb);
                }
                Undo.RegisterCreatedObjectUndo(g, "Add boulders");
            }
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: added " + nb + " big bushes, " + nt + " brown-bark trees (existing trees recoloured), " + nr + " boulders.");
    }

    [MenuItem("Tools/Mountain Environment/Remove test player (restore Pinoy)")]
    public static void RemoveTestPlayer()
    {
        var rootGo = GameObject.Find(RootName);
        var sp = rootGo != null ? rootGo.transform.Find("Sample Player") : null;
        if (sp != null) Undo.DestroyObjectImmediate(sp.gameObject);
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if ((go.name == "Pinoy" || go.name == "Main Camera") && !go.activeSelf) go.SetActive(true);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Mountain Environment/Lighting - clear blue day")]
    public static void LightClear()
    {
        ApplyLight(new Color(0.55f, 0.71f, 0.82f), 0.0042f, new Color(1f, 0.96f, 0.86f), 1.6f, new Vector3(48f, -35f, 0f));
    }

    [MenuItem("Tools/Mountain Environment/Lighting - film golden haze")]
    public static void LightHaze()
    {
        ApplyLight(new Color(0.85f, 0.80f, 0.61f), 0.0085f, new Color(1f, 0.87f, 0.62f), 1.5f, new Vector3(28f, -30f, 0f));
    }

    static void ApplyLight(Color fog, float density, Color sunColor, float intensity, Vector3 euler)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fog;
        RenderSettings.fogDensity = density;
        Light sun = RenderSettings.sun;
        if (sun == null)
            foreach (var l in Object.FindObjectsByType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null)
        {
            Undo.RecordObject(sun, "Mountain lighting");
            Undo.RecordObject(sun.transform, "Mountain lighting");
            sun.color = sunColor;
            sun.intensity = intensity;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(euler);
        }
        else Debug.LogWarning("Mountain Environment: no directional light found; only fog was changed.");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // ---------------------------------------------------------------- parts
    static float Dist(float x, float z, float cx, float cz)
    {
        return Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
    }

    static TerrainLayer MakeLayer(string name, Color c, float tile)
    {
        EnsureFolder(AssetDir + "/Layers");
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float n = 0.82f + 0.3f * Fbm(x * 0.18f, y * 0.18f, 3) + 0.08f * ((x * 7 + y * 13) % 5 / 4f);
                tex.SetPixel(x, y, new Color(Mathf.Clamp01(c.r * n), Mathf.Clamp01(c.g * n), Mathf.Clamp01(c.b * n), 1f));
            }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        AssetDatabase.CreateAsset(tex, AssetDir + "/Layers/" + name + "_tex.asset");
        var layer = new TerrainLayer();
        layer.diffuseTexture = tex;
        layer.tileSize = new Vector2(tile * 3f, tile * 3f);
        AssetDatabase.CreateAsset(layer, AssetDir + "/Layers/" + name + ".terrainlayer");
        return layer;
    }

    static void Cliff(Transform parent, List<GameObject> groups, List<GameObject> singles, float cx, float cz, int n, float spread, float sMin, float sMax)
    {
        for (int i = 0; i < n; i++)
        {
            float a = R(0f, 6.283f), r = R(0f, spread);
            float x = cx + Mathf.Cos(a) * r, z = cz + Mathf.Sin(a) * r;
            var list = (i % 2 == 0) ? groups : singles;
            float s = R(sMin, sMax) * RockScale;
            SpawnSized(list[rng.Next(list.Count)], P(x, z),
                Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f)), s * 2.5f, parent);
        }
    }

    static void Stake(Transform parent, Vector3 pos, float h, Material bamboo, Material top)
    {
        var g = new GameObject("Stake");
        g.transform.SetParent(parent, false);
        g.transform.position = pos;
        Prim(PrimitiveType.Cylinder, "Pole", g.transform, new Vector3(0, h / 2f, 0), new Vector3(0.22f, h / 2f, 0.22f), bamboo);
        Prim(PrimitiveType.Cylinder, "Top", g.transform, new Vector3(0, h, 0), new Vector3(0.24f, 0.17f, 0.24f), top);
    }

    static void Fence(Transform parent, Vector2 a, Vector2 b, int n, Material wood)
    {
        var pts = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float x = Mathf.Lerp(a.x, b.x, i / (float)n), z = Mathf.Lerp(a.y, b.y, i / (float)n);
            pts[i] = P(x, z);
            Prim(PrimitiveType.Cube, "Post", parent, Vector3.zero, new Vector3(0.17f, 1.6f, 0.17f), wood)
                .transform.position = pts[i] + Vector3.up * 0.7f;
        }
        float[] heights = { 0.55f, 1.1f };
        for (int i = 0; i < n; i++)
            foreach (float hg in heights)
            {
                Vector3 p0 = pts[i] + Vector3.up * hg, p1 = pts[i + 1] + Vector3.up * hg;
                var rail = Prim(PrimitiveType.Cube, "Rail", parent, Vector3.zero, new Vector3(0.1f, 0.1f, (p1 - p0).magnitude), wood);
                rail.transform.position = (p0 + p1) * 0.5f;
                rail.transform.rotation = Quaternion.LookRotation((p1 - p0).normalized);
            }
    }

    static void Hut(Transform parent, Vector3 pos, float yaw, float scale, Material thatch, Material skirt, Material weave, Material wood, Material dark)
    {
        var g = new GameObject("Hut");
        g.transform.SetParent(parent, false);
        g.transform.position = pos + Vector3.down * 0.3f;
        g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        g.transform.localScale = Vector3.one * scale;
        var t = g.transform;
        float[,] posts = { { -1.9f, -1.9f }, { 1.9f, -1.9f }, { -1.9f, 1.9f }, { 1.9f, 1.9f }, { 0, -1.9f }, { 0, 1.9f } };
        for (int i = 0; i < 6; i++)
            Prim(PrimitiveType.Cylinder, "Stilt", t, new Vector3(posts[i, 0], 0.75f, posts[i, 1]), new Vector3(0.26f, 0.95f, 0.26f), wood);
        Prim(PrimitiveType.Cube, "Floor", t, new Vector3(0, 1.55f, 0), new Vector3(4.5f, 0.18f, 4.5f), wood);
        Prim(PrimitiveType.Cube, "Walls", t, new Vector3(0, 2.5f, 0), new Vector3(3.8f, 1.7f, 3.8f), weave);
        Prim(PrimitiveType.Cube, "Door", t, new Vector3(0, 2.2f, 1.92f), new Vector3(0.95f, 1.3f, 0.06f), dark);
        Prim(PrimitiveType.Cube, "Window", t, new Vector3(1.92f, 2.7f, 0), new Vector3(0.06f, 0.7f, 0.8f), dark);
        Prim(PrimitiveType.Cube, "Window", t, new Vector3(-1.92f, 2.7f, 0), new Vector3(0.06f, 0.7f, 0.8f), dark);
        var sk = new GameObject("RoofSkirt"); sk.transform.SetParent(t, false); sk.transform.localPosition = new Vector3(0, 2.9f, 0);
        sk.AddComponent<MeshFilter>().sharedMesh = PyramidMesh("PyramidSkirt", 2.76f, 1.3f);
        sk.AddComponent<MeshRenderer>().sharedMaterial = skirt;
        var rf = new GameObject("Roof"); rf.transform.SetParent(t, false); rf.transform.localPosition = new Vector3(0, 3.4f, 0);
        rf.AddComponent<MeshFilter>().sharedMesh = PyramidMesh("PyramidRoof", 2.55f, 2.9f);
        rf.AddComponent<MeshRenderer>().sharedMaterial = thatch;
        for (int s = -1; s <= 1; s += 2)
        {
            var rl = Prim(PrimitiveType.Cube, "LadderRail", t, new Vector3(s * 0.4f, 0.7f, 2.7f), new Vector3(0.07f, 1.9f, 0.07f), wood);
            rl.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
        }
        for (int i = 0; i < 5; i++)
            Prim(PrimitiveType.Cube, "LadderRung", t, new Vector3(0, 0.25f + i * 0.3f, 3.05f - i * 0.14f), new Vector3(0.8f, 0.05f, 0.06f), wood);
    }
}
#endif
