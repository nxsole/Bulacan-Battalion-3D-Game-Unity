using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Lets the player skip a level's opening text (Level 1 and Level 2). SceneIntroController (the intro
// script) is left untouched: skipping stops its routine and leaves the screen exactly as its own ending
// does - black screen and text gone, cursor locked, time running - while the intro music carries on.
// The "skip" prompt shows only while the intro is up and disappears with it either way.
//
// Level 2 has this component set up in its scene. Any other level whose intro has no IntroSkip gets
// one added automatically when the level loads (see AutoInstall), so Level 1 needs no scene changes.
public class IntroSkip : MonoBehaviour
{
    [Tooltip("The intro to skip.")]
    [SerializeField] private SceneIntroController intro;
    [Tooltip("The intro's full-screen black Image (same one assigned on the intro).")]
    [SerializeField] private Image blackOverlay;
    [Tooltip("The intro's typed text (same one assigned on the intro).")]
    [SerializeField] private TMP_Text introText;
    [Tooltip("Optional on-screen hint, e.g. \"Press SPACE to skip\".")]
    [SerializeField] private TMP_Text skipPrompt;
    [SerializeField] private KeyCode[] skipKeys = { KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter };
    [Tooltip("Ignore skip presses for this long after the level loads, so a key still held from the menu doesn't skip it by accident.")]
    [SerializeField] private float ignoreInputFor = 0.5f;
    [SerializeField] private bool lockCursorOnSkip = true;
    [Tooltip("Used to tell the pause menu's frozen time apart from an intro freeze. Optional.")]
    [SerializeField] private PauseManager pauseManager;

    private bool done;
    private float startRealtime;

    private void Start()
    {
        startRealtime = Time.realtimeSinceStartup; // real time: the intro may freeze scaled time
        if (intro == null || blackOverlay == null) { Finish(); return; }
        if (skipPrompt != null) skipPrompt.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (done) return;

        // The intro switches its black screen off when it finishes on its own.
        if (!blackOverlay.gameObject.activeInHierarchy) { Finish(); return; }

        if (Time.realtimeSinceStartup - startRealtime < ignoreInputFor) return;
        if (PauseMenuOpen()) return;

        foreach (var key in skipKeys)
            if (Input.GetKeyDown(key)) { Skip(); return; }
    }

    public void Skip()
    {
        if (done) return;
        intro.StopAllCoroutines(); // halts the typing/fades; the music AudioSource keeps playing

        Color c = blackOverlay.color;
        c.a = 0f;
        blackOverlay.color = c;
        blackOverlay.gameObject.SetActive(false);
        if (introText != null) introText.gameObject.SetActive(false);

        // If the intro froze the scene, it would have unfrozen it at the end - do the same.
        if (Time.timeScale == 0f) Time.timeScale = 1f;

        if (lockCursorOnSkip)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        Finish();
    }

    private void Finish()
    {
        done = true;
        if (skipPrompt != null) skipPrompt.gameObject.SetActive(false);
        enabled = false;
    }

    private bool PauseMenuOpen()
    {
        return pauseManager != null && pauseManager.pauseMenuPanel != null && pauseManager.pauseMenuPanel.activeSelf;
    }

    // ---------------------------------------------------------------- automatic setup
    // Every time a level loads, give any intro that has no IntroSkip one, wired to the intro's own black
    // screen and text, with the same "Press SPACE to skip" hint Level 2 uses. Runs before Start, so the
    // intro hasn't begun typing yet.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSceneLoads()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var intro in FindObjectsByType<SceneIntroController>(FindObjectsSortMode.None))
            if (intro.gameObject.scene == scene && intro.GetComponent<IntroSkip>() == null)
                AutoInstall(intro);
    }

    private static void AutoInstall(SceneIntroController intro)
    {
        // The intro keeps these private; read them (read-only) rather than change the intro script.
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        var black = typeof(SceneIntroController).GetField("blackOverlay", Private)?.GetValue(intro) as Image;
        var text = typeof(SceneIntroController).GetField("introText", Private)?.GetValue(intro) as TMP_Text;
        if (black == null)
        {
            Debug.LogWarning("IntroSkip: couldn't find the intro's black screen, so this intro can't be skipped.", intro);
            return;
        }

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
        if (text != null && text.font != null) prompt.font = text.font;
        promptGo.SetActive(false); // Start shows it while the intro is up

        var skip = intro.gameObject.AddComponent<IntroSkip>();
        skip.intro = intro;
        skip.blackOverlay = black;
        skip.introText = text;
        skip.skipPrompt = prompt;
        skip.pauseManager = FindFirstObjectByType<PauseManager>();
    }
}
