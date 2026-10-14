// Tools > Levels > Add Level Music
// Sets the looping music for Level 2 Gameplay and Level 3 and saves both scenes. Level 2 plays it on its
// existing PersistentBGM (so it carries into the ending cutscene); Level 3 gets a "Level Music" object.
// Any older music track in either scene is removed so only the new one plays.
// Safe to run again. To change the loudness later,
// edit Volume on that AudioSource ("persistentMusic" in Level 2, "Level Music" in Level 3).
using TiradPass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

public static class LevelMusicSetup
{
    const string Level2 = "Assets/Scenes/Level 2 Gameplay.unity";
    const string Level3 = "Assets/Scenes/Level 3.unity";
    const string Level2Music = "Assets/Audios/Music/Level 2 Music.mp3";
    const string Level3Music = "Assets/Audios/Music/Level 3 Music.mp3";
    const string Mixer = "Assets/Audios/MasterMixer.mixer";
    const string ObjectName = "Level Music";
    const float Volume = 0.3f;   // kept low so gunfire, voices and SFX stay on top
    const float MusicMinLength = 60f;   // clips at least this long are treated as music, not SFX

    [MenuItem("Tools/Levels/Add Level Music")]
    public static void AddLevelMusic()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Level music", "Stop Play mode first.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string back = EditorSceneManager.GetActiveScene().path;

        // Routed through the Master group so the Settings volume slider affects it too.
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(Mixer);
        var groups = mixer != null ? mixer.FindMatchingGroups("Master") : null;
        var master = groups != null && groups.Length > 0 ? groups[0] : null;

        bool ok = Add(Level2, Level2Music, master) & Add(Level3, Level3Music, master);
        if (ok) Debug.Log("[Levels] Level music added to Level 2 Gameplay and Level 3 (looping, volume " + Volume + ").");

        if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
    }

    static bool Add(string scenePath, string clipPath, AudioMixerGroup output)
    {
        StreamMusic(clipPath);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        if (clip == null) { Debug.LogError("[Levels] Missing music clip: " + clipPath); return false; }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == ObjectName) Object.DestroyImmediate(root);

        // If the level already has a PersistentBGM (Level 2 does), use it: its music carries on into the
        // ending cutscene, which fades it out. Otherwise add a plain "Level Music" object.
        AudioSource src = null;
        var bgm = Object.FindFirstObjectByType<PersistentBGM>(FindObjectsInactive.Include);
        if (bgm != null) src = new SerializedObject(bgm).FindProperty("audioSource").objectReferenceValue as AudioSource;
        if (bgm != null && src == null) src = bgm.GetComponent<AudioSource>();
        if (src == null) src = new GameObject(ObjectName).AddComponent<AudioSource>();

        // Silence any other music track in the scene so only the new one plays.
        foreach (var other in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (other == src || other.clip == null || other.clip.length < MusicMinLength) continue;
            Debug.Log("[Levels] " + scene.name + ": removed old music '" + other.clip.name + "' from '" + other.name + "'.");
            other.clip = null;
            EditorUtility.SetDirty(other);
        }

        src.clip = clip;
        src.outputAudioMixerGroup = output;
        src.loop = true;
        src.playOnAwake = true;
        src.volume = Volume;
        src.spatialBlend = 0f;   // 2D - same loudness wherever the player is
        src.priority = 0;        // never dropped when lots of gunshots play at once
        EditorUtility.SetDirty(src);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    // Long tracks stream from disk instead of being decompressed into memory on load.
    static void StreamMusic(string clipPath)
    {
        var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
        if (importer == null) return;
        var s = importer.defaultSampleSettings;
        if (s.loadType == AudioClipLoadType.Streaming) return;
        s.loadType = AudioClipLoadType.Streaming;
        importer.defaultSampleSettings = s;
        importer.loadInBackground = true;
        importer.SaveAndReimport();
    }
}
