// Tools > Levels > Build Main Menu Level Select
// Adds LEVEL 1 / LEVEL 2 / LEVEL 3 / BACK buttons to the main menu and rewires the Play button to open
// them instead of starting the Level 1 intro straight away. Safe to run again - it rebuilds the buttons.
using TiradPass;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class MainMenuLevelSelectSetup
{
    const string MainMenu = "Assets/Scenes/main menu.unity";
    const string FontPath = "Assets/TextMesh Pro/Fonts/JenrivTitling-Regular SDF.asset";
    const string GroupName = "LevelSelect";

    // Matches the white text / yellow glow of the existing menu button images.
    static readonly Color Normal = Color.white;
    static readonly Color Hover = new Color(0.95f, 0.9f, 0.28f);
    static readonly Color Pressed = new Color(0.78f, 0.78f, 0.78f);

    [MenuItem("Tools/Levels/Build Main Menu Level Select")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Level select", "Stop Play mode first.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string back = EditorSceneManager.GetActiveScene().path;

        var scene = EditorSceneManager.OpenScene(MainMenu, OpenSceneMode.Single);

        var intro = Object.FindAnyObjectByType<TiradPassIntroCutscene>(FindObjectsInactive.Include);
        var panel = Find("MainMenuPanel");
        var play = Find("startButton");
        var settings = Find("settingsButton");
        var exit = Find("exitButton");
        if (intro == null || panel == null || play == null || settings == null || exit == null)
        {
            EditorUtility.DisplayDialog("Level select", "Couldn't find the intro cutscene, MainMenuPanel, startButton, settingsButton or exitButton in the main menu.", "OK");
            return;
        }

        // Rebuild from scratch on re-runs.
        var old = panel.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var menu = panel.GetComponent<LevelSelectMenu>();
        if (menu == null) menu = panel.gameObject.AddComponent<LevelSelectMenu>();

        var group = new GameObject(GroupName, typeof(RectTransform));
        group.layer = panel.gameObject.layer;
        var groupRt = (RectTransform)group.transform;
        groupRt.SetParent(panel, false);
        groupRt.anchorMin = Vector2.zero;
        groupRt.anchorMax = Vector2.one;
        groupRt.offsetMin = groupRt.offsetMax = Vector2.zero;

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        var playRt = (RectTransform)play;
        float x = playRt.anchoredPosition.x;
        float y = playRt.anchoredPosition.y;

        AddButton(group.transform, playRt, play, font, "LEVEL 1", new Vector2(x, y), menu.PlayLevel1);
        AddButton(group.transform, playRt, play, font, "LEVEL 2", new Vector2(x, y - 90f), menu.PlayLevel2);
        AddButton(group.transform, playRt, play, font, "LEVEL 3", new Vector2(x, y - 180f), menu.PlayLevel3);
        AddButton(group.transform, playRt, play, font, "BACK", new Vector2(x, y - 290f), menu.CloseLevelSelect);
        group.SetActive(false);

        // Play now opens the level select (Level 1's intro is started from the LEVEL 1 button instead).
        var playButton = play.GetComponent<Button>();
        RemoveListeners(playButton.onClick, "PlayCutscene", "OpenLevelSelect");
        UnityEventTools.AddPersistentListener(playButton.onClick, menu.OpenLevelSelect);
        EditorUtility.SetDirty(playButton);

        var introSo = new SerializedObject(intro);
        var so = new SerializedObject(menu);
        var buttons = so.FindProperty("mainButtons");
        buttons.arraySize = 3;
        buttons.GetArrayElementAtIndex(0).objectReferenceValue = play.gameObject;
        buttons.GetArrayElementAtIndex(1).objectReferenceValue = settings.gameObject;
        buttons.GetArrayElementAtIndex(2).objectReferenceValue = exit.gameObject;
        so.FindProperty("levelSelectGroup").objectReferenceValue = group;
        so.FindProperty("level1Intro").objectReferenceValue = intro;
        so.FindProperty("screenFadeCanvasGroup").objectReferenceValue = introSo.FindProperty("screenFadeCanvasGroup").objectReferenceValue;
        so.FindProperty("mainMenuMusicSource").objectReferenceValue = introSo.FindProperty("mainMenuMusicSource").objectReferenceValue;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Levels] Main menu level select built: Play opens LEVEL 1 (campfire intro -> Level 1), LEVEL 2 (Level 2 Intro Cutscene), LEVEL 3 (Level3 IntroCutscene) and BACK.");

        if (!string.IsNullOrEmpty(back) && back != MainMenu) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
    }

    static Transform Find(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t;
        return null;
    }

    static void AddButton(Transform parent, RectTransform template, Transform audioFrom, TMP_FontAsset font, string label, Vector2 pos, UnityAction onClick)
    {
        var go = new GameObject(label.Replace(" ", "") + "Button", typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = template.anchorMin;
        rt.anchorMax = template.anchorMax;
        rt.pivot = template.pivot;
        rt.sizeDelta = template.sizeDelta;
        rt.localScale = template.localScale;
        rt.anchoredPosition = pos;

        // Text sits where the words are drawn in the existing 479x141 button images (left side, centred vertically).
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = label;
        if (font != null) text.font = font;
        text.fontSize = 34f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.margin = new Vector4(112f, 0f, 0f, 0f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = text;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Normal;
        colors.highlightedColor = Hover;
        colors.pressedColor = Pressed;
        colors.selectedColor = Normal;
        button.colors = colors;
        UnityEventTools.AddPersistentListener(button.onClick, onClick);

        var audio = audioFrom.GetComponent<ButtonAudio>();
        if (audio != null && ComponentUtility.CopyComponent(audio)) ComponentUtility.PasteComponentAsNew(go);
    }

    static void RemoveListeners(UnityEventBase evt, params string[] methods)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            if (System.Array.IndexOf(methods, evt.GetPersistentMethodName(i)) >= 0)
                UnityEventTools.RemovePersistentListener(evt, i);
    }
}
