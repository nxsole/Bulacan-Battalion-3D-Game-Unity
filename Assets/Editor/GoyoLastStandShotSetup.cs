using System.Linq;
using TiradPass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GoyoLastStandShotSetup
{
    [InitializeOnLoadMethod]
    private static void PendingSetup()
    {
        EditorApplication.delayCall += () =>
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || scene.path != "Assets/Scenes/Level 2 Ending Cutscene.unity") return;
            if (scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<GoyoLastStandShot>(true) != null)) return;
            Configure();
        };
    }

    [MenuItem("Tools/Tirad Pass/Set Up Goyo Tracking Shot")]
    public static void Configure()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/Level 2 Ending Cutscene.unity")
        { Debug.LogError("Open Level 2 Ending Cutscene outside Play Mode first."); return; }
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var existing = all.Select(t => t.GetComponent<GoyoLastStandShot>()).FirstOrDefault(s => s != null);
        if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
        var goyo = all.FirstOrDefault(t => t.name == "Filipino Soldier_Goyo");
        var camera = all.Select(t => t.GetComponent<Camera>()).FirstOrDefault(c => c != null && c.CompareTag("MainCamera"));
        if (goyo == null || camera == null) { Debug.LogError("Could not find Goyo and the main camera. No scene changes made."); return; }
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Build Goyo low-angle tracking shot");
        var host = new GameObject("GoyoLastStandTrackingShot");
        Undo.RegisterCreatedObjectUndo(host, "Create shot controller");
        var shot = Undo.AddComponent<GoyoLastStandShot>(host);
        shot.shotCamera = camera; shot.goyo = goyo;
        foreach (var behaviour in all.SelectMany(t => t.GetComponents<MonoBehaviour>()).Where(b => b != null))
        {
            if (behaviour is level2IntroCutscene || behaviour is Level2EndingCutscene || behaviour is EnemyAI
                || behaviour is FirstPersonController || behaviour is GameOutcomeManager
                || behaviour.GetType().Name.Contains("CinemachineBrain"))
            { Undo.RecordObject(behaviour, "Disable competing cinematic/gameplay controller"); behaviour.enabled = false; }
        }
        // Remove the copied intro so its Awake cannot reset the shot or bind Skip.
        foreach (var intro in all.Select(t => t.GetComponent<level2IntroCutscene>()).Where(c => c != null))
        {
            var data = new SerializedObject(intro);
            shot.fade = data.FindProperty("screenFadeCanvasGroup").objectReferenceValue as CanvasGroup;
            foreach (var field in new[] { "dialogueCanvasGroup", "skipButton", "americansRunningObject" })
            {
                var value = data.FindProperty(field).objectReferenceValue;
                var target = value is Component component ? component.gameObject : value as GameObject;
                if (target != null) { Undo.RecordObject(target, "Hide intro-only elements"); target.SetActive(false); }
            }
            Undo.DestroyObjectImmediate(intro);
        }
        foreach (var audio in all.SelectMany(t => t.GetComponents<AudioSource>()))
        { Undo.RecordObject(audio, "Disable intro audio autoplay"); audio.playOnAwake = false; }
        var ground = goyo.position;
        Undo.RecordObject(goyo, "Elevate Goyo on rocks");
        goyo.position = ground + Vector3.up * 1.6f;
        goyo.gameObject.SetActive(true);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        const string rockPath = "Assets/Entities/GoyoShotRock.mat";
        var rockMaterial = AssetDatabase.LoadAssetAtPath<Material>(rockPath);
        if (rockMaterial == null && shader != null)
        {
            rockMaterial = new Material(shader); rockMaterial.SetColor("_BaseColor", new Color(0.25f, 0.26f, 0.25f));
            rockMaterial.SetFloat("_Smoothness", 0.05f); AssetDatabase.CreateAsset(rockMaterial, rockPath);
        }
        var rocks = new GameObject("GoyoElevatedRocks");
        Undo.RegisterCreatedObjectUndo(rocks, "Create rock staging");
        for (int i = 0; i < 5; i++)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(rock, "Create boulder"); rock.name = "Boulder " + (i + 1); rock.transform.SetParent(rocks.transform);
            float angle = i * Mathf.PI * 0.5f;
            rock.transform.position = ground + new Vector3(Mathf.Cos(angle) * (i == 0 ? 0f : 1.1f), 0.45f, Mathf.Sin(angle) * 0.9f);
            rock.transform.localScale = i == 0 ? new Vector3(2.8f, 2.3f, 2.4f) : new Vector3(2f, 1.5f, 1.8f);
            if (rockMaterial != null) rock.GetComponent<Renderer>().sharedMaterial = rockMaterial;
        }
        shot.trackingStart = Marker("TrackingStart", host.transform, ground - goyo.forward * 10f - goyo.right * 4f + Vector3.up * 0.7f);
        shot.trackingEnd = Marker("TrackingEnd", host.transform, ground - goyo.forward * 9f + goyo.right * 4f + Vector3.up * 0.9f);
        shot.barricadePieces = all.Where(t => t.name.StartsWith("Sandbag") && t.GetComponent<Renderer>() != null)
            .OrderBy(t => (t.position - ground).sqrMagnitude).Take(12).ToArray();
        var smokeGO = new GameObject("PassBattleSmoke", typeof(ParticleSystem));
        Undo.RegisterCreatedObjectUndo(smokeGO, "Create smoke"); smokeGO.transform.SetParent(host.transform);
        smokeGO.transform.position = ground + goyo.forward * 5f;
        shot.battleSmoke = smokeGO.GetComponent<ParticleSystem>();
        var main = shot.battleSmoke.main; main.playOnAwake = false; main.startLifetime = 10f; main.startSpeed = 0.45f;
        main.startSize = 5f; main.startColor = new Color(0.35f, 0.36f, 0.36f, 0.15f);
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 160;
        var emission = shot.battleSmoke.emission; emission.rateOverTime = 10f;
        var shape = shot.battleSmoke.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(20f, 1f, 10f);
        var smokeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Entities/EndingSmoke.mat");
        if (smokeMaterial == null)
        {
            var smokeShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (smokeShader != null)
            {
                smokeMaterial = new Material(smokeShader);
                smokeMaterial.SetFloat("_Surface", 1f);
                smokeMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                smokeMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                smokeMaterial.SetFloat("_ZWrite", 0f); smokeMaterial.renderQueue = 3000;
                smokeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                smokeMaterial.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
                AssetDatabase.CreateAsset(smokeMaterial, "Assets/Entities/EndingSmoke.mat");
            }
        }
        if (smokeMaterial != null) smokeGO.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMaterial;
        shot.battleSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Undo.RecordObject(camera, "Frame low-angle wide shot");
        Undo.RecordObject(camera.transform, "Frame low-angle wide shot");
        camera.fieldOfView = 65f; camera.transform.position = shot.trackingStart.position;
        camera.transform.LookAt(goyo.position + Vector3.up * 1.25f);
        EditorUtility.SetDirty(shot); EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene); Selection.activeGameObject = host;
        Debug.Log("Goyo tracking shot configured and saved. Enter Play Mode to preview 14-second shot.", shot);
    }

    private static Transform Marker(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Create tracking marker");
        go.transform.SetParent(parent); go.transform.position = position; return go.transform;
    }
}
