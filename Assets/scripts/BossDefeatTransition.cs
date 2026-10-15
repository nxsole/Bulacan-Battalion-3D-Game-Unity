using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Level 3: once Goyo (the boss) dies, wait a moment so his fall is seen, fade the screen and the level
// music to black, then load the Level 3 ending cutscene. The black overlay is created at runtime, so the
// only setup is this component somewhere in the scene - the boss and music are found automatically.
public class BossDefeatTransition : MonoBehaviour
{
    [Tooltip("Left empty, the first GoyoBossAI in the scene is used.")]
    [SerializeField] private GoyoBossAI boss;
    [SerializeField] private string endingSceneName = "Level 3 Ending cutscene";

    [Header("Timing (real seconds)")]
    [Tooltip("Pause after the boss dies before the fade starts.")]
    [SerializeField] private float delayBeforeFade = 2f;
    [SerializeField] private float fadeDuration = 2f;
    [Tooltip("Time held on black before the ending loads.")]
    [SerializeField] private float holdOnBlack = 0.5f;

    [Header("Audio")]
    [Tooltip("Faded out with the screen. Left empty, the scene's \"Level Music\" object is used.")]
    [SerializeField] private AudioSource musicSource;

    private bool hadBoss;
    private bool triggered;

    private void Start()
    {
        if (boss == null) boss = FindAnyObjectByType<GoyoBossAI>(FindObjectsInactive.Include);
        hadBoss = boss != null;
        if (!hadBoss) Debug.LogWarning("[BossDefeatTransition] No GoyoBossAI found in the scene.");

        if (musicSource == null)
        {
            var music = GameObject.Find("Level Music");
            if (music != null) musicSource = music.GetComponent<AudioSource>();
        }
    }

    private void Update()
    {
        // The boss destroys itself a while after dying, so a missing boss also counts as dead.
        if (triggered || !hadBoss || (boss != null && !boss.IsDead)) return;
        triggered = true;
        StartCoroutine(FadeAndLoad());
    }

    private IEnumerator FadeAndLoad()
    {
        // No pausing from here on - a pause would freeze time into the ending scene.
        var pause = FindAnyObjectByType<PauseManager>();
        if (pause != null) pause.enabled = false;

        if (delayBeforeFade > 0f) yield return new WaitForSecondsRealtime(delayBeforeFade);

        var overlay = CreateBlackOverlay();
        float startVolume = musicSource != null ? musicSource.volume : 0f;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            overlay.color = new Color(0f, 0f, 0f, t);
            if (musicSource != null) musicSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }
        overlay.color = Color.black;
        if (musicSource != null) musicSource.Stop();

        if (holdOnBlack > 0f) yield return new WaitForSecondsRealtime(holdOnBlack);

        Time.timeScale = 1f;
        SceneManager.LoadScene(endingSceneName);
    }

    private static Image CreateBlackOverlay()
    {
        var canvasGo = new GameObject("Boss Defeat Fade", typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // above every HUD, health bar and pause menu

        var imageGo = new GameObject("Black", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)imageGo.transform;
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var image = imageGo.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true; // swallow clicks during the fade
        return image;
    }
}
