using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Level 2 only. Lets the player skip the opening text. SceneIntroController (Level 1's intro script)
// is left untouched: skipping stops its routine and leaves the screen exactly as its own ending does -
// black screen and text gone, cursor locked, time running - while the intro music carries on.
// The "skip" prompt shows only while the intro is up and disappears with it either way.
public class Level2IntroSkip : MonoBehaviour
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
}
