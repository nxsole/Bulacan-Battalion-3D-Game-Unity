// Tools > Boss > Build Goyo boss + test scene
// Builds the Goyo boss from the Goyo model and a scene to try him out in:
//  1. Sets the clips in "boss stuff" (Rifle Run, Rifle Punch, Running Dive Roll) to Humanoid using
//     Goyo's avatar. Rifle Run loops and keeps its forward root motion, so it moves him.
//  2. "Goyo Boss.controller": a copy of the soldiers' American.controller with Rifle Run in every
//     movement state (Walk, Crouch Walk) - the boss only ever runs - plus a Dive Roll state for his
//     dodge. "Goyo Smoke.prefab": the smoke cloud for his battle-cry teleport.
//  3. "Goyo Boss.prefab": Goyo's own model (mesh, skeleton, avatar) with the Filipino soldier's kit
//     moved across bone-for-bone - their skeletons are identical: body collider, NavMeshAgent, audio,
//     the 11-bone ragdoll and the rifle with its muzzle point - plus GoyoBossAI carrying the soldier's
//     tuning with the boss's aim settings on top.
//  4. "Boss Test.unity": flat ground, low cover walls, a baked NavMesh, the boss, and the Level 2 player
//     variant (health bar + Space dash, see PlayerVariantBuilder) with Level 2's rifle pickup, pause
//     manager and event system.
// Safe to run again: everything it makes is rebuilt.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class GoyoBossBuilder
{
    const string BossDir = "Assets/Entities/boss stuff";
    const string GoyoModelPath = "Assets/Entities/for intro cutscene/Filipino Soldier_Goyo.fbx";
    const string SoldierPrefabPath = "Assets/Entities/Pinoy/Filipino Soldier.prefab";
    const string SoldierControllerPath = "Assets/Entities/American/American.controller";
    const string ControllerPath = BossDir + "/Goyo Boss.controller";
    const string PrefabPath = BossDir + "/Goyo Boss.prefab";
    const string TestScenePath = "Assets/Scenes/Boss Test.unity";
    const string TestSceneDir = "Assets/Scenes/Boss Test";
    const string Level2ScenePath = "Assets/Scenes/Level 2 Gameplay.unity";
    static readonly string[] SceneKit = { "Dreyse Needle rifle", "PauseManager", "EventSystem" };

    // No Goyo battle cry is recorded yet (his voice clips are calm cutscene lines), so the most aggressive
    // existing soldier cry stands in. Swap in a recorded one on the prefab's Teleport Cry Clips.
    const string PlaceholderCryPath = "Assets/Entities/Pinoy/cries/ElevenLabs_2026-09-06T05_12_56_Juan - Moody, Aggresive, Irritated_pvc_s50_m2.mp3";

    // Goyo's reinforcements land this fraction of their shots at the player (EnemyAI.playerHitChance;
    // default soldiers are 1 = every shot).
    const float ReinforcementPlayerHitChance = 0.2f;

    const int PeopleLayer = 7;   // soldiers and the player
    const int ObstacleLayer = 6; // EnemyAI's line-of-sight blockers (mask 64) - cover must be on it

    [MenuItem("Tools/Boss/Build Goyo boss + test scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Run());
    }

    // Unity.exe -batchmode -projectPath <proj> -executeMethod GoyoBossBuilder.BuildBatch -quit
    public static void BuildBatch() => Debug.Log(Run());

    static string Run()
    {
        var log = new StringBuilder("Goyo boss build\n");
        var goyoAvatar = AssetDatabase.LoadAllAssetsAtPath(GoyoModelPath).OfType<Avatar>().FirstOrDefault();
        if (goyoAvatar == null) return "Goyo boss build: no avatar on " + GoyoModelPath;

        var run = ConfigureClip("Rifle Run", goyoAvatar, loop: true, rootMotionXZ: true, log);
        var punch = ConfigureClip("Rifle Punch", goyoAvatar, loop: false, rootMotionXZ: false, log);
        var dive = ConfigureClip("Running Dive Roll", goyoAvatar, loop: false, rootMotionXZ: true, log);
        if (run == null || dive == null || punch == null) return log.Append("  missing Rifle Run / Running Dive Roll / Rifle Punch clip - stopped").ToString();

        var controller = BuildController(run, dive, punch, log);
        var smoke = BuildSmoke(log, "Goyo Smoke", 1.5f, 2.2f, 3.2f, 2.2f, 3.8f, 28, 12, 1f, "3-4 m rising cloud, ~3 s - teleports, riflemen leaving");
        var shortSmoke = BuildSmoke(log, "Goyo Smoke (Short)", 0.4f, 0.6f, 1.0f, 1.8f, 2.8f, 20, 0, 0.8f, "quick ~1 s puff - riflemen appearing");

        // the test scene doubles as the workspace for building the prefab
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var prefab = BuildPrefab(controller, goyoAvatar, smoke, shortSmoke, dive, punch, log);
        BuildTestScene(scene, prefab, log);
        return log.ToString();
    }

    // ---------------------------------------------------------------- clips
    static AnimationClip ConfigureClip(string fileName, Avatar avatar, bool loop, bool rootMotionXZ, StringBuilder log)
    {
        string path = BossDir + "/" + fileName + ".fbx";
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) { log.AppendLine("  " + fileName + ": not found"); return null; }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importAnimation = true;
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = fileName;
            c.loopTime = loop;
            c.loopPose = loop;
            c.lockRootRotation = true;          // bake rotation into the pose: no drifting turn
            c.keepOriginalOrientation = true;
            c.lockRootHeightY = true;           // bake height into the pose, based on the feet
            c.heightFromFeet = true;
            c.lockRootPositionXZ = !rootMotionXZ; // XZ stays root motion where the clip travels
            c.keepOriginalPositionXZ = !rootMotionXZ;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        var clip = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview"));
        log.AppendFormat("  clip {0}: humanoid={1} length={2:F2}s loop={3} rootMotionXZ={4}\n",
            clip != null ? clip.name : "MISSING", clip != null && clip.humanMotion, clip != null ? clip.length : 0f, loop, rootMotionXZ);
        return clip;
    }

    // ---------------------------------------------------------------- animator
    static AnimatorController BuildController(AnimationClip run, AnimationClip dive, AnimationClip punch, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
        AssetDatabase.CopyAsset(SoldierControllerPath, ControllerPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        var swapped = new List<string>();
        var sm = controller.layers[0].stateMachine;
        AnimatorState idle = null;
        foreach (var child in sm.states)
        {
            if (child.state.name == "Walk" || child.state.name == "Crouch Walk")
            {
                child.state.motion = run;
                swapped.Add(child.state.name);
            }
            if (child.state.name == "Idle") idle = child.state;
        }

        // Dive Roll and Rifle Punch: a trigger jumps to each from anything; back to Idle once it's done
        AddOneShot(controller, sm, idle, "Dive Roll", dive, "DiveRoll", new Vector3(600f, -120f, 0f));
        AddOneShot(controller, sm, idle, "Rifle Punch", punch, "Melee", new Vector3(600f, -40f, 0f));

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        log.AppendFormat("  controller {0}: copy of the soldiers' Animator, Rifle Run in [{1}], Dive Roll (DiveRoll trigger) and Rifle Punch (Melee trigger) states\n",
            ControllerPath, string.Join(", ", swapped));
        return controller;
    }

    static void AddOneShot(AnimatorController controller, AnimatorStateMachine sm, AnimatorState idle, string stateName, AnimationClip clip, string trigger, Vector3 pos)
    {
        controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        var state = sm.AddState(stateName, pos);
        state.motion = clip;
        var into = sm.AddAnyStateTransition(state);
        into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        into.hasExitTime = false;
        into.duration = 0.1f;
        into.canTransitionToSelf = false;
        if (idle == null) return;
        var back = state.AddTransition(idle);
        back.hasExitTime = true;
        back.exitTime = 0.92f;
        back.duration = 0.15f;
    }

    // ---------------------------------------------------------------- smoke
    // A big, slow, rising cloud for the teleport, made from the muzzle flash's smoke layer (the 5x5
    // white-puff sprite sheet) scaled up. Plays once and removes itself.
    static GameObject BuildSmoke(StringBuilder log, string name, float duration, float lifeMin, float lifeMax, float sizeMin, float sizeMax, int burst, int secondBurst, float radius, string what)
    {
        var flash = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Entities/American/MuzzleFlash.prefab");
        var src = flash != null ? flash.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(p => p.name == "Smoke") : null;
        if (src == null) { log.AppendLine("  smoke: muzzle flash smoke layer not found - no smoke"); return null; }

        var go = Object.Instantiate(src.gameObject);
        go.name = name;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var ps = go.GetComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.86f, 0.86f, 0.83f, 0.95f);
        main.gravityModifier = -0.04f; // drifts upward
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.maxParticles = 80;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(secondBurst > 0
            ? new[] { new ParticleSystem.Burst(0f, (short)burst), new ParticleSystem.Burst(0.15f, (short)secondBurst) }
            : new[] { new ParticleSystem.Burst(0f, (short)burst) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.position = new Vector3(0f, 1f, 0f); // around his body, not his feet
        shape.rotation = Vector3.zero;

        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.1f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colour.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.5f));

        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.dampen = 0.15f;

        string path = BossDir + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) AssetDatabase.DeleteAsset(path);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        log.AppendLine("  smoke: " + path + " (" + what + ")");
        return prefab;
    }

    // ---------------------------------------------------------------- prefab
    static GameObject BuildPrefab(AnimatorController controller, Avatar avatar, GameObject smoke, GameObject shortSmoke, AnimationClip dive, AnimationClip punch, StringBuilder log)
    {
        var goyoModel = AssetDatabase.LoadAssetAtPath<GameObject>(GoyoModelPath);
        var soldierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SoldierPrefabPath);
        var boss = (GameObject)PrefabUtility.InstantiatePrefab(goyoModel);
        var soldier = (GameObject)PrefabUtility.InstantiatePrefab(soldierPrefab); // donor, deleted below
        boss.name = "Goyo Boss";
        boss.tag = "Ally"; // the Filipino side
        foreach (var t in boss.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PeopleLayer;

        var bossBones = boss.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

        // root: body capsule, rigidbody, agent, audio
        foreach (var c in new Component[] { soldier.GetComponent<CapsuleCollider>(), soldier.GetComponent<Rigidbody>(),
                                            soldier.GetComponent<NavMeshAgent>(), soldier.GetComponent<AudioSource>() })
            if (c != null) Paste(c, boss);

        var anim = boss.GetComponent<Animator>() ?? boss.AddComponent<Animator>();
        var soldierAnim = soldier.GetComponent<Animator>();
        anim.avatar = avatar;
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = true;
        anim.cullingMode = soldierAnim != null ? soldierAnim.cullingMode : AnimatorCullingMode.CullUpdateTransforms;

        // ragdoll: colliders + rigidbodies first, then joints (whose connected bodies need the new rigidbodies)
        int ragdollBones = 0;
        var joints = new List<(CharacterJoint src, Transform dst)>();
        foreach (var t in soldier.GetComponentsInChildren<Transform>(true))
        {
            if (t == soldier.transform || !bossBones.TryGetValue(t.name, out var dst)) continue;
            bool any = false;
            foreach (var col in t.GetComponents<Collider>()) { Paste(col, dst.gameObject); any = true; }
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) { Paste(rb, dst.gameObject); any = true; }
            var j = t.GetComponent<CharacterJoint>();
            if (j != null) joints.Add((j, dst));
            if (any) ragdollBones++;
        }
        foreach (var (src, dst) in joints)
        {
            var nj = (CharacterJoint)Paste(src, dst.gameObject);
            nj.connectedBody = src.connectedBody != null && bossBones.TryGetValue(src.connectedBody.name, out var cb) ? cb.GetComponent<Rigidbody>() : null;
        }

        // rifle: the soldier's whole "Gun Root" (rifle + muzzle point) onto the same hand bone
        var srcGun = soldier.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Gun Root");
        Transform gun = null;
        if (srcGun != null && bossBones.TryGetValue(srcGun.parent.name, out var hand))
        {
            gun = Object.Instantiate(srcGun.gameObject, hand).transform;
            gun.name = "Gun Root";
            gun.localPosition = srcGun.localPosition;
            gun.localRotation = srcGun.localRotation;
            gun.localScale = srcGun.localScale;
        }
        var muzzle = gun != null ? gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MuzzlePoint") : null;
        string aimFix = gun != null ? AlignRifleToSoldier(boss, soldier, gun) : "no rifle";
        float reach = 0f;
        float impact = muzzle != null ? MeasurePunchImpact(boss, muzzle, punch, out reach) : 1.1f;

        // AI: the soldier's EnemyAI tuning copied onto GoyoBossAI (same field names), then the boss's own
        var ai = boss.AddComponent<GoyoBossAI>();
        var dstSo = new SerializedObject(ai);
        var srcSo = new SerializedObject(soldier.GetComponent<EnemyAI>());
        var it = srcSo.GetIterator();
        it.NextVisible(true);
        while (it.NextVisible(false))
        {
            if (it.name == "m_Script") continue;
            var d = dstSo.FindProperty(it.propertyPath);
            if (d != null && d.propertyType == it.propertyType) dstSo.CopyFromSerializedProperty(it);
        }
        dstSo.FindProperty("firePoint").objectReferenceValue = muzzle;
        dstSo.FindProperty("weaponRoot").objectReferenceValue = gun;
        dstSo.FindProperty("audioSource").objectReferenceValue = boss.GetComponent<AudioSource>();
        SetStrings(dstSo.FindProperty("targetTags"), "Player", "Enemy"); // the player, and American soldiers
        foreach (var voice in new[] { "enemySpottedSounds", "battleCryReplySounds", "battleCryCombatSounds" })
            dstSo.FindProperty(voice).arraySize = 0; // no soldier voice lines on the boss
        dstSo.FindProperty("aimDurationMin").floatValue = 1.2f; // longer than a soldier's 0.4-0.9 s, so the
        dstSo.FindProperty("aimDurationMax").floatValue = 1.6f; // locked half is long enough to dodge
        dstSo.FindProperty("aimTrackFraction").floatValue = 0.5f;
        dstSo.FindProperty("detectionRadius").floatValue = 40f;
        dstSo.FindProperty("hitChance").floatValue = 0.5f;
        // keeps moving: always repositions after a reload (soldier: 20% chance, within 2 m), a real
        // distance each time, and reloads quicker so he's on the move more often
        dstSo.FindProperty("repositionChance").floatValue = 1f;
        dstSo.FindProperty("repositionRadius").floatValue = 8f;
        dstSo.FindProperty("minRepositionDistance").floatValue = 4f;
        dstSo.FindProperty("kneelReloadDuration").floatValue = 2f;
        dstSo.FindProperty("maxHealth").intValue = 10;
        dstSo.FindProperty("aimBone").objectReferenceValue = bossBones.TryGetValue("mixamorig:Spine2", out var chest) ? chest : null;
        // traits: dodge roll + battle cry smoke teleport (+1 soldier) + firing line from half health
        dstSo.FindProperty("rollDuration").floatValue = dive.length;
        dstSo.FindProperty("volleySoldierPrefab").objectReferenceValue = soldierPrefab; // the line's riflemen use the soldier model
        dstSo.FindProperty("volleyCount").intValue = 6;
        dstSo.FindProperty("soldiersPerTeleport").intValue = 1;
        dstSo.FindProperty("smokePrefab").objectReferenceValue = smoke;
        dstSo.FindProperty("volleySpawnSmokePrefab").objectReferenceValue = shortSmoke; // quick puff for the firing line appearing
        // melee rush: blow timing measured from the punch clip; the strike lasts until the clip hands back to Idle
        dstSo.FindProperty("meleeImpactTime").floatValue = impact;
        dstSo.FindProperty("meleeRange").floatValue = reach > 0f ? Mathf.Clamp(reach + 0.55f, 2.2f, 3.6f) : 3.2f; // the rifle's reach + the player's width
        dstSo.FindProperty("meleeDuration").floatValue = punch.length * 0.92f;
        dstSo.FindProperty("shotsPerMelee").intValue = 2;
        var cry = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderCryPath);
        var cries = dstSo.FindProperty("teleportCryClips");
        cries.arraySize = cry != null ? 1 : 0;
        if (cry != null) cries.GetArrayElementAtIndex(0).objectReferenceValue = cry;
        dstSo.ApplyModifiedPropertiesWithoutUndo();

        // report before the working copy is destroyed (destroyed objects read as null)
        log.AppendFormat("  prefab {0}: Goyo model + soldier kit (ragdoll on {1} bones, {2} joints, rifle {3}, muzzle {4}), GoyoBossAI targets [Player, Enemy], 10 health, repositions after every reload\n",
            PrefabPath, ragdollBones, joints.Count, gun != null ? "ok" : "MISSING", muzzle != null ? "ok" : "MISSING");
        log.AppendLine("  rifle angle: " + aimFix);
        log.AppendFormat("  melee: Rifle Punch lands {0:F2}s into the {1:F2}s clip (rifle reaches {2:F2} m ahead of him); rush after every 2 shots\n", impact, punch.length, reach);

        Object.DestroyImmediate(soldier);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) AssetDatabase.DeleteAsset(PrefabPath);
        var prefab = PrefabUtility.SaveAsPrefabAsset(boss, PrefabPath);
        Object.DestroyImmediate(boss);
        return prefab;
    }

    // Goyo's model was rigged with its hand bone turned differently from the soldier's (same names,
    // different axes), so the soldier's rifle offset tilts Goyo's rifle ~10 degrees up and ~14 sideways -
    // and the soldiers' aim offset (aimRotationOffsetDegrees) is tuned for the soldier's rifle angle.
    // Pose both in the same aim animation and turn Goyo's rifle to match the soldier's exactly. The
    // correction goes on a new "Aim Offset" between Gun Root and the rifle, because the recoil code
    // overwrites Gun Root's own rotation every frame.
    static string AlignRifleToSoldier(GameObject boss, GameObject soldier, Transform gunRoot)
    {
        var aim = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Entities/Animations/Real Anims/Aim.anim");
        var soldierGun = soldier.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Gun Root");
        var bossMuzzle = gunRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MuzzlePoint");
        var soldierMuzzle = soldierGun != null ? soldierGun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MuzzlePoint") : null;
        if (aim == null || soldierGun == null || bossMuzzle == null || soldierMuzzle == null) return "not aligned (aim clip or rifle parts missing)";

        var offset = new GameObject("Aim Offset").transform;
        offset.SetParent(gunRoot, false);
        foreach (var c in gunRoot.Cast<Transform>().Where(c => c != offset).ToList()) c.SetParent(offset, false);

        // same spot and facing, same frame of the aim clip
        boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        soldier.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        string Pitch(Transform from, Transform to)
        {
            Vector3 d = (to.position - from.position).normalized;
            return $"{Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg:F1} up / {Vector3.SignedAngle(Vector3.forward, new Vector3(d.x, 0f, d.z), Vector3.up):F1} side";
        }

        string before = "", target = "", after = "";
        Quaternion correction = Quaternion.identity;
        Sample(() =>
        {
            before = Pitch(gunRoot, bossMuzzle);
            target = Pitch(soldierGun, soldierMuzzle);
            correction = Quaternion.Inverse(gunRoot.rotation) * soldierGun.rotation; // boss rifle = soldier rifle orientation
        });
        offset.localRotation = correction; // applied outside the preview, which reverts what it posed
        Sample(() => after = Pitch(gunRoot, bossMuzzle));
        return $"barrel in aim pose was {before}, soldier's is {target}, now {after}";

        void Sample(System.Action measure)
        {
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(boss, aim, 0.5f);
                AnimationMode.SampleAnimationClip(soldier, aim, 0.5f);
                AnimationMode.EndSampling();
                measure();
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
        }
    }

    // When does the punch land? Step through the clip on the boss and find the moment the rifle's muzzle is
    // furthest out in front of him (the strike's full extension).
    static float MeasurePunchImpact(GameObject boss, Transform muzzle, AnimationClip punch, out float reach)
    {
        boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        float bestT = punch.length * 0.4f;
        reach = float.MinValue;
        AnimationMode.StartAnimationMode();
        try
        {
            for (float t = 0f; t <= punch.length; t += 0.02f)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(boss, punch, t);
                AnimationMode.EndSampling();
                float forward = boss.transform.InverseTransformPoint(muzzle.position).z;
                if (forward > reach) { reach = forward; bestT = t; }
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }
        return bestT;
    }

    static Component Paste(Component src, GameObject dst)
    {
        UnityEditorInternal.ComponentUtility.CopyComponent(src);
        UnityEditorInternal.ComponentUtility.PasteComponentAsNew(dst);
        var all = dst.GetComponents(src.GetType());
        return all[all.Length - 1];
    }

    static void SetStrings(SerializedProperty arr, params string[] values)
    {
        arr.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) arr.GetArrayElementAtIndex(i).stringValue = values[i];
    }

    // ---------------------------------------------------------------- test scene
    static void BuildTestScene(UnityEngine.SceneManagement.Scene scene, GameObject bossPrefab, StringBuilder log)
    {
        // the player brings its own camera
        var cam = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<Camera>() != null);
        if (cam != null) Object.DestroyImmediate(cam);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100 x 100 m

        // low walls to duck behind (Obstacle layer = what blocks the AI's line of sight), plus a few pillars
        var cover = new GameObject("Cover").transform;
        Vector3[] walls = { new Vector3(-6f, 0f, -4f), new Vector3(5f, 0f, -2f), new Vector3(-4f, 0f, 4f), new Vector3(8f, 0f, 6f), new Vector3(-9f, 0f, 8f) };
        foreach (var p in walls) Block("Low Wall", cover, p + Vector3.up * 0.6f, new Vector3(4f, 1.2f, 0.6f));
        // nothing on the straight line between the player start (0,-15) and the boss (0,15), so he can see you
        Vector3[] pillars = { new Vector3(4f, 0f, -8f), new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, -6f), new Vector3(6f, 0f, 10f) };
        foreach (var p in pillars) Block("Pillar", cover, p + Vector3.up * 1.5f, new Vector3(1.2f, 3f, 1.2f));

        var navGo = new GameObject("NavMesh Surface");
        var surface = navGo.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~LayerMask.GetMask("People", "Weapon", "UI", "Ignore Raycast");
        surface.BuildNavMesh();
        if (!AssetDatabase.IsValidFolder(TestSceneDir)) AssetDatabase.CreateFolder("Assets/Scenes", "Boss Test");
        string navPath = TestSceneDir + "/NavMesh-Boss Test.asset";
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath) != null) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);

        // scene kit from Level 2 Gameplay: rifle pickup, pause manager, event system (for the menu buttons)
        var level2 = EditorSceneManager.OpenScene(Level2ScenePath, OpenSceneMode.Additive);
        try
        {
            Selection.objects = level2.GetRootGameObjects().Where(g => SceneKit.Contains(g.name)).ToArray();
            Unsupported.CopyGameObjectsToPasteboard();
            EditorSceneManager.SetActiveScene(scene);
            Selection.objects = new Object[0];
            Unsupported.PasteGameObjectsFromPasteboard();
        }
        finally
        {
            EditorSceneManager.SetActiveScene(scene);
            EditorSceneManager.CloseScene(level2, true);
            Selection.objects = new Object[0];
        }
        var roots = scene.GetRootGameObjects();
        var rifle = roots.FirstOrDefault(g => g.name == "Dreyse Needle rifle");
        var pause = roots.FirstOrDefault(g => g.name == "PauseManager")?.GetComponent<PauseManager>();
        if (rifle != null) rifle.transform.SetPositionAndRotation(new Vector3(1.5f, 0.4f, -13.5f), Quaternion.Euler(-3.2f, -66f, 24f));

        // the player: the Level 2 player variant (health bar + Space dash), wired to this scene's rifle and pause manager
        var variant = PlayerVariantBuilder.EnsureVariant(log);
        string wiring = "no player variant";
        if (variant != null)
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(variant, scene);
            player.name = "Player";
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.1f, -15f), Quaternion.identity);
            wiring = PlayerVariantBuilder.WireIntoScene(player, rifle, pause);
        }
        roots = scene.GetRootGameObjects();

        var boss = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab, scene);
        boss.transform.SetPositionAndRotation(new Vector3(0f, 0f, 15f), Quaternion.LookRotation(Vector3.back));
        log.AppendLine("  reinforcements: " + BuildReinforcements(boss));

        bool onMesh = NavMesh.SamplePosition(boss.transform.position, out _, 0.5f, NavMesh.AllAreas);
        EditorSceneManager.SaveScene(scene, TestScenePath);
        log.AppendFormat("  test scene {0}: 100 m ground, {1} low walls + {2} pillars, NavMesh {3} triangles, from Level 2 [{4}], player = Level 2 variant ({5}), boss 30 m from the player (on NavMesh: {6})\n",
            TestScenePath, walls.Length, pillars.Length, NavMesh.CalculateTriangulation().indices.Length / 3,
            string.Join(", ", SceneKit.Where(n => roots.Any(r => r.name == n))), wiring, onMesh);
    }

    // His men for the half-health phase: spawn points around the edge of the map, facing the middle, and
    // an inactive Filipino soldier template set to shoot at the player, copied by a Level2WaveSpawner
    // that only spawns when the boss calls (runOwnWaves off). The boss in this scene is wired to it.
    static string BuildReinforcements(GameObject boss)
    {
        var root = new GameObject("Goyo Reinforcements");
        var pointsRoot = new GameObject("Spawn Points").transform;
        pointsRoot.SetParent(root.transform, false);
        Vector3[] spots = { new Vector3(-32f, 0f, 0f), new Vector3(32f, 0f, 0f), new Vector3(-26f, 0f, 28f),
                            new Vector3(26f, 0f, 28f), new Vector3(-26f, 0f, -28f), new Vector3(26f, 0f, -28f) };
        var points = new List<Transform>();
        foreach (var s in spots)
        {
            if (!NavMesh.SamplePosition(s, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            var p = new GameObject("Spawn Point " + points.Count).transform;
            p.SetParent(pointsRoot, false);
            Vector3 inward = -hit.position; inward.y = 0f;
            p.SetPositionAndRotation(hit.position, Quaternion.LookRotation(inward));
            points.Add(p);
        }

        var template = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SoldierPrefabPath), root.transform);
        template.name = "Filipino Reinforcement (copied by spawner)";
        var so = new SerializedObject(template.GetComponent<EnemyAI>());
        SetStrings(so.FindProperty("targetTags"), "Player");
        // less deadly than a normal soldier against the player: only ReinforcementPlayerHitChance of their
        // shots land (a default soldier's always do), and a slower aim gives the player time against five
        so.FindProperty("playerHitChance").floatValue = ReinforcementPlayerHitChance;
        so.FindProperty("aimDurationMin").floatValue = 1.0f;
        so.FindProperty("aimDurationMax").floatValue = 1.6f;
        so.FindProperty("detectionRadius").floatValue = 35f;
        so.ApplyModifiedPropertiesWithoutUndo();
        var follower = template.GetComponent<AllyPathFollower>();
        if (follower != null)
        {
            var fso = new SerializedObject(follower);
            fso.FindProperty("followOnStart").boolValue = false; // no path here - they hunt the player
            fso.ApplyModifiedPropertiesWithoutUndo();
        }
        template.SetActive(false);

        var spawned = new GameObject("Spawned").transform;
        spawned.SetParent(root.transform, false);
        var spawner = root.AddComponent<Level2WaveSpawner>();
        var sso = new SerializedObject(spawner);
        sso.FindProperty("template").objectReferenceValue = template;
        sso.FindProperty("container").objectReferenceValue = spawned;
        var arr = sso.FindProperty("spawnPoints");
        arr.arraySize = points.Count;
        for (int i = 0; i < points.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        sso.FindProperty("runOwnWaves").boolValue = false; // only when the boss calls
        sso.FindProperty("maxAlive").intValue = 5;
        sso.FindProperty("spawnInterval").floatValue = 0.4f;
        sso.FindProperty("spawnJitter").floatValue = 0.8f;
        sso.ApplyModifiedPropertiesWithoutUndo();

        var bso = new SerializedObject(boss.GetComponent<GoyoBossAI>());
        bso.FindProperty("reinforcementSpawner").objectReferenceValue = spawner;
        bso.FindProperty("reinforcementCount").intValue = 5;
        bso.ApplyModifiedPropertiesWithoutUndo();
        return $"{points.Count} spawn points around the map, 5 Filipino soldiers targeting the player at half health ({ReinforcementPlayerHitChance:P0} of their shots land), boss wired";
    }

    static void Block(string name, Transform parent, Vector3 pos, Vector3 size)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name;
        b.layer = ObstacleLayer;
        b.transform.SetParent(parent, false);
        b.transform.position = pos;
        b.transform.localScale = size;
    }
}
#endif
