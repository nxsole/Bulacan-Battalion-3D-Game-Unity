using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Central win/lose handler using CanvasGroup for screen fades.
public class GameOutcomeManager : MonoBehaviour
{
    [Header("Teams")]
    [Tooltip("Tag of the player's allied troops. When none with this tag remain alive, it's a loss.")]
    [SerializeField] private string allyTag = "Ally";

    [Header("Shared UI")]
    [Tooltip("Drag your 'scenefade' GameObject (with CanvasGroup) here.")]
    [SerializeField] private CanvasGroup sceneFadeCanvasGroup;
    [Tooltip("Outcome text element (TextMeshPro UGUI).")]
    [SerializeField] private TMP_Text outcomeText;

    [Header("Lose Screen")]
    [TextArea(2, 5)]
    [SerializeField] private string loseMessage = "The line has fallen.";
    [SerializeField] private float loseBlackFadeIn = 1f;   // black fades up first
    [SerializeField] private float loseTypeSpeed = 0.05f;
    [SerializeField] private float loseHoldAfterTyped = 3f;
    [SerializeField] private AudioClip loseStinger;

    [Header("Win Sequence")]
    [TextArea(2, 5)]
    [SerializeField] private string winMessage = "The field is ours.";
    [Tooltip("How far (metres) the camera drifts forward during the win pan.")]
    [SerializeField] private float winPanDistance = 6f;
    [Tooltip("How long the camera takes to pan that distance.")]
    [SerializeField] private float winPanDuration = 6f;
    [Tooltip("Delay after the pan starts before the win text begins typing.")]
    [SerializeField] private float winTextDelay = 1.5f;
    [SerializeField] private float winTypeSpeed = 0.06f;
    [Tooltip("How long the fully-typed win text holds before the black fades in.")]
    [SerializeField] private float winHoldAfterTyped = 2f;
    [SerializeField] private float winBlackFadeIn = 2f;
    [SerializeField] private AudioClip winStinger;

    [Header("Win - Second Line (on black)")]
    [Tooltip("A second line shown on the fully-black screen after the first line has faded under it. Leave empty to skip and go straight to the next scene.")]
    [TextArea(2, 5)]
    [SerializeField] private string winSecondMessage = "1902. The war is won.";
    [Tooltip("Seconds to wait on black (after it's fully faded in) before the second line appears.")]
    [SerializeField] private float winSecondTextDelay = 0.6f;
    [Tooltip("Seconds the second line holds on screen before transitioning.")]
    [SerializeField] private float winSecondTextHold = 3f;
    [Tooltip("Separate TMP text for the second line. If left empty, the same outcomeText element is reused.")]
    [SerializeField] private TMP_Text winSecondText;

    [Header("Audio")]
    [Tooltip("Optional source for the win/lose stinger. If empty, one is added when needed.")]
    [SerializeField] private AudioSource stingerSource;

    [Header("After")]
    [Tooltip("Name of the scene to load on win (e.g., Level 2 Ending Cutscene or MainMenu). Must be in Build Settings.")]
    [SerializeField] private string mainMenuSceneName = "Level 2 Ending Cutscene";
    [Tooltip("Duration of final transition fade before loading scene.")]
    [SerializeField] private float sceneFadeDuration = 1.2f;
    [Tooltip("Real seconds to wait on the final black screen before loading the scene.")]
    [SerializeField] private float delayBeforeMenu = 0.5f;

    [Header("Debug")]
    [Tooltip("Logs subscription, every unit death, the live ally count, and why lose did/didn't fire. Turn off once confirmed.")]
    [SerializeField] private bool debugLogging = true;

    private bool outcomeDecided;

    private void Awake()
    {
        if (sceneFadeCanvasGroup != null)
        {
            sceneFadeCanvasGroup.alpha = 0f;
            sceneFadeCanvasGroup.gameObject.SetActive(true);
        }
        if (outcomeText != null)
        {
            outcomeText.text = "";
            SetTextAlpha(outcomeText, 0f);
        }
        if (stingerSource == null) stingerSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        EnemyAI.OnAnyUnitDied += HandleUnitDied;
        if (debugLogging) Debug.Log($"[Outcome] Subscribed to death events. Watching for allyTag='{allyTag}'. Living allies at start: {EnemyAI.CountLivingWithTag(allyTag)}.", this);
    }
    private void OnDisable() => EnemyAI.OnAnyUnitDied -= HandleUnitDied;

    private void HandleUnitDied(string diedTag)
    {
        if (outcomeDecided) return;

        int living = EnemyAI.CountLivingWithTag(allyTag);
        if (debugLogging) Debug.Log($"[Outcome] Unit died with tag '{diedTag}'. My allyTag='{allyTag}'. Living allies now: {living}.", this);

        if (diedTag != allyTag) return;

        if (living <= 0)
        {
            if (debugLogging) Debug.Log("[Outcome] No allies left -> triggering lose.", this);
            TriggerLose();
        }
    }

    // ---------------- LOSE ----------------

    public void TriggerLose()
    {
        if (outcomeDecided) return;
        outcomeDecided = true;
        StartCoroutine(LoseRoutine());
    }

    private IEnumerator LoseRoutine()
    {
        PlayStinger(loseStinger);

        if (sceneFadeCanvasGroup != null)
            yield return FadeCanvasGroup(sceneFadeCanvasGroup, 0f, 1f, loseBlackFadeIn);

        if (outcomeText != null)
        {
            SetTextAlpha(outcomeText, 1f);
            yield return Typewriter(outcomeText, loseMessage, loseTypeSpeed);
        }

        if (loseHoldAfterTyped > 0f) yield return WaitRealtime(loseHoldAfterTyped);

        yield return TransitionAndLoadScene();
    }

    // ---------------- WIN ----------------

    public void TriggerWin(Transform playerToFreeze = null)
    {
        if (outcomeDecided) return;
        outcomeDecided = true;
        StartCoroutine(WinRoutine(playerToFreeze));
    }

    private IEnumerator WinRoutine(Transform playerToFreeze)
    {
        PlayStinger(winStinger);

        // Pan the camera the player controller actually drives. Camera.main is ambiguous here:
        // the player's Weapon Cam (the overlay that only renders the arms/rifle) is also tagged
        // MainCamera, and when Camera.main picked it the pan moved that overlay instead - the
        // world view stayed put and the player model just slid backward out of frame.
        Transform camT = null;
        if (playerToFreeze != null)
        {
            var fpc = playerToFreeze.GetComponentInChildren<FirstPersonController>();
            if (fpc != null)
            {
                fpc.enabled = false;
                camT = fpc.PlayerCamera;
            }
        }
        if (camT == null && Camera.main != null) camT = Camera.main.transform;

        if (camT != null) camT.SetParent(null);

        Vector3 panDir = camT != null ? camT.forward : Vector3.zero;
        float panSpeed = winPanDuration > 0f ? winPanDistance / winPanDuration : 0f;

        // Phase 1: Camera pan & typewriter
        bool textStarted = false;
        float t = 0f;
        while (t < winPanDuration)
        {
            if (camT != null) camT.position += panDir * (panSpeed * Time.unscaledDeltaTime);

            if (!textStarted && t >= winTextDelay)
            {
                textStarted = true;
                if (outcomeText != null)
                {
                    SetTextAlpha(outcomeText, 1f);
                    StartCoroutine(Typewriter(outcomeText, winMessage, winTypeSpeed));
                }
            }

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Phase 2: Hold text while panning
        float holdTimer = 0f;
        while (holdTimer < winHoldAfterTyped)
        {
            if (camT != null) camT.position += panDir * (panSpeed * Time.unscaledDeltaTime);
            holdTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        // Phase 3: Fade screen black using scenefade CanvasGroup
        if (sceneFadeCanvasGroup != null && winBlackFadeIn > 0f)
        {
            float fadeTimer = 0f;
            while (fadeTimer < winBlackFadeIn)
            {
                if (camT != null) camT.position += panDir * (panSpeed * Time.unscaledDeltaTime);
                sceneFadeCanvasGroup.alpha = Mathf.Lerp(0f, 1f, fadeTimer / winBlackFadeIn);
                fadeTimer += Time.unscaledDeltaTime;
                yield return null;
            }
            sceneFadeCanvasGroup.alpha = 1f;
        }
        else if (sceneFadeCanvasGroup != null)
        {
            sceneFadeCanvasGroup.alpha = 1f;
        }

        // Phase 4: Second line on black
        if (outcomeText != null && winSecondText == null) outcomeText.text = "";
        else if (outcomeText != null) SetTextAlpha(outcomeText, 0f);

        TMP_Text secondTarget = winSecondText != null ? winSecondText : outcomeText;

        if (!string.IsNullOrEmpty(winSecondMessage))
        {
            if (winSecondTextDelay > 0f) yield return WaitRealtime(winSecondTextDelay);

            if (secondTarget != null)
            {
                secondTarget.transform.SetAsLastSibling();
                secondTarget.text = winSecondMessage;
                SetTextAlpha(secondTarget, 1f);
            }

            if (winSecondTextHold > 0f) yield return WaitRealtime(winSecondTextHold);
        }

        // Final transition to next scene
        yield return TransitionAndLoadScene(secondTarget);
    }

    // ---------------- FADE OUT & SCENE TRANSITION ----------------

    private IEnumerator TransitionAndLoadScene(TMP_Text activeTextElement = null)
    {
        TMP_Text targetText = activeTextElement != null ? activeTextElement : outcomeText;

        // 1. Fade active text element and audio out while keeping screen faded
        float elapsed = 0f;
        float startStingerVol = stingerSource != null ? stingerSource.volume : 0f;

        while (elapsed < sceneFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / sceneFadeDuration;

            if (targetText != null)
            {
                SetTextAlpha(targetText, Mathf.Lerp(1f, 0f, progress));
            }

            if (sceneFadeCanvasGroup != null)
            {
                sceneFadeCanvasGroup.alpha = Mathf.Lerp(sceneFadeCanvasGroup.alpha, 1f, progress);
            }

            if (stingerSource != null && stingerSource.isPlaying)
            {
                stingerSource.volume = Mathf.Lerp(startStingerVol, 0f, progress);
            }

            yield return null;
        }

        if (targetText != null) SetTextAlpha(targetText, 0f);
        if (sceneFadeCanvasGroup != null) sceneFadeCanvasGroup.alpha = 1f;
        if (stingerSource != null) stingerSource.Stop();

        if (delayBeforeMenu > 0f) yield return WaitRealtime(delayBeforeMenu);

        // 2. Reset Time & Unlock Cursor
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 3. Load Next Scene
        if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            Debug.LogWarning("[GameOutcomeManager] Scene name is empty! Verify Inspector setting.", this);
        }
    }

    // ---------------- SHARED HELPERS ----------------

    private void PlayStinger(AudioClip clip)
    {
        if (clip == null) return;
        if (stingerSource == null) stingerSource = gameObject.AddComponent<AudioSource>();
        stingerSource.PlayOneShot(clip);
    }

    private IEnumerator Typewriter(TMP_Text text, string message, float speed)
    {
        if (text == null || string.IsNullOrEmpty(message)) yield break;
        for (int i = 0; i <= message.Length; i++)
        {
            text.text = message.Substring(0, i);
            if (speed > 0f) yield return WaitRealtime(speed);
        }
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;
        if (duration <= 0f) { cg.alpha = to; yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        cg.alpha = to;
    }

    private IEnumerator WaitRealtime(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    private static void SetTextAlpha(TMP_Text text, float a)
    {
        Color c = text.color; c.a = a; text.color = c;
    }
}