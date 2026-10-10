using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// Adds bushes / trees / rocks ONLY in the empty gaps. Nothing existing is moved, changed or deleted.
public static class MtnFill
{
    static float PathX(float z) { return 12f * Mathf.Sin(z * 0.045f) + 5f * Mathf.Sin(z * 0.11f + 1f) - z * 0.1f; }

    [MenuItem("Tools/Mountain Environment/Fill empty areas (adds only)")]
    public static void Run()
    {
        var rootGo = GameObject.Find("Mountain Environment");
        var terrain = rootGo != null ? rootGo.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogWarning("Fill: terrain not found"); return; }
        var root = rootGo.transform;
        var td = terrain.terrainData;
        Vector3 tp = terrain.transform.position;
        float S = td.size.x, half = S / 2f - 14f;
        float cx0 = tp.x + S / 2f, cz0 = tp.z + S / 2f; // world centre
        var rng = new System.Random(System.Environment.TickCount);
        System.Func<float, float, float> R = (a, b) => a + (b - a) * (float)rng.NextDouble();

        // templates = copies of what is already in the scene
        System.Func<string, string, List<Transform>> kids = (g, sub) =>
        {
            var l = new List<Transform>();
            Transform t = root.Find(g); if (t != null && sub != null) t = t.Find(sub);
            if (t != null) foreach (Transform c in t) if (c.GetComponentInChildren<Renderer>() != null) l.Add(c);
            return l;
        };
        var bushes = kids("Foliage", "Bushes");
        var bigBushes = kids("Foliage", "Big Bushes");
        var trees = kids("Trees", null);
        var treesAdded = kids("Trees", "Added"); trees.AddRange(treesAdded);
        var boulders = kids("Rocks", "Big Boulders");
        var little = kids("Rocks", "Little Rocks");

        // occupancy hash of everything that is already there
        const float H = 16f;
        var hash = new Dictionary<Vector2Int, List<Vector2>>();
        System.Action<Vector2> add = p =>
        {
            var k = new Vector2Int(Mathf.FloorToInt(p.x / H), Mathf.FloorToInt(p.y / H));
            List<Vector2> l; if (!hash.TryGetValue(k, out l)) { l = new List<Vector2>(); hash[k] = l; }
            l.Add(p);
        };
        foreach (var r in root.GetComponentsInChildren<Renderer>()) add(new Vector2(r.bounds.center.x, r.bounds.center.z));
        System.Func<Vector2, float> nearest = p =>
        {
            float best = 1e9f; int kx = Mathf.FloorToInt(p.x / H), kz = Mathf.FloorToInt(p.y / H);
            for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++)
            {
                List<Vector2> l; if (!hash.TryGetValue(new Vector2Int(kx + i, kz + j), out l)) continue;
                foreach (var q in l) best = Mathf.Min(best, (q - p).magnitude);
            }
            return best;
        };

        float WX = PathX(30f) - 7f, WZ = 30f, CX = PathX(14f) - 13f, CZ = 14f;
        var fill = root.Find("Fill-in"); if (fill == null) { fill = new GameObject("Fill-in").transform; fill.SetParent(root, false); }
        Transform gB = Sub(fill, "Bushes"), gBB = Sub(fill, "Big Bushes"), gT = Sub(fill, "Trees"), gR = Sub(fill, "Rocks");

        // random order of candidate points, so gaps fill naturally
        var cand = new List<Vector2>();
        for (float x = -half; x < half; x += 5f) for (float z = -half; z < half; z += 5f) cand.Add(new Vector2(x + R(0, 5), z + R(0, 5)));
        for (int i = cand.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = cand[i]; cand[i] = cand[j]; cand[j] = t; }

        const float Gap = 15f;
        int nb = 0, nbb = 0, nt = 0, nr = 0;
        foreach (var c in cand)
        {
            if (nb + nbb + nt + nr >= 700) break;
            float lx = c.x, lz = c.y;
            if (Mathf.Abs(lx - PathX(lz)) < 6f) continue;
            if (Vector2.Distance(c, new Vector2(CX, CZ)) < 16f || Vector2.Distance(c, new Vector2(WX, WZ)) < 16f) continue;
            float nx = (lx + S / 2f) / S, nz = (lz + S / 2f) / S;
            if (td.GetSteepness(nx, nz) > 36f) continue;
            var wp2 = new Vector2(cx0 + lx, cz0 + lz);
            if (nearest(wp2) < Gap) continue;

            float roll = R(0, 1);
            List<Transform> src; Transform parent; string tag; float scaleMul;
            if (roll < 0.40f && bushes.Count > 0) { src = bushes; parent = gB; tag = "Bush"; scaleMul = R(0.9f, 1.2f); }
            else if (roll < 0.58f && bigBushes.Count > 0) { src = bigBushes; parent = gBB; tag = "Big Bush"; scaleMul = R(0.9f, 1.15f); }
            else if (roll < 0.82f && trees.Count > 0) { src = trees; parent = gT; tag = "Tree"; scaleMul = R(0.85f, 1.15f); }
            else if (boulders.Count + little.Count > 0)
            {
                bool big = boulders.Count > 0 && (little.Count == 0 || R(0, 1) < 0.45f);
                src = big ? boulders : little; parent = gR; tag = "Rock"; scaleMul = big ? R(0.5f, 0.9f) : R(0.9f, 1.6f);
            }
            else continue;

            var tpl = src[rng.Next(src.Count)];
            var g = Object.Instantiate(tpl.gameObject, parent);
            g.name = tag + " fill";
            Undo.RegisterCreatedObjectUndo(g, "Fill empty areas");
            float wx = cx0 + lx, wz = cz0 + lz;
            float y = terrain.SampleHeight(new Vector3(wx, 0, wz)) + tp.y;
            g.transform.position = new Vector3(wx, y, wz);
            g.transform.rotation = Quaternion.Euler(0f, R(0, 360), 0f);
            g.transform.localScale = tpl.localScale * scaleMul;
            if (tag == "Rock")
            {
                var rs = g.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float low = float.MaxValue;
                    for (int i = 0; i <= 2; i++) for (int j = 0; j <= 2; j++)
                        low = Mathf.Min(low, terrain.SampleHeight(new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 2f), 0, Mathf.Lerp(b.min.z, b.max.z, j / 2f))) + tp.y);
                    g.transform.position += Vector3.up * ((low - 0.1f * b.size.y) - b.min.y);
                }
                nr++;
            }
            else if (tag == "Tree") nt++; else if (tag == "Big Bush") nbb++; else nb++;
            add(wp2);
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Mountain Environment: filled gaps with " + nb + " bushes, " + nbb + " big bushes, " + nt + " trees, " + nr + " rocks (nothing else changed).");
    }

    static Transform Sub(Transform p, string n)
    {
        var t = p.Find(n); if (t != null) return t;
        t = new GameObject(n).transform; t.SetParent(p, false); return t;
    }
}
