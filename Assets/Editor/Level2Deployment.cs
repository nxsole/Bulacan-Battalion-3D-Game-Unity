// Tools > Level 2 > Deploy troops + bake NavMesh
// Mans every sandbag barricade in Level 2 with Filipino soldiers who hold their ground, and sets up
// two American wave spawners - one at each end of the trail - whose troops march along it and
// converge on the Filipino camp in the middle, with a wave director sending waves from each end in
// turn and an on-screen bar showing how close each column is to the camp. Brings over Level 1's player kit (player
// with pause menu and fade-in, rifle pickup, pause manager, event system, intro and win/lose
// screens), puts the player on the south front line with a sprint, adds an intro skip and the
// on-screen survival timer that wins the level when it runs out, and bakes a NavMeshSurface over the
// play area so everyone can path. Safe to run again: it re-uses/trims the soldiers it placed last
// time and rebuilds the spawners (keeping their Inspector settings).
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public static class Level2Deployment
{
    const string ScenePath = "Assets/Scenes/Level 2 Gameplay.unity"; // was "Level 2.unity" before the cutscene scenes were split out
    const string NavMeshDir = "Assets/Scenes/Level 2";
    const string FilipinoPrefabPath = "Assets/Entities/Pinoy/Filipino Soldier.prefab";
    const string AmericanPrefabPath = "Assets/Entities/American/American Soldier.prefab";
    const string EnvRootName = "Mountain Environment";
    const string FilipinoRootName = "Pinoy";
    const string AmericanRootName = "American";
    const string SurfaceName = "NavMesh Surface";
    const string TestPlayerName = "Sample Player"; // MountainEnvironmentBuilder's bare test player
    const string TimerName = "Survival Timer";
    const string DirectorName = "American Wave Director";
    const string HudName = "Frontline HUD";

    // ---- player kit copied from Level 1 (root object names there)
    const string Level1ScenePath = "Assets/Scenes/Level 1.unity";
    const string PlayerName = "Player";
    const string RifleName = "Dreyse Needle rifle";
    const string IntroName = "Introduction";
    const string OutcomeName = "Outcome manager";
    static readonly string[] Level1PlayerKit = { PlayerName, RifleName, "PauseManager", "EventSystem", IntroName, OutcomeName };

    const string Level2IntroMessage =
        "December 2, 1899. To cover President Aguinaldo's escape north, General Gregorio del Pilar and some " +
        "sixty men dig in at Tirad Pass to hold back the American column climbing the trail.";
    const string Level2LoseMessage =
        "Tirad Pass fell on December 2, 1899. General Gregorio del Pilar, only 24, died with most of his men - " +
        "but their stand bought Aguinaldo the time he needed to escape.";
    const string Level2WinMessage = "The pass holds.";
    const string Level2OverrunMessage = "The camp is overrun. The pass is lost.";
    const string Level2WinSecondMessage =
        "Every hour held at Tirad Pass was another hour for President Aguinaldo to slip deeper into the " +
        "mountains of Northern Luzon.";

    // ---- barricades
    const float SandbagLinkDistance = 3.5f;   // bags closer than this belong to the same barricade
    const int MinBagsPerBarricade = 5;
    const float DefenderSpacing = 3f;         // matches EnemyAI.minSeparationDistance
    const float DefenderSetback = 1.3f;       // metres behind the bag line's centre
    const float LineEndInset = 0.75f;
    const float ThreatLookAlongTrail = 20f;   // a barricade faces the trail this far toward its attackers

    // ---- combat tuning (Level 2 overrides; the prefabs and Level 1 keep their own values)
    // Filipinos see and shoot 10 m further than the Americans' 35, so they get the first shots as a
    // column closes in. Kept just under the ~45-50 m to each spawn so troops still don't come under
    // fire the moment they appear.
    const float FilipinoDetectionRadius = 45f;
    const float AmericanHitChance = 0.03f;    // chance a shot kills an AI soldier; Level 1 uses 0.06

    // ---- player aim: the prefab's aim drift is a 2 cm offset on a ~1 minute cycle, so each time you
    // aim the sights settle somewhere different and stay there. This makes it a small breathing sway
    // around the centre instead: the same sight picture every time, with a little life in it.
    const float PlayerAimDriftAmount = 0.0015f;
    const float PlayerAimDriftSpeed = 1.2f;

    // ---- the two American fronts. Both columns march along the trail toward the Filipino camp in the
    // middle (CentreZ, halfway between the outermost barricades at z ~ -39 and ~ 74). Barricades south of
    // it face the south column, the ones north of it face the north column. Each spawn sits ~45 m from
    // the nearest sandbags: just outside both sides' 35-40 m detection range, so troops don't pop in
    // mid-fight, but close enough that the shooting starts within seconds of a wave setting off.
    const float CentreZ = 17.5f;
    class Front
    {
        public string label;
        public float spawnZ;
        public string legacySpawner, legacyPath; // names used before there were two fronts
        public string Spawner => "American Spawner (" + label + ")";
        public string Path => "American Advance Path (" + label + ")";
        public float TowardCentre => spawnZ < CentreZ ? 1f : -1f;
    }
    static readonly Front[] Fronts =
    {
        new Front { label = "South", spawnZ = -85f, legacySpawner = "American Spawner", legacyPath = "American Advance Path" },
        new Front { label = "North", spawnZ = 120f },
    };
    const int SpawnPointColumns = 6;
    const int SpawnPointRows = 2;
    const float SpawnPointSpacing = 3f;
    const int MaxAlivePerSpawner = 20; // two spawners now share the field (performance cap)

    // ---- advance: waypoints along the trail from each spawn to the camp
    const float AdvanceWaypointStep = 15f;
    // A new waypoint order pre-empts combat, so give each leg long enough to fight out a barricade
    // before the follower's safety timeout pushes the squad on to the next point.
    const float AdvanceLegTimeout = 90f;

    // ---- player: on the south front line, just behind the gap between the two forward barricades
    const float PlayerSetback = 3.5f;
    const float PlayerClearance = 2.2f; // no defender post this close to the player's spot
    const float RifleClearance = 1.5f;  // the rifle pickup lands at least this far from any defender

    // ---- navmesh volume (the whole trail, from below the south spawn to beyond the north spawn)
    static readonly Vector3 SurfaceCenter = new Vector3(0f, 60f, 15f);
    static readonly Vector3 SurfaceSize = new Vector3(180f, 200f, 300f);

    // Same trail curve MountainEnvironmentBuilder carves into the terrain.
    static float PathX(float z) { return 12f * Mathf.Sin(z * 0.045f) + 5f * Mathf.Sin(z * 0.11f + 1f) - z * 0.1f; }
    static Vector3 TrailPoint(float z) { return new Vector3(PathX(z), 0f, z); }

    class Barricade
    {
        public List<Transform> bags = new List<Transform>();
        public Vector3 centre;   // XZ centroid
        public Vector3 along;    // unit, along the bag line
        public Vector3 back;     // unit, from the bags toward the defenders (away from the threat)
        public float halfLength;
        public bool facesNorth;
    }

    [MenuItem("Tools/Level 2/Deploy troops + bake NavMesh")]
    public static void Deploy()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorUtility.DisplayDialog("Level 2 Deployment", "This works on " + ScenePath + ". Open it now?", "Open", "Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        Debug.Log(Run());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // Unity.exe -batchmode -projectPath <proj> -executeMethod Level2Deployment.DeployBatch -quit
    public static void DeployBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        string report = Run();
        Debug.Log(report);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static string Run()
    {
        var log = new StringBuilder("Level 2 deployment\n");
        var env = GameObject.Find(EnvRootName);
        if (env == null) return "Level 2 deployment: no '" + EnvRootName + "' in the scene.";
        var terrain = env.GetComponentInChildren<Terrain>();
        var filipinoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FilipinoPrefabPath);
        var americanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AmericanPrefabPath);
        if (terrain == null || filipinoPrefab == null || americanPrefab == null)
            return "Level 2 deployment: missing terrain or soldier prefabs.";

        if (ImportLevel1PlayerKit(log)) SetLevel2Texts(log);
        BakeNavMesh(log);

        // ---- Filipinos: one soldier every DefenderSpacing metres behind each barricade, kept clear of the player's spot
        var barricades = FindBarricades(env.transform);
        bool hasPlayerSpot = PlayerSpot(barricades, terrain, out Pose playerPose);
        var slots = new List<Pose>();
        foreach (var b in barricades)
        {
            int n = Mathf.Max(1, Mathf.FloorToInt((2f * (b.halfLength - LineEndInset)) / DefenderSpacing) + 1);
            int placed = 0;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : Mathf.Lerp(-(b.halfLength - LineEndInset), b.halfLength - LineEndInset, i / (n - 1f));
                if (!TryDefenderSlot(b, t, terrain, out Pose pose)) continue;
                if (hasPlayerSpot && FlatDistance(pose.position, playerPose.position) < PlayerClearance) continue;
                slots.Add(pose);
                placed++;
            }
            log.AppendFormat("  barricade at ({0:F0}, {1:F0}): {2} bags, {3} defenders, facing {4} ({5:F0} deg)\n",
                b.centre.x, b.centre.z, b.bags.Count, placed, b.facesNorth ? "the north column" : "the south column",
                Quaternion.LookRotation(-b.back).eulerAngles.y);
        }

        var pinoy = FindRoot(FilipinoRootName) ?? new GameObject(FilipinoRootName);
        Undo.RecordObject(pinoy.transform, "Deploy Filipinos");
        pinoy.SetActive(true); // MountainEnvironmentBuilder's test player switched it off
        pinoy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        pinoy.transform.localScale = Vector3.one;

        var filipinos = Children(pinoy.transform, filipinoPrefab);
        var template = filipinos.FirstOrDefault();
        while (filipinos.Count > slots.Count)
        {
            Undo.DestroyObjectImmediate(filipinos[filipinos.Count - 1].gameObject);
            filipinos.RemoveAt(filipinos.Count - 1);
        }
        while (filipinos.Count < slots.Count)
            filipinos.Add(SpawnLike(filipinoPrefab, template, pinoy.transform, "Filipino Soldier"));
        for (int i = 0; i < slots.Count; i++)
        {
            Undo.RecordObject(filipinos[i], "Deploy Filipinos");
            filipinos[i].SetPositionAndRotation(slots[i].position, slots[i].rotation);
            filipinos[i].gameObject.SetActive(true);
            filipinos[i].name = i == 0 ? "Filipino Soldier" : "Filipino Soldier (" + i + ")";
            ConfigureFilipino(filipinos[i].gameObject);
        }
        log.AppendFormat("  Filipinos: {0} at {1} barricades, holding position\n", slots.Count, barricades.Count);

        // ---- Americans: no hand-placed troops; a spawner at each end of the trail sends them in in waves
        var oldAmerican = FindRoot(AmericanRootName);
        if (oldAmerican != null) Undo.DestroyObjectImmediate(oldAmerican);
        var american = new GameObject(AmericanRootName); // empty at edit time - spawned troops are parented here
        Undo.RegisterCreatedObjectUndo(american, "Deploy Americans");

        var spawners = new List<Level2WaveSpawner>();
        var spawnPoints = new Dictionary<Front, Transform[]>();
        foreach (var front in Fronts)
        {
            var advance = BuildAdvancePath(front, terrain);
            var points = BuildSpawner(front, americanPrefab, advance, american.transform, terrain, out var spawner);
            spawners.Add(spawner);
            spawnPoints[front] = points;
            Vector3 anchor = TrailPoint(front.spawnZ);
            log.AppendFormat("  {0} spawner: {1} spawn points around ({2:F0}, {3:F0}); advance path of {4} waypoints to the camp at z {5:F0}\n",
                front.label, points.Length, anchor.x, anchor.z, advance.transform.childCount, CentreZ);
        }

        var director = BuildWaveDirector(spawners, log);
        BuildFrontlineHud(spawners, barricades, terrain, log);

        var player = PlacePlayer(hasPlayerSpot, playerPose, slots, terrain, log);
        BuildIntroSkip(log);
        if (player != null) { BuildSprint(player, log); SteadyPlayerAim(player, log); }
        var timer = BuildSurvivalTimer(log);
        if (player != null) WireSurvivalTimer(timer, player, director, log);
        AddCampOverrun(log);

        // ---- verify each column can actually reach the defenders on the baked NavMesh
        foreach (var front in Fronts)
        {
            int reachable = 0;
            var path = new NavMeshPath();
            var pts = spawnPoints[front];
            Vector3 from = pts.Length > 0 ? pts[0].position : TrailPoint(front.spawnZ);
            if (NavMesh.SamplePosition(from, out NavMeshHit fromHit, 2f, NavMesh.AllAreas))
                foreach (var s in slots)
                    if (NavMesh.SamplePosition(s.position, out NavMeshHit toHit, 2f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path)
                        && path.status == NavMeshPathStatus.PathComplete) reachable++;
            log.AppendFormat("  NavMesh: {0} column can path to {1}/{2} Filipino positions\n", front.label, reachable, slots.Count);
        }
        return log.ToString();
    }

    // ---------------------------------------------------------------- navmesh
    static void BakeNavMesh(StringBuilder log)
    {
        var go = FindRoot(SurfaceName);
        if (go == null)
        {
            go = new GameObject(SurfaceName);
            Undo.RegisterCreatedObjectUndo(go, "Bake NavMesh");
        }
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var surface = go.GetComponent<NavMeshSurface>() ?? Undo.AddComponent<NavMeshSurface>(go);
        surface.agentTypeID = 0; // Humanoid, same as the soldiers' NavMeshAgents
        surface.collectObjects = CollectObjects.Volume;
        surface.center = SurfaceCenter;
        surface.size = SurfaceSize;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes; // same as Level 1's terrain surface
        // Leave the soldiers, the player and their rifles out of the bake (People / Weapon layers).
        surface.layerMask = ~(LayerMask.GetMask("People", "Weapon", "UI", "Ignore Raycast"));
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.minRegionArea = 2f;

        surface.BuildNavMesh();
        if (surface.navMeshData == null) { log.AppendLine("  NavMesh: bake FAILED"); return; }

        if (!AssetDatabase.IsValidFolder(NavMeshDir))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(NavMeshDir).Replace('\\', '/'), System.IO.Path.GetFileName(NavMeshDir));
        string assetPath = NavMeshDir + "/NavMesh-" + SurfaceName.Replace(" ", "") + ".asset";
        var old = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
        if (old != null && old != surface.navMeshData) AssetDatabase.DeleteAsset(assetPath);
        if (!AssetDatabase.Contains(surface.navMeshData)) AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
        EditorUtility.SetDirty(surface);

        var tri = NavMesh.CalculateTriangulation();
        log.AppendFormat("  NavMesh: baked {0} triangles into {1}\n", tri.indices.Length / 3, assetPath);
    }

    // ---------------------------------------------------------------- barricades
    static List<Barricade> FindBarricades(Transform env)
    {
        var bags = env.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Sandbag")).ToList();
        var parent = Enumerable.Range(0, bags.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        for (int i = 0; i < bags.Count; i++)
            for (int j = i + 1; j < bags.Count; j++)
            {
                Vector3 d = bags[i].position - bags[j].position; d.y = 0f;
                if (d.magnitude < SandbagLinkDistance) parent[Find(i)] = Find(j);
            }

        var result = new List<Barricade>();
        foreach (var group in Enumerable.Range(0, bags.Count).GroupBy(Find))
        {
            if (group.Count() < MinBagsPerBarricade) continue;
            var b = new Barricade();
            foreach (int i in group) b.bags.Add(bags[i]);
            foreach (var t in b.bags) b.centre += new Vector3(t.position.x, 0f, t.position.z) / b.bags.Count;

            // principal axis of the bags on the ground plane = direction the line runs
            float sxx = 0, szz = 0, sxz = 0;
            foreach (var t in b.bags) { float dx = t.position.x - b.centre.x, dz = t.position.z - b.centre.z; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; }
            float ang = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);
            b.along = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            foreach (var t in b.bags) b.halfLength = Mathf.Max(b.halfLength, Mathf.Abs(Vector3.Dot(t.position - b.centre, b.along)));

            // The Americans come along the trail from whichever end this barricade is on, so it's
            // defended from the side away from the trail a little further toward that end. That works
            // both for lines across the trail and for the flanking lines beside it.
            b.facesNorth = b.centre.z > CentreZ;
            Vector3 threat = TrailPoint(b.centre.z + (b.facesNorth ? ThreatLookAlongTrail : -ThreatLookAlongTrail));
            Vector3 normal = Vector3.Cross(Vector3.up, b.along);
            b.back = Vector3.Dot(normal, b.centre - threat) >= 0f ? normal : -normal;
            result.Add(b);
        }
        // south to north, so a re-run keeps the same soldiers in the same spots
        return result.OrderBy(b => b.centre.z).ThenBy(b => b.centre.x).ToList();
    }

    static bool TryDefenderSlot(Barricade b, float t, Terrain terrain, out Pose pose)
    {
        pose = default;
        foreach (float setback in new[] { DefenderSetback, 1.6f, 2f, 2.5f })
        {
            Vector3 want = b.centre + b.along * t + b.back * setback;
            if (!SnapToGround(want, terrain, 0.6f, out Vector3 ground)) continue;
            // the snap must not have slid the soldier round to the enemy side of the bags
            if (Vector3.Dot(ground - (b.centre + b.along * t), b.back) < 0.9f) continue;
            pose = new Pose(ground, Quaternion.LookRotation(-b.back));
            return true;
        }
        return false;
    }

    // Walkable XZ from the NavMesh, height from the terrain itself (EnemyAI drives the body by root
    // motion, and the NavMesh's approximate height has put units into the floor before).
    static bool SnapToGround(Vector3 p, Terrain terrain, float maxDistance, out Vector3 ground)
    {
        p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
        ground = p;
        if (!NavMesh.SamplePosition(p, out NavMeshHit hit, maxDistance, NavMesh.AllAreas)) return false;
        ground = hit.position;
        ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y;
        return true;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ---------------------------------------------------------------- soldiers
    static GameObject FindRoot(string name)
    {
        return EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);
    }

    static List<Transform> Children(Transform root, GameObject prefab)
    {
        var list = new List<Transform>();
        foreach (Transform c in root)
            if (PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject) == prefab) list.Add(c);
        return list;
    }

    // New instance of the prefab carrying the same overrides as an already-configured one in the scene
    // (target tags, hit chance...). References to scene objects are left out since they'd point at the
    // template's own components rather than the new soldier's.
    static Transform SpawnLike(GameObject prefab, Transform template, Transform parent, string name)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "Deploy Filipinos");
        if (template != null)
        {
            var mods = PrefabUtility.GetPropertyModifications(go).ToList();
            foreach (var m in PrefabUtility.GetPropertyModifications(template.gameObject))
            {
                if (m.target is Transform || m.propertyPath == "m_Name" || m.propertyPath == "m_IsActive") continue;
                if (m.objectReference != null && !EditorUtility.IsPersistent(m.objectReference)) continue;
                mods.Add(m);
            }
            PrefabUtility.SetPropertyModifications(go, mods.ToArray());
        }
        go.name = name;
        return go.transform;
    }

    // Waypoints every AdvanceWaypointStep metres along the trail, from just past this front's spawn to
    // the camp in the middle. Lives on its own root and is handed to that front's Americans explicitly.
    static AllyPath BuildAdvancePath(Front front, Terrain terrain)
    {
        foreach (var name in new[] { front.Path, front.legacyPath })
        {
            var old = name != null ? FindRoot(name) : null;
            if (old != null) Undo.DestroyObjectImmediate(old);
        }
        var root = new GameObject(front.Path);
        Undo.RegisterCreatedObjectUndo(root, "American advance path");
        var path = root.AddComponent<AllyPath>();

        var zs = new List<float>();
        float dir = front.TowardCentre;
        for (float z = front.spawnZ + dir * AdvanceWaypointStep; (CentreZ - z) * dir > 0.01f; z += dir * AdvanceWaypointStep) zs.Add(z);
        zs.Add(CentreZ); // always end on the camp

        int n = 0;
        foreach (float z in zs)
        {
            if (!SnapToGround(TrailPoint(z), terrain, 6f, out Vector3 p)) continue;
            var wp = new GameObject("Waypoint " + n++);
            wp.transform.SetParent(root.transform, false);
            wp.transform.position = p;
        }
        return path;
    }

    // Spawner at one end of the trail: a grid of spawn points across the trail facing toward the camp,
    // and one inactive, fully configured American that Level2WaveSpawner copies for every soldier.
    static Transform[] BuildSpawner(Front front, GameObject americanPrefab, AllyPath advance, Transform container, Terrain terrain, out Level2WaveSpawner spawner)
    {
        // removed at the end, once its Inspector settings are copied over
        var old = FindRoot(front.Spawner) ?? (front.legacySpawner != null ? FindRoot(front.legacySpawner) : null);
        var root = new GameObject(front.Spawner);
        Undo.RegisterCreatedObjectUndo(root, "American spawner");

        Vector3 anchor = TrailPoint(front.spawnZ);
        Vector3 forward = (TrailPoint(front.spawnZ + front.TowardCentre * 20f) - anchor).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        SnapToGround(anchor, terrain, 6f, out Vector3 rootPos);
        root.transform.SetPositionAndRotation(rootPos, Quaternion.LookRotation(forward));

        var pointsRoot = new GameObject("Spawn Points").transform;
        pointsRoot.SetParent(root.transform, false);
        var points = new List<Transform>();
        for (int r = 0; r < SpawnPointRows; r++)
            for (int c = 0; c < SpawnPointColumns; c++)
            {
                Vector3 p = anchor + right * ((c - (SpawnPointColumns - 1) * 0.5f) * SpawnPointSpacing + (r % 2) * SpawnPointSpacing * 0.5f)
                                   - forward * (r * SpawnPointSpacing);
                if (!SnapToGround(p, terrain, 4f, out Vector3 ground)) continue;
                var sp = new GameObject("Spawn Point " + points.Count).transform;
                sp.SetParent(pointsRoot, false);
                sp.SetPositionAndRotation(ground, Quaternion.LookRotation(forward));
                points.Add(sp);
            }

        var template = (GameObject)PrefabUtility.InstantiatePrefab(americanPrefab, root.transform);
        template.name = "American Template (copied by spawner)";
        template.transform.SetPositionAndRotation(rootPos, Quaternion.LookRotation(forward));
        ConfigureAmerican(template, 35f, advance);
        template.SetActive(false);

        spawner = root.AddComponent<Level2WaveSpawner>();
        // Keep wave sizes, timing and event hookups tuned in the Inspector across re-runs (a brand-new
        // spawner keeps the script's defaults). Any old win-on-last-wave hookup is dropped: Level 2 is
        // won by the survival timer.
        var oldSpawner = old != null ? old.GetComponent<Level2WaveSpawner>() : null;
        if (oldSpawner != null)
        {
            EditorUtility.CopySerialized(oldSpawner, spawner);
            RemoveListeners(spawner.onAllWavesDefeated, t => t == null || t is GameOutcomeManager);
        }
        bool firstBuildOfThisFront = old == null || old.name != front.Spawner;
        if (old != null) Undo.DestroyObjectImmediate(old);

        var so = new SerializedObject(spawner);
        so.FindProperty("template").objectReferenceValue = template;
        so.FindProperty("container").objectReferenceValue = container;
        var arr = so.FindProperty("spawnPoints");
        arr.arraySize = points.Count;
        for (int i = 0; i < points.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        // With two spawners on the field, cap each lower than the single spawner's 30 (set once, so a
        // value tuned in the Inspector afterwards is kept).
        if (firstBuildOfThisFront) so.FindProperty("maxAlive").intValue = MaxAlivePerSpawner;
        so.FindProperty("runOwnWaves").boolValue = false; // the wave director hands out waves, alternating sides
        so.ApplyModifiedPropertiesWithoutUndo();
        return points.ToArray();
    }

    // ---------------------------------------------------------------- player
    // Behind the middle of the two southernmost barricades, facing down the trail.
    static bool PlayerSpot(List<Barricade> barricades, Terrain terrain, out Pose pose)
    {
        pose = default;
        if (barricades.Count == 0) return false;
        var front = barricades.Take(Mathf.Min(2, barricades.Count)).ToList(); // sorted south to north
        Vector3 centre = Vector3.zero, back = Vector3.zero;
        foreach (var b in front) { centre += b.centre / front.Count; back += b.back; }
        back.Normalize();
        if (!SnapToGround(centre + back * PlayerSetback, terrain, 3f, out Vector3 pos)) return false;
        pose = new Pose(pos, Quaternion.LookRotation(-back));
        return true;
    }

    // The Level 1 player if it's been imported (the builder's bare test player is then removed),
    // otherwise the test player. Goes on the front line with the rifle pickup lying close by.
    static Transform PlacePlayer(bool hasSpot, Pose spot, List<Pose> defenderSlots, Terrain terrain, StringBuilder log)
    {
        var env = GameObject.Find(EnvRootName);
        var testPlayer = env != null ? env.transform.Find(TestPlayerName) : null;
        var player = FindRoot(PlayerName)?.transform;
        if (player != null && testPlayer != null)
        {
            Undo.DestroyObjectImmediate(testPlayer.gameObject);
            log.AppendLine("  Player: removed the builder's '" + TestPlayerName + "' in favour of Level 1's player");
        }
        if (player == null) player = testPlayer;
        if (player == null) { log.AppendLine("  Player: none found, left alone"); return null; }
        if (!hasSpot) { log.AppendLine("  Player: no walkable ground behind the front line, left alone"); return player; }

        Undo.RecordObject(player, "Move player");
        player.SetPositionAndRotation(spot.position + Vector3.up * 0.1f, spot.rotation);
        log.AppendFormat("  Player: '{0}' at ({1:F1}, {2:F1}, {3:F1}) behind the south front line\n", player.name, spot.position.x, spot.position.y, spot.position.z);

        // Rifle pickup lying close to the player, on a spot no defender is standing on (it has a
        // Rigidbody, so it's dropped from just above the ground and settles itself). Tilt matches how
        // it lies in Level 1.
        var rifle = FindRoot(RifleName);
        if (rifle == null) return player;
        Vector3 f = spot.rotation * Vector3.forward, r = spot.rotation * Vector3.right;
        Vector3[] offsets = { -f * 1.6f, -f * 1.6f + r * 1.2f, -f * 1.6f - r * 1.2f, r * 1.8f, -r * 1.8f, -f * 2.5f };
        foreach (var off in offsets)
        {
            if (!SnapToGround(spot.position + off, terrain, 1f, out Vector3 rp)) continue;
            if (defenderSlots.Any(s => FlatDistance(s.position, rp) < RifleClearance)) continue;
            Undo.RecordObject(rifle.transform, "Move rifle");
            rifle.transform.SetPositionAndRotation(rp + Vector3.up * 0.4f, Quaternion.Euler(-3.2f, spot.rotation.eulerAngles.y - 66f, 24f));
            log.AppendFormat("  Rifle pickup: at ({0:F1}, {1:F1}, {2:F1}), clear of the defenders\n", rp.x, rp.y, rp.z);
            return player;
        }
        log.AppendLine("  Rifle pickup: no clear spot next to the player, left where it was");
        return player;
    }

    // Hold Shift to sprint (Level2Sprint). Built once, then kept so its speed can be tuned in the Inspector.
    static void BuildSprint(Transform player, StringBuilder log)
    {
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc == null) { log.AppendLine("  Sprint: player has no FirstPersonController, skipped"); return; }
        if (player.GetComponent<Level2Sprint>() != null) { log.AppendLine("  Sprint: already set up, kept as is"); return; }

        var sprint = Undo.AddComponent<Level2Sprint>(player.gameObject);
        var fso = new SerializedObject(fpc);
        var camT = fso.FindProperty("playerCamera").objectReferenceValue as Transform;
        var so = new SerializedObject(sprint);
        so.FindProperty("player").objectReferenceValue = fpc;
        so.FindProperty("playerCamera").objectReferenceValue = camT != null ? camT.GetComponent<Camera>() : null;
        so.FindProperty("deathScreen").objectReferenceValue = fso.FindProperty("deathScreenCanvasGroup").objectReferenceValue;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("  Sprint: hold Shift to sprint");
    }

    // Override on Level 2's player only - FirstPersonController and the Player prefab are unchanged.
    static void SteadyPlayerAim(Transform player, StringBuilder log)
    {
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc == null) return;
        var so = new SerializedObject(fpc);
        so.FindProperty("aimDriftAmount").floatValue = PlayerAimDriftAmount;
        so.FindProperty("aimDriftSpeed").floatValue = PlayerAimDriftSpeed;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendFormat("  Player aim: drift {0} at speed {1} (prefab: 0.02 at 0.1), so the sights settle in the same place each time\n",
            PlayerAimDriftAmount, PlayerAimDriftSpeed);
    }

    // ---------------------------------------------------------------- player kit from Level 1
    // Copies Level 1's player setup into Level 2 exactly the way Ctrl+C / Ctrl+V between two open
    // scenes does, so prefab links, overrides and the references between the copied objects all
    // survive: the player (with its pause menu, fade-in and weapon-pickup wiring), the rifle pickup
    // with its "Press E" prompt, the pause manager, the event system the menu buttons need, the
    // intro screen and the win/lose screens. Level 1 is opened read-only and closed unsaved.
    // Only done once: if any of these already exist in Level 2 they're left as they are.
    static bool ImportLevel1PlayerKit(StringBuilder log)
    {
        var present = Level1PlayerKit.Where(n => FindRoot(n) != null).ToList();
        if (present.Count > 0)
        {
            log.AppendFormat("  Level 1 player kit: already in Level 2 ({0}); not re-imported - delete them first to import fresh\n",
                string.Join(", ", present));
            return false;
        }

        var level2 = EditorSceneManager.GetActiveScene();
        var level1 = EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Additive);
        try
        {
            Selection.objects = level1.GetRootGameObjects().Where(g => Level1PlayerKit.Contains(g.name)).ToArray();
            Unsupported.CopyGameObjectsToPasteboard();
            EditorSceneManager.SetActiveScene(level2);
            Selection.objects = new Object[0]; // nothing selected: paste goes to the root of the active scene
            Unsupported.PasteGameObjectsFromPasteboard();
        }
        finally
        {
            EditorSceneManager.SetActiveScene(level2);
            EditorSceneManager.CloseScene(level1, true);
            Selection.objects = new Object[0];
        }

        var imported = Level1PlayerKit.Where(n => FindRoot(n) != null).ToList();
        log.AppendFormat("  Level 1 player kit: imported {0}/{1} ({2})\n", imported.Count, Level1PlayerKit.Length, string.Join(", ", imported));

        // sanity-check the wiring that has to survive the copy
        var pause = FindRoot("PauseManager")?.GetComponent<PauseManager>();
        var fpc = FindRoot(PlayerName)?.GetComponent<FirstPersonController>();
        string Check(Object o) => o == null ? "MISSING" : "ok";
        string pickup = "MISSING";
        if (fpc != null)
        {
            var so = new SerializedObject(fpc);
            var pick = so.FindProperty("weaponPickupObject").objectReferenceValue as GameObject;
            var prompt = so.FindProperty("interactionPromptCanvasGroup").objectReferenceValue;
            pickup = pick != null && pick.scene == level2 && prompt != null ? "ok" : "MISSING";
        }
        log.AppendFormat("    wiring: pause menu {0}, pause->player {1}, pause->music {2}, player->rifle pickup {3}\n",
            Check(pause != null ? pause.pauseMenuPanel : null), Check(pause != null ? pause.playerController : null),
            Check(pause != null ? pause.backgroundMusic : null), pickup);
        return imported.Count == Level1PlayerKit.Length;
    }

    // The intro and win/lose screens came over with Level 1's story text; give them Level 2's.
    // Only done on import, so later edits made in the Inspector aren't overwritten by a re-run.
    static void SetLevel2Texts(StringBuilder log)
    {
        var intro = FindRoot(IntroName)?.GetComponent<SceneIntroController>();
        if (intro != null)
        {
            var so = new SerializedObject(intro);
            so.FindProperty("introMessage").stringValue = Level2IntroMessage;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        var outcome = FindRoot(OutcomeName)?.GetComponent<GameOutcomeManager>();
        if (outcome != null)
        {
            var so = new SerializedObject(outcome);
            so.FindProperty("loseMessage").stringValue = Level2LoseMessage;
            so.FindProperty("winMessage").stringValue = Level2WinMessage;
            so.FindProperty("winSecondMessage").stringValue = Level2WinSecondMessage;
            so.FindProperty("overrunMessage").stringValue = Level2OverrunMessage;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        log.AppendLine("  Intro / win / overrun / lose text: set for Tirad Pass");
    }

    // ---------------------------------------------------------------- intro skip
    // Adds IntroSkip to Level 2's copy of the intro, plus a "Press SPACE to skip" hint in the
    // intro's own canvas (drawn over its black screen, same font as its text). Built once, then kept.
    static void BuildIntroSkip(StringBuilder log)
    {
        var introGo = FindRoot(IntroName);
        var intro = introGo != null ? introGo.GetComponent<SceneIntroController>() : null;
        if (intro == null) { log.AppendLine("  Intro skip: no intro in the scene, skipped"); return; }
        if (introGo.GetComponent<IntroSkip>() != null) { log.AppendLine("  Intro skip: already set up, kept as is"); return; }

        var introSo = new SerializedObject(intro);
        var black = introSo.FindProperty("blackOverlay").objectReferenceValue as Image;
        var introText = introSo.FindProperty("introText").objectReferenceValue as TMP_Text;
        if (black == null) { log.AppendLine("  Intro skip: intro has no black screen assigned, skipped"); return; }

        var promptGo = new GameObject("Skip Prompt", typeof(RectTransform));
        promptGo.layer = black.gameObject.layer;
        promptGo.transform.SetParent(black.transform.parent, false);
        promptGo.transform.SetAsLastSibling(); // draw over the black screen
        var rt = (RectTransform)promptGo.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-40f, 36f);
        rt.sizeDelta = new Vector2(600f, 60f);
        var prompt = promptGo.AddComponent<TextMeshProUGUI>();
        prompt.text = "Press SPACE to skip";
        prompt.alignment = TextAlignmentOptions.BottomRight;
        prompt.fontSize = 28f;
        prompt.color = new Color(1f, 1f, 1f, 0.7f);
        prompt.raycastTarget = false;
        if (introText != null && introText.font != null) prompt.font = introText.font;
        promptGo.SetActive(false); // IntroSkip shows it while the intro is up
        Undo.RegisterCreatedObjectUndo(promptGo, "Intro skip prompt");

        var skip = Undo.AddComponent<IntroSkip>(introGo);
        var so = new SerializedObject(skip);
        so.FindProperty("intro").objectReferenceValue = intro;
        so.FindProperty("blackOverlay").objectReferenceValue = black;
        so.FindProperty("introText").objectReferenceValue = introText;
        so.FindProperty("skipPrompt").objectReferenceValue = prompt;
        so.FindProperty("pauseManager").objectReferenceValue = FindRoot("PauseManager")?.GetComponent<PauseManager>();
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("  Intro skip: SPACE / Enter skips the opening text, with an on-screen hint");
    }

    // ---------------------------------------------------------------- survival timer (the win condition)
    // On-screen countdown at the top centre, in the same font as the intro text. Built once; after
    // that it's left alone so its duration/label/colours can be tuned in the Inspector.
    static Level2SurvivalTimer BuildSurvivalTimer(StringBuilder log)
    {
        var existing = FindRoot(TimerName);
        if (existing != null && existing.GetComponent<Level2SurvivalTimer>() != null)
        {
            log.AppendLine("  Survival timer: already set up, kept as is");
            return existing.GetComponent<Level2SurvivalTimer>();
        }
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var root = new GameObject(TimerName);
        Undo.RegisterCreatedObjectUndo(root, "Survival timer");
        var timer = root.AddComponent<Level2SurvivalTimer>();

        var canvasGo = new GameObject("Timer UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1; // under the intro (0) and win/lose (5) black screens, so they cover it
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var textGo = new GameObject("Time", typeof(RectTransform));
        textGo.layer = canvasGo.layer;
        textGo.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -30f);
        rt.sizeDelta = new Vector2(700f, 170f);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Top;
        text.fontSize = 64f;
        text.raycastTarget = false;
        text.text = "<size=55%>HOLD THE PASS</size>\n5:00";

        var intro = FindRoot(IntroName)?.GetComponent<SceneIntroController>();
        var introSo = intro != null ? new SerializedObject(intro) : null;
        var introText = introSo?.FindProperty("introText").objectReferenceValue as TMP_Text;
        if (introText != null && introText.font != null) text.font = introText.font;

        var so = new SerializedObject(timer);
        so.FindProperty("timerText").objectReferenceValue = text;
        so.FindProperty("waitWhileVisible").objectReferenceValue = introSo?.FindProperty("blackOverlay").objectReferenceValue;
        so.ApplyModifiedPropertiesWithoutUndo();

        log.AppendFormat("  Survival timer: built ({0}), starts once the intro's black screen clears\n",
            introText != null ? "intro font" : "default font");
        return timer;
    }

    // Level 1 wins by walking into its Ending zone; Level 2 is won by holding until the timer runs out,
    // which also stops the American waves (the director stops both spawners). Re-wired every run.
    // (Losing needs no wiring: GameOutcomeManager already triggers it when the last Filipino falls.)
    static void WireSurvivalTimer(Level2SurvivalTimer timer, Transform player, Level2WaveDirector director, StringBuilder log)
    {
        var outcome = FindRoot(OutcomeName)?.GetComponent<GameOutcomeManager>();
        // drop this tool's previous hookups (and any left pointing at old, deleted spawners)
        RemoveListeners(timer.onTimerFinished, t => t == null || t is GameOutcomeManager || t is Level2WaveSpawner || t is Level2WaveDirector);
        if (outcome != null)
            UnityEditor.Events.UnityEventTools.AddObjectPersistentListener<Transform>(timer.onTimerFinished, outcome.TriggerWin, player);
        if (director != null)
            UnityEditor.Events.UnityEventTools.AddPersistentListener(timer.onTimerFinished, director.StopSpawning);
        EditorUtility.SetDirty(timer);
        log.AppendFormat("  Win condition: timer runs out -> {0}{1}\n",
            outcome != null ? "GameOutcomeManager.TriggerWin" : "(no outcome manager!)",
            director != null ? " + stop the American waves" : "");
    }

    // The other ending: Americans reaching the camp. Level2CampOverrun goes on the HUD's Camp Centre
    // marker and finds the outcome manager, spawners, director, timer and player by itself. Kept on
    // re-runs, so its radius/count tuned in the Inspector survive.
    static void AddCampOverrun(StringBuilder log)
    {
        var centre = FindRoot(HudName)?.transform.Find("Camp Centre");
        if (centre == null) { log.AppendLine("  Camp overrun: no Camp Centre marker found - not added!"); return; }
        bool created = centre.GetComponent<Level2CampOverrun>() == null;
        if (created) Undo.AddComponent<Level2CampOverrun>(centre.gameObject);
        log.AppendLine("  Camp overrun: " + (created ? "added" : "kept") + " - Americans reaching the camp play the ending with the overrun message");
    }

    // ---------------------------------------------------------------- wave director (alternating sides)
    // Built once and kept, so wave sizes/timing tuned in the Inspector survive re-runs; only its
    // spawner list is refreshed, because the spawners are rebuilt every run.
    static Level2WaveDirector BuildWaveDirector(List<Level2WaveSpawner> spawners, StringBuilder log)
    {
        var root = FindRoot(DirectorName);
        bool created = root == null;
        if (created)
        {
            root = new GameObject(DirectorName);
            Undo.RegisterCreatedObjectUndo(root, "Wave director");
        }
        var director = root.GetComponent<Level2WaveDirector>() ?? Undo.AddComponent<Level2WaveDirector>(root);
        var so = new SerializedObject(director);
        var arr = so.FindProperty("spawners");
        arr.arraySize = spawners.Count;
        for (int i = 0; i < spawners.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = spawners[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendFormat("  Wave director: {0}; waves alternate {1}\n", created ? "built" : "kept (settings unchanged)",
            string.Join(" -> ", spawners.Select(s => s.name.Replace("American Spawner ", "").Trim('(', ')'))) + " -> ...");
        return director;
    }

    // ---------------------------------------------------------------- frontline bar
    // One track under the timer with the camp marked in the middle; each side's fill grows from its end
    // toward the centre as that side's Americans close in, labelled with their mean distance. Built
    // once (so its look can be tuned); the spawner links and the barricade ticks - one per barricade,
    // at its distance from the camp, so a fill reaching a tick means that line has been reached - are
    // refreshed every run.
    static void BuildFrontlineHud(List<Level2WaveSpawner> spawners, List<Barricade> barricades, Terrain terrain, StringBuilder log)
    {
        var root = FindRoot(HudName);
        var hud = root != null ? root.GetComponent<Level2FrontlineHud>() : null;
        bool created = hud == null;
        if (created)
        {
            if (root != null) Undo.DestroyObjectImmediate(root);
            hud = CreateFrontlineHud(terrain);
        }

        var so = new SerializedObject(hud);
        var sides = so.FindProperty("sides");
        for (int i = 0; i < sides.arraySize && i < Fronts.Length; i++)
        {
            var label = Fronts[i].label;
            var spawner = spawners.FirstOrDefault(s => s.name == Fronts[i].Spawner);
            var side = sides.GetArrayElementAtIndex(i);
            side.FindPropertyRelative("label").stringValue = label.ToUpperInvariant();
            side.FindPropertyRelative("spawner").objectReferenceValue = spawner;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // barricade ticks: same scale the bar uses at runtime (empty = spawn, middle = camp)
        int ticks = 0;
        var centre = so.FindProperty("centre").objectReferenceValue as Transform;
        var track = hud.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "Track");
        if (centre != null && track != null)
        {
            foreach (Transform child in track.Cast<Transform>().ToList())
                if (child.name.StartsWith("Line Tick")) Undo.DestroyObjectImmediate(child.gameObject);
            foreach (var b in barricades)
            {
                var spawner = spawners.FirstOrDefault(s => s.name == Fronts[b.facesNorth ? 1 : 0].Spawner);
                if (spawner == null) continue;
                float start = FlatDistance(spawner.SpawnCentre, centre.position);
                float f = start > 1f ? Mathf.Clamp01(1f - FlatDistance(b.centre, centre.position) / start) : 0f;
                float x = b.facesNorth ? 1f - 0.5f * f : 0.5f * f;
                var tick = UiRect("Line Tick " + ticks++, track, new Vector2(x, 0.5f), new Vector2(x, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(2f, 22f));
                UiImage(tick, new Color(1f, 1f, 1f, 0.55f));
                Undo.RegisterCreatedObjectUndo(tick.gameObject, "Line tick");
            }
        }
        log.AppendLine("  Frontline bar: " + (created ? "built" : "kept") + " - south and north columns' mean distance to the camp, "
            + ticks + " barricade ticks");
    }

    static Level2FrontlineHud CreateFrontlineHud(Terrain terrain)
    {
        var root = new GameObject(HudName);
        Undo.RegisterCreatedObjectUndo(root, "Frontline HUD");
        var hud = root.AddComponent<Level2FrontlineHud>();

        // the point both columns converge on
        var centre = new GameObject("Camp Centre").transform;
        centre.SetParent(root.transform, false);
        SnapToGround(TrailPoint(CentreZ), terrain, 6f, out Vector3 c);
        centre.position = c;

        var font = (FindRoot(IntroName)?.GetComponent<SceneIntroController>() is SceneIntroController intro
            ? new SerializedObject(intro).FindProperty("introText").objectReferenceValue as TMP_Text : null)?.font;

        var canvasGo = new GameObject("Frontline UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1; // with the timer: under the intro and win/lose black screens
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // track, just under the survival timer
        var track = UiRect("Track", canvasGo.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -205f), new Vector2(640f, 16f));
        UiImage(track, new Color(0f, 0f, 0f, 0.45f));
        var fillColour = new Color(0.78f, 0.22f, 0.16f, 0.9f);
        var southFill = UiRect("South Fill", track, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        UiImage(southFill, fillColour);
        var northFill = UiRect("North Fill", track, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        UiImage(northFill, fillColour);
        var mark = UiRect("Camp Mark", track, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 28f));
        UiImage(mark, new Color(1f, 0.92f, 0.7f, 1f));

        var campLabel = UiText("Camp Label", track, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
            new Vector2(0f, -6f), new Vector2(200f, 26f), "CAMP", 18f, TextAlignmentOptions.Top, font);
        campLabel.color = new Color(1f, 0.92f, 0.7f, 0.9f);
        var southText = UiText("South Label", track, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(0f, 4f), new Vector2(300f, 30f), "SOUTH  clear", 22f, TextAlignmentOptions.BottomLeft, font);
        var northText = UiText("North Label", track, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 4f), new Vector2(300f, 30f), "NORTH  clear", 22f, TextAlignmentOptions.BottomRight, font);

        var so = new SerializedObject(hud);
        so.FindProperty("centre").objectReferenceValue = centre;
        var sides = so.FindProperty("sides");
        sides.arraySize = 2;
        var south = sides.GetArrayElementAtIndex(0);
        south.FindPropertyRelative("fill").objectReferenceValue = southFill;
        south.FindPropertyRelative("text").objectReferenceValue = southText;
        south.FindPropertyRelative("fromRight").boolValue = false;
        var north = sides.GetArrayElementAtIndex(1);
        north.FindPropertyRelative("fill").objectReferenceValue = northFill;
        north.FindPropertyRelative("text").objectReferenceValue = northText;
        north.FindPropertyRelative("fromRight").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return hud;
    }

    static RectTransform UiRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    static void UiImage(RectTransform rt, Color colour)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = colour;
        img.raycastTarget = false;
    }

    static TextMeshProUGUI UiText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size,
        string text, float fontSize, TextAlignmentOptions align, TMP_FontAsset font)
    {
        var rt = UiRect(name, parent, anchorMin, anchorMax, pivot, pos, size);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.alignment = align;
        t.raycastTarget = false;
        if (font != null) t.font = font;
        return t;
    }

    static void RemoveListeners(UnityEngine.Events.UnityEventBase evt, System.Func<Object, bool> match)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            if (match(evt.GetPersistentTarget(i)))
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(evt, i);
    }

    // Defenders: stay behind the sandbags. EnemyAI is the unchanged Level 1 script, so this is done
    // with its own tuning - no repositioning, no running for cover, no searching, no walking off to
    // squad alerts - plus Level2Defender to walk them back if they drift. Their path follower is
    // switched off so it can't pick up an American advance path (it grabs the first AllyPath in the
    // scene when none is assigned).
    static void ConfigureFilipino(GameObject go)
    {
        var ai = go.GetComponent<EnemyAI>();
        if (ai != null)
        {
            var so = new SerializedObject(ai);
            so.FindProperty("repositionChance").floatValue = 0f;
            so.FindProperty("seekCoverBeforeReload").boolValue = false;
            so.FindProperty("searchDuration").floatValue = 0f;
            so.FindProperty("alertExpireTime").floatValue = -1f; // alerts never count as fresh, so no Investigate walk
            so.FindProperty("detectionRadius").floatValue = FilipinoDetectionRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        if (go.GetComponent<Level2Defender>() == null) go.AddComponent<Level2Defender>();

        // No voice lines in Level 2 - only gunfire, reloads and the hit/death sounds stay. The clips are
        // all per-soldier overrides (copied over from Level 1); the prefab itself has none, so dropping
        // the overrides leaves these fields empty. Also drops overrides for EnemyAI fields that no
        // longer exist (an earlier pass used a holdPosition field).
        var mods = PrefabUtility.GetPropertyModifications(go);
        if (mods != null && mods.Any(DropOverride))
            PrefabUtility.SetPropertyModifications(go, mods.Where(m => !DropOverride(m)).ToArray());
        var follower = go.GetComponent<AllyPathFollower>();
        if (follower != null)
        {
            var so = new SerializedObject(follower);
            so.FindProperty("followOnStart").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static readonly string[] FilipinoVoiceFields =
    {
        "enemySpottedSounds", "enemySpottedVolume",        // "Enemy spotted!" bark
        "battleCryReplySounds", "battleCryReplyDelay",     // squad reply cry
        "battleCryCombatSounds",                           // individual combat cries
        "startMoveClip", "startMoveVolume", "startAudioSource", // path follower's start-of-march line
    };

    static bool DropOverride(PropertyModification m)
    {
        if (m.propertyPath == "holdPosition") return true;
        foreach (var f in FilipinoVoiceFields)
            if (m.propertyPath == f || m.propertyPath.StartsWith(f + ".")) return true;
        return false;
    }

    // Same tuning the Americans use in Level 1, plus a follower that walks them along their advance path.
    static void ConfigureAmerican(GameObject go, float detectionRadius, AllyPath advance)
    {
        var follower = go.GetComponent<AllyPathFollower>() ?? go.AddComponent<AllyPathFollower>();
        var fso = new SerializedObject(follower);
        fso.FindProperty("path").objectReferenceValue = advance;
        fso.FindProperty("followOnStart").boolValue = true;
        fso.FindProperty("perLegTimeout").floatValue = AdvanceLegTimeout;
        fso.ApplyModifiedPropertiesWithoutUndo();

        var ai = go.GetComponent<EnemyAI>();
        if (ai == null) return;
        var so = new SerializedObject(ai);
        var tags = so.FindProperty("targetTags");
        tags.arraySize = 2;
        tags.GetArrayElementAtIndex(0).stringValue = "Ally";
        tags.GetArrayElementAtIndex(1).stringValue = "Player";
        so.FindProperty("detectionRadius").floatValue = detectionRadius;
        so.FindProperty("hitChance").floatValue = AmericanHitChance;
        so.FindProperty("optimalCombatRange").floatValue = 11f;
        so.FindProperty("pressAdvantage").boolValue = true;
        so.FindProperty("deathVolume").floatValue = 0.754f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
