using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TiradPass;

// Main menu level select. The Play button opens it: Play / Settings / Exit are hidden and the
// LEVEL 1 / LEVEL 2 / LEVEL 3 / BACK buttons are shown in their place.
//  - Level 1 plays the campfire intro in the menu as before (which then loads Level 1).
//  - Level 2 and Level 3 fade to black and load their intro cutscene scenes.
// Built and wired by Tools > Levels > Build Main Menu Level Select.
public class LevelSelectMenu : MonoBehaviour
{
    [Header("Menu Groups")]
    [Tooltip("Play / Settings / Exit - hidden while the level select is open.")]
    [SerializeField] private GameObject[] mainButtons;
    [Tooltip("Holds the LEVEL 1 / 2 / 3 / BACK buttons.")]
    [SerializeField] private GameObject levelSelectGroup;

    [Header("Level 1")]
    [SerializeField] private TiradPassIntroCutscene level1Intro;

    [Header("Level 2 & 3")]
    [SerializeField] private string level2SceneName = "Level 2 Intro Cutscene";
    [SerializeField] private string level3SceneName = "Level3 IntroCutscene";
    [Tooltip("Full-screen black CanvasGroup faded in before the scene loads (same one the Level 1 intro uses).")]
    [SerializeField] private CanvasGroup screenFadeCanvasGroup;
    [SerializeField] private AudioSource mainMenuMusicSource;
    [SerializeField] private float fadeDuration = 1.5f;

    private bool isLoading;

    private void Awake()
    {
        ShowLevelSelect(false);

        // Level 2's music lives on a PersistentBGM that survives scene loads. If the player quit back to
        // the menu mid-level, drop it so it doesn't play over the menu or follow them into another level.
        foreach (var bgm in FindObjectsByType<PersistentBGM>(FindObjectsSortMode.None))
            Destroy(bgm.gameObject);
    }

    public void OpenLevelSelect() => ShowLevelSelect(true);

    public void CloseLevelSelect() => ShowLevelSelect(false);

    public void PlayLevel1()
    {
        if (isLoading) return;
        isLoading = true;
        if (level1Intro != null) level1Intro.PlayCutscene();
    }

    public void PlayLevel2() => LoadLevel(level2SceneName);

    public void PlayLevel3() => LoadLevel(level3SceneName);

    private void ShowLevelSelect(bool show)
    {
        if (levelSelectGroup != null) levelSelectGroup.SetActive(show);
        if (mainButtons == null) return;
        foreach (var b in mainButtons)
            if (b != null) b.SetActive(!show);
    }

    private void LoadLevel(string sceneName)
    {
        if (isLoading || string.IsNullOrEmpty(sceneName)) return;
        isLoading = true;
        StartCoroutine(FadeAndLoad(sceneName));
    }

    private IEnumerator FadeAndLoad(string sceneName)
    {
        float startVolume = mainMenuMusicSource != null ? mainMenuMusicSource.volume : 0f;
        float startAlpha = screenFadeCanvasGroup != null ? screenFadeCanvasGroup.alpha : 0f;
        if (screenFadeCanvasGroup != null) screenFadeCanvasGroup.blocksRaycasts = true;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / fadeDuration;
            if (screenFadeCanvasGroup != null) screenFadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, t);
            if (mainMenuMusicSource != null) mainMenuMusicSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }

        if (screenFadeCanvasGroup != null) screenFadeCanvasGroup.alpha = 1f;
        if (mainMenuMusicSource != null) mainMenuMusicSource.Stop();

        SceneManager.LoadScene(sceneName);
    }
}
