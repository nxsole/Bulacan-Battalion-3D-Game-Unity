// Tools > Levels > Connect Level 1 -> Level 2
// Sets Level 1's win to load "Level 2 Intro Cutscene" (losing still goes back to the main menu) and saves Level 1.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LevelFlowSetup
{
    const string Level1 = "Assets/Scenes/Level 1.unity";
    const string Level2Intro = "Level 2 Intro Cutscene";
    const string MainMenu = "main menu";

    [MenuItem("Tools/Levels/Connect Level 1 -> Level 2")]
    public static void ConnectLevel1To2()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Connect levels", "Stop Play mode first.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string back = EditorSceneManager.GetActiveScene().path;

        var scene = EditorSceneManager.OpenScene(Level1, OpenSceneMode.Single);
        int n = 0;
        foreach (var m in Object.FindObjectsByType<GameOutcomeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(m);
            so.FindProperty("mainMenuSceneName").stringValue = Level2Intro;   // after the win sequence
            var lose = so.FindProperty("loseSceneName"); if (lose != null) lose.stringValue = MainMenu;
            so.ApplyModifiedPropertiesWithoutUndo();
            n++;
        }
        EnsureInBuild("Assets/Scenes/" + Level2Intro + ".unity");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Levels] Level 1 now continues to '" + Level2Intro + "' after a win (" + n + " outcome manager(s) updated). Losing still returns to '" + MainMenu + "'.");

        if (!string.IsNullOrEmpty(back) && back != Level1) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
    }

    static void EnsureInBuild(string path)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in list) if (s.path == path) { if (!s.enabled) { s.enabled = true; EditorBuildSettings.scenes = list.ToArray(); } return; }
        list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // Tools > Levels > Connect Level 2 -> Level 3
    // Level 2 Ending Cutscene now loads the Level 3 intro, and the Level 3 intro loads the Level 3 gameplay scene.
    const string L2Ending = "Assets/Scenes/Level 2 Ending Cutscene.unity";
    const string L3Intro = "Assets/Scenes/Level3 IntroCutscene.unity";
    const string L3Gameplay = "Assets/Scenes/Level 3.unity";

    [MenuItem("Tools/Levels/Connect Level 2 -> Level 3")]
    public static void ConnectLevel2To3()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Connect levels", "Stop Play mode first.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string back = EditorSceneManager.GetActiveScene().path;

        int a = SetField(L2Ending, "Level2EndingCutscene", "mainMenuSceneName", System.IO.Path.GetFileNameWithoutExtension(L3Intro));
        int b = SetField(L3Intro, "Level3IntroCutscene", "gameplaySceneName", System.IO.Path.GetFileNameWithoutExtension(L3Gameplay));
        EnsureInBuild(L3Intro);
        EnsureInBuild(L3Gameplay);
        Debug.Log("[Levels] Level 2 Ending Cutscene -> '" + System.IO.Path.GetFileNameWithoutExtension(L3Intro) + "' (" + a + " updated), Level 3 intro -> '" + System.IO.Path.GetFileNameWithoutExtension(L3Gameplay) + "' (" + b + " updated). Both scenes saved and in Build Settings.");

        if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
    }

    static int SetField(string scenePath, string typeName, string field, string value)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        int n = 0;
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null || mb.GetType().Name != typeName) continue;
            var so = new SerializedObject(mb);
            var p = so.FindProperty(field); if (p == null) continue;
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            n++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return n;
    }
}
