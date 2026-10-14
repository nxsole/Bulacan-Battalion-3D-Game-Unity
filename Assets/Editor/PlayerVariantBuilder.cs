// Tools > Player > Build Level 2 player variant
// Saves Level 2's player - the Player prefab plus everything Level 2 gives it: the copied Level 1
// overrides, pause menu and fade-in, Level2Sprint (Shift) and the steadier aim - as a prefab variant
// of Player.prefab, then adds:
//   - PlayerDash: Space dashes in the direction you're moving
//   - a health bar (PlayerHealthBar) at the bottom-left of the player's screen
//   - a boss health bar (BossHealthBar) at the top centre, shown only while a living boss is around
//   - PlayerBossDamage: lets the rifle hurt the Goyo boss (the player script only damages EnemyAI)
// Neither Player.prefab nor Level 2's player is changed; Level 2 is opened read-only.
//
// Links from a prefab to objects in a scene can't be stored in the prefab, so the variant loses the
// ones Level 2's player had to its scene: the rifle pickup (and its "Press E" prompt) and the pause
// manager behind the pause menu's buttons. WireIntoScene() restores them wherever the variant is placed.
#if UNITY_EDITOR
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class PlayerVariantBuilder
{
    public const string VariantPath = "Assets/Entities/Pinoy/Player Level 2 Variant.prefab";
    const string Level2ScenePath = "Assets/Scenes/Level 2 Gameplay.unity";
    const string PlayerName = "Player";

    [MenuItem("Tools/Player/Build Level 2 player variant")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new StringBuilder("Level 2 player variant\n");
        Build(log);
        Debug.Log(log.ToString());
    }

    public static void BuildBatch()
    {
        var log = new StringBuilder("Level 2 player variant\n");
        Build(log);
        Debug.Log(log.ToString());
    }

    // Existing variant: kept, with its features brought up to date (dash, health bars, boss damage).
    // No variant yet: built from Level 2's player.
    public static GameObject EnsureVariant(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null) return Build(log);
        AddFeatures(log, "existing, kept");
        return AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
    }

    public static GameObject Build(StringBuilder log)
    {
        // 1. Level 2's player, saved as a variant of Player.prefab (keeps all its overrides and additions)
        var active = EditorSceneManager.GetActiveScene();
        var level2 = EditorSceneManager.OpenScene(Level2ScenePath, OpenSceneMode.Additive);
        try
        {
            var src = level2.GetRootGameObjects().FirstOrDefault(g => g.name == PlayerName);
            if (src == null || !PrefabUtility.IsAnyPrefabInstanceRoot(src))
            {
                log.AppendLine("  player variant: no Player prefab instance in " + Level2ScenePath);
                return null;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) != null) AssetDatabase.DeleteAsset(VariantPath);
            PrefabUtility.SaveAsPrefabAsset(src, VariantPath); // an instance root saves as a variant of its prefab
        }
        finally
        {
            if (active.IsValid() && active != level2) EditorSceneManager.SetActiveScene(active);
            EditorSceneManager.CloseScene(level2, true); // never saved
        }

        // 2. add the variant's own features
        AddFeatures(log, "variant of Player.prefab from Level 2's player");
        return AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
    }

    // Adds (or refreshes) everything the variant has on top of Level 2's player. Components are added
    // if missing and keep their Inspector settings; the two HUD bars are rebuilt.
    static void AddFeatures(StringBuilder log, string what)
    {
        var root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            var fpc = root.GetComponent<FirstPersonController>();
            var fso = new SerializedObject(fpc);
            var deathScreen = fso.FindProperty("deathScreenCanvasGroup").objectReferenceValue as CanvasGroup;

            var dash = root.GetComponent<PlayerDash>() ?? root.AddComponent<PlayerDash>();
            var dso = new SerializedObject(dash);
            dso.FindProperty("player").objectReferenceValue = fpc;
            dso.FindProperty("deathScreen").objectReferenceValue = deathScreen;
            dso.FindProperty("audioSource").objectReferenceValue = root.GetComponent<AudioSource>();
            dso.ApplyModifiedPropertiesWithoutUndo();

            var damage = root.GetComponent<PlayerBossDamage>() ?? root.AddComponent<PlayerBossDamage>();
            var bso = new SerializedObject(damage);
            bso.FindProperty("player").objectReferenceValue = fpc;
            bso.ApplyModifiedPropertiesWithoutUndo();

            var canvas = root.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c => c.name == "UI") ?? root.GetComponentInChildren<Canvas>(true);
            var font = root.GetComponentsInChildren<TMP_Text>(true).Select(t => t.font).FirstOrDefault(f => f != null);
            BuildHealthBar(canvas.transform, fpc, font);
            BuildBossHealthBar(canvas.transform, font);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            log.AppendFormat("  player variant: {0} ({1}) - Space dash, health bar, boss health bar and boss damage on '{2}'\n", VariantPath, what, canvas.name);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // Top centre: boss name over a bar with a pale damage trail. Hidden until a living boss is in the scene.
    static void BuildBossHealthBar(Transform canvas, TMP_FontAsset font)
    {
        var old = canvas.Find("Boss Health Bar");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject("Boss Health Bar", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        root.gameObject.layer = canvas.gameObject.layer;
        root.SetParent(canvas, false);
        root.SetSiblingIndex(1); // with the health bar, under the vignette/death screen/pause menu
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -30f);
        root.sizeDelta = new Vector2(640f, 56f);
        root.GetComponent<CanvasGroup>().blocksRaycasts = false;

        var label = Child("Name", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 30f));
        var t = label.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = "GEN. GREGORIO DEL PILAR";
        t.fontSize = 24f;
        t.alignment = TextAlignmentOptions.Bottom;
        t.color = new Color(1f, 0.92f, 0.7f, 1f);
        t.raycastTarget = false;
        if (font != null) t.font = font;

        var track = Child("Track", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 18f));
        Img(track, new Color(0f, 0f, 0f, 0.55f));
        var trail = Child("Trail", track, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        Img(trail, new Color(1f, 0.9f, 0.75f, 0.85f));
        var fill = Child("Fill", track, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        Img(fill, new Color(0.7f, 0.12f, 0.1f, 1f));

        var bar = root.gameObject.AddComponent<BossHealthBar>();
        var so = new SerializedObject(bar);
        so.FindProperty("fill").objectReferenceValue = fill;
        so.FindProperty("trail").objectReferenceValue = trail;
        so.FindProperty("nameText").objectReferenceValue = t;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static RectTransform Child(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    static void Img(RectTransform rt, Color colour)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = colour;
        img.raycastTarget = false;
    }

    static void BuildHealthBar(Transform canvas, FirstPersonController fpc, TMP_FontAsset font)
    {
        var old = canvas.Find("Health Bar");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var bar = Rect("Health Bar", canvas, new Vector2(40f, 40f), new Vector2(260f, 26f));
        bar.SetAsFirstSibling(); // under the vignette/death screen/pause menu so those cover it
        var bg = bar.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);
        bg.raycastTarget = false;

        var segs = Rect("Segments", bar, Vector2.zero, Vector2.zero);
        segs.anchorMin = Vector2.zero; segs.anchorMax = Vector2.one;
        segs.offsetMin = new Vector2(4f, 4f); segs.offsetMax = new Vector2(-4f, -4f);
        var layout = segs.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        var label = Rect("Label", bar, new Vector2(0f, 30f), new Vector2(260f, 26f));
        var t = label.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = "HEALTH";
        t.fontSize = 20f;
        t.alignment = TextAlignmentOptions.BottomLeft;
        t.color = new Color(1f, 1f, 1f, 0.85f);
        t.raycastTarget = false;
        if (font != null) t.font = font;

        var hb = bar.gameObject.AddComponent<PlayerHealthBar>();
        var so = new SerializedObject(hb);
        so.FindProperty("player").objectReferenceValue = fpc;
        so.FindProperty("segmentContainer").objectReferenceValue = segs;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // bottom-left anchored rect
    static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    // ---------------------------------------------------------------- placing the variant in a scene
    // Restores the scene links a prefab can't hold: the rifle pickup + its prompt, and the pause
    // manager (its menu panel/player, and the pause menu's Continue/Quit buttons).
    public static string WireIntoScene(GameObject playerInstance, GameObject rifle, PauseManager pause)
    {
        var report = new StringBuilder();
        var fpc = playerInstance.GetComponent<FirstPersonController>();
        if (rifle != null)
        {
            var prompt = rifle.GetComponentsInChildren<CanvasGroup>(true).FirstOrDefault(c => c.name == "Prompt");
            var so = new SerializedObject(fpc);
            so.FindProperty("weaponPickupObject").objectReferenceValue = rifle;
            so.FindProperty("interactionPromptCanvasGroup").objectReferenceValue = prompt;
            so.FindProperty("interactionPromptText").objectReferenceValue = prompt != null ? prompt.GetComponentInChildren<TMP_Text>(true) : null;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.Append("rifle pickup " + (prompt != null ? "+ prompt " : "") + "linked");
        }
        if (pause != null)
        {
            var menu = playerInstance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PauseMenu");
            var pso = new SerializedObject(pause);
            pso.FindProperty("pauseMenuPanel").objectReferenceValue = menu != null ? menu.gameObject : null;
            pso.FindProperty("playerController").objectReferenceValue = fpc;
            pso.ApplyModifiedPropertiesWithoutUndo();
            int buttons = 0;
            if (menu != null)
                foreach (var b in menu.GetComponentsInChildren<Button>(true))
                {
                    for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
                    if (b.name.ToLowerInvariant().Contains("continue")) { UnityEventTools.AddPersistentListener(b.onClick, pause.ResumeGame); buttons++; }
                    else if (b.name.ToLowerInvariant().Contains("quit")) { UnityEventTools.AddPersistentListener(b.onClick, pause.QuitToMainMenu); buttons++; }
                    EditorUtility.SetDirty(b);
                }
            report.Append((report.Length > 0 ? ", " : "") + $"pause manager linked ({buttons} menu buttons)");
        }
        return report.ToString();
    }
}
#endif
