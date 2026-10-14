// Tools > Level 3 > Move boss fight from Boss Test into Level 3
// Takes the Goyo boss fight out of "Boss Test.unity" and sets it up in the Level 3 boss arena:
//  1. The barrier walls (Cube...) and capsules (Cylinder...) placed around the arena are made invisible -
//     their renderers are switched off, their colliders stay.
//  2. A NavMesh is baked around the arena from physics colliders (not render meshes), so the invisible
//     barriers still block the boss and his men.
//  3. Player, Goyo Boss, Goyo Reinforcements, the rifle pickup, PauseManager and EventSystem are moved over
//     (their wiring to each other comes with them). The Boss Test layout is kept, centred on the arena's
//     "Boss Arena Center" marker and dropped onto the terrain; the reinforcement spawn points are pulled in
//     to a ring inside the barrier walls. Boss Test.unity itself is left as it was.
#if UNITY_EDITOR
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class Level3BossFight
{
    const string Level3Path = "Assets/Scenes/Level 3.unity";
    const string BossTestPath = "Assets/Scenes/Boss Test.unity";
    const string NavDir = "Assets/Scenes/Level 3";
    static readonly string[] FightKit = { "Player", "Goyo Boss", "Goyo Reinforcements", "Dreyse Needle rifle", "PauseManager", "EventSystem" };
    static readonly string[] Placed = { "Player", "Goyo Boss", "Goyo Reinforcements", "Dreyse Needle rifle" };   // the rest don't care where they are
    const float SpawnRing = 28f;   // reinforcements appear on this ring - the barrier walls stand at ~36 m

    [MenuItem("Tools/Level 3/Move boss fight from Boss Test into Level 3")]
    public static void Move()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Run());
    }

    // Unity.exe -batchmode -projectPath <proj> -executeMethod Level3BossFight.MoveBatch -quit
    public static void MoveBatch() => Debug.Log(Run());

    static string Run()
    {
        var log = new StringBuilder("Level 3 boss fight\n");
        var level3 = EditorSceneManager.OpenScene(Level3Path, OpenSceneMode.Single);
        var roots = level3.GetRootGameObjects();
        var already = roots.Where(r => FightKit.Contains(r.name)).Select(r => r.name).ToArray();
        if (already.Length > 0) return "Level 3 boss fight: Level 3 already has [" + string.Join(", ", already) + "] - stopped, nothing changed";
        var env = roots.FirstOrDefault(r => r.name == "Mountain Environment");
        var terrain = env != null ? env.GetComponentInChildren<Terrain>() : null;
        var mark = env != null ? env.transform.Find("Boss Arena Center") : null;
        if (terrain == null || mark == null) return "Level 3 boss fight: no terrain / Boss Arena Center in Level 3 - stopped";
        Vector3 C = mark.position;
        float ty = terrain.transform.position.y;
        Vector3 OnGround(float x, float z, float lift)
        {
            var p = new Vector3(C.x + x, 0f, C.z + z);
            p.y = terrain.SampleHeight(p) + ty + lift;
            return p;
        }

        // 1. barriers: invisible, still solid
        int walls = 0, capsules = 0;
        foreach (var r in roots)
        {
            bool wall = r.name.StartsWith("Cube"), capsule = r.name.StartsWith("Cylinder");
            if (!wall && !capsule) continue;
            foreach (var mr in r.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;
            if (wall) walls++; else capsules++;
        }
        log.AppendFormat("  barriers: {0} walls + {1} capsules made invisible (colliders kept)\n", walls, capsules);

        // 2. NavMesh over the arena, from colliders so the invisible barriers count
        var navGo = new GameObject("NavMesh Surface");
        navGo.transform.position = C;
        var surface = navGo.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Volume;
        surface.center = Vector3.zero;
        surface.size = new Vector3(110f, 60f, 110f);
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~LayerMask.GetMask("People", "Weapon", "UI", "Ignore Raycast");
        surface.BuildNavMesh();
        if (!AssetDatabase.IsValidFolder(NavDir)) AssetDatabase.CreateFolder("Assets/Scenes", "Level 3");
        string navPath = NavDir + "/NavMesh-Level 3.asset";
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath) != null) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        log.AppendFormat("  NavMesh: {0} triangles over a 110 m square around the arena centre {1}\n", NavMesh.CalculateTriangulation().indices.Length / 3, C);

        // 3. the fight, moved across from Boss Test
        var bossTest = EditorSceneManager.OpenScene(BossTestPath, OpenSceneMode.Additive);
        var kit = bossTest.GetRootGameObjects().Where(g => FightKit.Contains(g.name)).ToList();
        foreach (var g in kit) SceneManager.MoveGameObjectToScene(g, level3);
        EditorSceneManager.CloseScene(bossTest, true);   // not saved - Boss Test keeps its copy
        log.AppendLine("  moved from Boss Test: [" + string.Join(", ", kit.Select(g => g.name)) + "]");

        // same layout as Boss Test (its ground was at y = 0), centred on the arena and sat on the terrain
        foreach (var g in kit.Where(g => Placed.Contains(g.name)))
        {
            var p = g.transform.position;
            g.transform.position = OnGround(p.x, p.z, p.y);
        }
        var boss = kit.FirstOrDefault(g => g.name == "Goyo Boss");
        if (boss != null && NavMesh.SamplePosition(boss.transform.position, out NavMeshHit bh, 2f, NavMesh.AllAreas)) boss.transform.position = bh.position;

        // reinforcement spawn points: same directions, pulled in to SpawnRing, on the NavMesh, facing the middle
        var reinf = kit.FirstOrDefault(g => g.name == "Goyo Reinforcements");
        var points = reinf != null ? reinf.transform.Find("Spawn Points") : null;
        int onMesh = 0, total = 0;
        if (points != null)
            foreach (Transform sp in points)
            {
                total++;
                Vector3 d = sp.position - C; d.y = 0f;
                if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
                d = d.normalized * SpawnRing;
                Vector3 pos = OnGround(d.x, d.z, 0f);
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas)) { pos = hit.position; onMesh++; }
                sp.SetPositionAndRotation(pos, Quaternion.LookRotation(-d));
            }
        log.AppendFormat("  reinforcement spawn points: {0}/{1} on the NavMesh, {2} m from the centre\n", onMesh, total, SpawnRing);

        var player = kit.FirstOrDefault(g => g.name == "Player");
        log.AppendFormat("  player at {0}, boss at {1} (on NavMesh: {2})\n",
            player != null ? player.transform.position.ToString() : "MISSING",
            boss != null ? boss.transform.position.ToString() : "MISSING",
            boss != null && NavMesh.SamplePosition(boss.transform.position, out _, 0.5f, NavMesh.AllAreas));

        EditorSceneManager.MarkSceneDirty(level3);
        EditorSceneManager.SaveScene(level3);
        AssetDatabase.SaveAssets();
        log.AppendLine("  Level 3 saved");
        return log.ToString();
    }
}
#endif
