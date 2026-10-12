using System.Linq;
using TiradPass;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Level2EndingSetup
{
    [MenuItem("Tools/Tirad Pass/Configure Level 2 Ending")]
    public static void Configure()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Level 2 Ending Cutscene.unity")
            throw new System.InvalidOperationException("Open Level 2 Ending Cutscene first.");
        var existing = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Level2EndingCutscene>(true)).FirstOrDefault();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("Level 2 ending is already configured. Selected its controller.", existing);
            return;
        }
        var old = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<level2IntroCutscene>(true)).FirstOrDefault();
        if (old == null) old = CreateReferenceSource(scene);
        var data = new SerializedObject(old);
        if (Ref<CanvasGroup>(data, "screenFadeCanvasGroup") == null || Ref<TextMeshProUGUI>(data, "dialogueContentText") == null)
            throw new System.InvalidOperationException("Assign the intro controller's Screen Fade Canvas Group and Dialogue Content Text before configuring the ending.");
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Configure Level 2 Ending");
        var ending = Undo.AddComponent<Level2EndingCutscene>(old.gameObject);
        for (var parent = ending.transform; parent != null; parent = parent.parent)
        {
            Undo.RecordObject(parent.gameObject, "Activate ending controller hierarchy");
            parent.gameObject.SetActive(true);
        }
        ending.mainCamera = Ref<Camera>(data, "mainCamera");
        ending.goyoCamTarget = Ref<Transform>(data, "goyoCamTarget");
        ending.mateoCamTarget = Ref<Transform>(data, "mateoCamTarget");
        ending.aftermathCamTarget = Ref<Transform>(data, "openingHorizonTarget");
        ending.speakerNameText = Ref<TextMeshProUGUI>(data, "speakerNameText");
        ending.dialogueContentText = Ref<TextMeshProUGUI>(data, "dialogueContentText");
        ending.dialogueCanvasGroup = Ref<CanvasGroup>(data, "dialogueCanvasGroup");
        ending.screenFadeCanvasGroup = Ref<CanvasGroup>(data, "screenFadeCanvasGroup");
        ending.skipButton = Ref<UnityEngine.UI.Button>(data, "skipButton");
        ending.screenFadeImage = ending.screenFadeCanvasGroup.GetComponentInChildren<UnityEngine.UI.Image>(true);
        if (ending.skipButton != null)
        {
            Undo.RecordObject(ending.skipButton, "Clear copied skip callbacks");
            for (int i = ending.skipButton.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(ending.skipButton.onClick, i);
        }
        var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach (var behaviour in all.SelectMany(t => t.GetComponents<MonoBehaviour>()))
        {
            if (behaviour is EnemyAI || behaviour is FirstPersonController || behaviour is GameOutcomeManager)
            {
                Undo.RecordObject(behaviour, "Disable gameplay during ending");
                behaviour.enabled = false;
            }
        }
        ending.goyo = all.FirstOrDefault(t => t.name == "Filipino Soldier_Goyo");
        ending.mateo = all.FirstOrDefault(t => t.name == "mateo");
        ending.defenders = all.Where(t => t.name.StartsWith("Filipino Soldier") && t != ending.goyo
            && !HasSoldierParent(t)).ToArray();
        if (ending.goyo != null)
            ending.barricades = all.Where(t => t.name.StartsWith("Sandbag") && t.GetComponent<Renderer>() != null)
                .OrderBy(t => Vector3.SqrMagnitude(t.position - ending.goyo.position)).Take(8).ToArray();
        var firing = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Entities/Animations/Real Anims/Firing Standing.anim");
        const string controllerPath = "Assets/Entities/Animations/Level2EndingFiring.controller";
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
        if (firing != null && controller == null)
        {
            controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Last stand firing");
            state.motion = firing;
            controller.layers[0].stateMachine.defaultState = state;
        }
        var americans = Ref<GameObject>(data, "americansRunningObject");
        if (americans != null) { Undo.RecordObject(americans, "Hide intro running group"); americans.SetActive(false); }
        foreach (var actor in ending.defenders.Concat(new[] { ending.goyo, ending.mateo }).Where(t => t != null))
        {
            Undo.RecordObject(actor.gameObject, "Activate ending defender");
            actor.gameObject.SetActive(true);
            if (controller != null && actor != ending.goyo)
            {
                foreach (var animator in actor.GetComponentsInChildren<Animator>(true))
                {
                    Undo.RecordObject(animator, "Stage last stand firing");
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                }
            }
            foreach (var behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Undo.RecordObject(behaviour, "Disable gameplay during cinematic");
                behaviour.enabled = false;
            }
        }
        // Keep original intro audio clips out of this ending; no substitute spoken audio.
        foreach (var audio in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(audio, "Stop copied intro audio autoplay");
            audio.playOnAwake = false;
            audio.Stop();
        }
        BuildCard(ending);
        BuildSmoke(ending);
        var audioGO = new GameObject("EndingAudio");
        Undo.RegisterCreatedObjectUndo(audioGO, "Create ending audio");
        ending.battleAudio = audioGO.AddComponent<AudioSource>();
        ending.battleAudio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/audios/alexgrohl-war-535028.mp3");
        ending.battleAudio.playOnAwake = false;
        ending.battleAudio.volume = 0.3f;
        ending.battleAudio.loop = true;
        if (ending.goyo != null && ending.mateo != null && ending.goyoCamTarget != null)
        {
            var aftermath = new GameObject("CM_endingAftermath");
            Undo.RegisterCreatedObjectUndo(aftermath, "Create aftermath camera marker");
            var focus = (ending.goyo.position + ending.mateo.position) * 0.5f;
            aftermath.transform.position = ending.goyoCamTarget.position - ending.goyoCamTarget.forward * 6f + Vector3.up * 3f;
            aftermath.transform.LookAt(focus + Vector3.up * 0.4f);
            ending.aftermathCamTarget = aftermath.transform;
        }
        // Disabled MonoBehaviours still receive Awake; remove the copied controller so
        // its Awake cannot re-register the intro skip handler or reset the ending UI.
        Undo.DestroyObjectImmediate(old);
        EditorUtility.SetDirty(ending);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = ending.gameObject;
        Debug.Log($"Ending configured: Goyo={ending.goyo}, Mateo={ending.mateo}, defenders={ending.defenders.Length}. Save the scene after review.");
    }

    private static bool HasSoldierParent(Transform transform)
    {
        for (var parent = transform.parent; parent != null; parent = parent.parent)
            if (parent.name.StartsWith("Filipino Soldier")) return true;
        return false;
    }

    private static T Ref<T>(SerializedObject data, string name) where T : Object
        => data.FindProperty(name).objectReferenceValue as T;

    // Rebuild reference wiring when the copied intro component was removed.
    // This temporary component is removed by Configure after its references are copied.
    private static level2IntroCutscene CreateReferenceSource(Scene scene)
    {
        var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var host = new GameObject("Level2EndingController");
        Undo.RegisterCreatedObjectUndo(host, "Create ending controller");
        var source = Undo.AddComponent<level2IntroCutscene>(host);
        source.enabled = false;
        var data = new SerializedObject(source);
        System.Action<string, Object> assign = (name, value) => data.FindProperty(name).objectReferenceValue = value;
        assign("mainCamera", all.Select(t => t.GetComponent<Camera>()).FirstOrDefault(c => c != null && c.CompareTag("MainCamera"))
            ?? all.Select(t => t.GetComponent<Camera>()).FirstOrDefault(c => c != null));
        assign("goyoCamTarget", all.FirstOrDefault(t => t.name == "CM_goyo"));
        assign("mateoCamTarget", all.FirstOrDefault(t => t.name == "CM_mateo"));
        assign("openingHorizonTarget", all.FirstOrDefault(t => t.name == "CM_goyo"));

        var canvasGO = UI("EndingDialogueCanvas", null, typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.GetComponent<Canvas>().sortingOrder = 90;
        var scaler = canvasGO.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var groupGO = UI("EndingDialogue", canvasGO.transform, typeof(CanvasGroup));
        var groupRect = groupGO.GetComponent<RectTransform>();
        groupRect.anchorMin = Vector2.zero; groupRect.anchorMax = Vector2.one;
        groupRect.offsetMin = groupRect.offsetMax = Vector2.zero;
        assign("dialogueCanvasGroup", groupGO.GetComponent<CanvasGroup>());
        var font = all.Select(t => t.GetComponent<TextMeshProUGUI>()).FirstOrDefault(t => t != null);
        foreach (var field in new[] { "speakerNameText", "dialogueContentText" })
        {
            var textGO = UI(field, groupGO.transform, typeof(TextMeshProUGUI));
            var text = textGO.GetComponent<TextMeshProUGUI>();
            if (font != null) text.font = font.font;
            text.fontSize = field == "speakerNameText" ? 36 : 40;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            var rect = textGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.12f, field == "speakerNameText" ? 0.23f : 0.08f);
            rect.anchorMax = new Vector2(0.88f, field == "speakerNameText" ? 0.3f : 0.22f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            assign(field, text);
        }
        var fadeGO = UI("EndingScreenFade", canvasGO.transform, typeof(CanvasGroup), typeof(UnityEngine.UI.Image));
        var fadeRect = fadeGO.GetComponent<RectTransform>();
        fadeRect.anchorMin = Vector2.zero; fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = fadeRect.offsetMax = Vector2.zero;
        fadeGO.GetComponent<UnityEngine.UI.Image>().color = Color.black;
        fadeGO.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        assign("screenFadeCanvasGroup", fadeGO.GetComponent<CanvasGroup>());
        var skipGO = UI("EndingSkipButton", canvasGO.transform, typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        var skipRect = skipGO.GetComponent<RectTransform>();
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = Vector2.one;
        skipRect.anchoredPosition = new Vector2(-40, -40); skipRect.sizeDelta = new Vector2(180, 60);
        skipGO.GetComponent<UnityEngine.UI.Image>().color = new Color(0.2f, 0.18f, 0.14f);
        assign("skipButton", skipGO.GetComponent<UnityEngine.UI.Button>());
        var labelGO = UI("Label", skipGO.transform, typeof(TextMeshProUGUI));
        var label = labelGO.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font.font;
        label.text = "SKIP"; label.fontSize = 28; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        // Hide copied dialogue/fade UI so it cannot cover the replacement canvas.
        foreach (var t in all.Where(t => t.name == "dialoguetext" || t.name == "speakertext" || t.name == "scenefade" || t.name == "skipButton"))
        {
            Undo.RecordObject(t.gameObject, "Hide superseded intro UI");
            t.gameObject.SetActive(false);
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        return source;
    }

    private static GameObject UI(string name, Transform parent, params System.Type[] components)
    {
        var go = new GameObject(name, new[] { typeof(RectTransform) }.Concat(components).ToArray());
        Undo.RegisterCreatedObjectUndo(go, "Create ending UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void BuildCard(Level2EndingCutscene ending)
    {
        var go = UI("EndingCardCanvas", null, typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(CanvasGroup));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        ending.endingCard = go.GetComponent<CanvasGroup>();
        ending.endingCard.alpha = 0f;
        ending.endingCard.interactable = false;
        ending.endingCard.blocksRaycasts = false;
        var background = UI("BlackBackground", go.transform, typeof(UnityEngine.UI.Image));
        var rect = background.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        background.GetComponent<UnityEngine.UI.Image>().color = Color.black;
        var textGO = UI("StoryText", go.transform, typeof(TextMeshProUGUI));
        rect = textGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.15f, 0.3f); rect.anchorMax = new Vector2(0.85f, 0.85f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        ending.endingText = textGO.GetComponent<TextMeshProUGUI>();
        ending.endingText.font = ending.dialogueContentText.font;
        ending.endingText.fontSize = 42;
        ending.endingText.alignment = TextAlignmentOptions.Center;
        ending.endingText.color = new Color(0.92f, 0.89f, 0.81f);
        ending.endingText.raycastTarget = false;
        ending.endingText.text = "THE LAST STAND\n\n" + ending.finalMessage + "\n\nLEVEL COMPLETE\nDefeat is part of the story.";
        var buttonGO = UI("ReturnToMenu", go.transform, typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        rect = buttonGO.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.17f); rect.sizeDelta = new Vector2(400, 72);
        buttonGO.GetComponent<UnityEngine.UI.Image>().color = new Color(0.23f, 0.2f, 0.15f);
        ending.menuButton = buttonGO.GetComponent<UnityEngine.UI.Button>();
        var labelGO = UI("Label", buttonGO.transform, typeof(TextMeshProUGUI));
        rect = labelGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var label = labelGO.GetComponent<TextMeshProUGUI>();
        label.font = ending.endingText.font; label.text = "RETURN TO MENU"; label.fontSize = 30;
        label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
    }

    private static void BuildSmoke(Level2EndingCutscene ending)
    {
        var go = new GameObject("EndingBattleSmoke", typeof(ParticleSystem));
        Undo.RegisterCreatedObjectUndo(go, "Create battle smoke");
        go.transform.position = ending.goyo != null ? ending.goyo.position + Vector3.forward * 4f : Vector3.zero;
        ending.smoke = go.GetComponent<ParticleSystem>();
        var main = ending.smoke.main;
        main.playOnAwake = false; main.startLifetime = 8f; main.startSpeed = 0.5f;
        main.startSize = 4f; main.startColor = new Color(0.35f, 0.34f, 0.32f, 0.18f);
        main.maxParticles = 100; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = ending.smoke.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(15, 1, 8);
        var emission = ending.smoke.emission; emission.rateOverTime = 8f;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null)
        {
            const string path = "Assets/Entities/EndingSmoke.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f); material.renderQueue = 3000;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
                AssetDatabase.CreateAsset(material, path);
            }
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        }
        ending.smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
