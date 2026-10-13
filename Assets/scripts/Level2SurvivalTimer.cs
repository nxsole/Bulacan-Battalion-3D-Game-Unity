using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Level 2 only. The win condition: hold the pass until the countdown runs out. The time left is on
// screen the whole way; when it reaches zero, onTimerFinished fires (Tools > Level 2 > Deploy troops
// wires it to GameOutcomeManager.TriggerWin and to stopping the American waves).
//
// Counts on scaled time, so the pause menu (Time.timeScale = 0) pauses it too. It holds off while the
// intro's black screen is still up, so the player gets the full time from the moment they can play.
// Losing is untouched: GameOutcomeManager still calls it when the last Filipino falls, and a win at
// zero after that is ignored because the outcome is already decided.
public class Level2SurvivalTimer : MonoBehaviour
{
    [Tooltip("How long the Filipinos must hold the pass, in seconds.")]
    [SerializeField, Min(1f)] private float duration = 300f;
    [SerializeField] private TMP_Text timerText;
    [Tooltip("Shown above the time.")]
    [SerializeField] private string label = "HOLD THE PASS";

    [Header("Final stretch")]
    [Tooltip("The time turns warningColor once this many seconds are left.")]
    [SerializeField] private float warningTime = 30f;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color warningColor = new Color(1f, 0.35f, 0.25f);

    [Header("Start")]
    [Tooltip("Don't start counting while this full-screen image is still showing - the intro's black screen. Leave empty to start straight away.")]
    [SerializeField] private Graphic waitWhileVisible;
    [Tooltip("Hide the timer once it has run out.")]
    [SerializeField] private bool hideWhenFinished = true;

    [Tooltip("Fired once when the countdown reaches zero.")]
    public UnityEvent onTimerFinished = new UnityEvent();

    public float Remaining { get; private set; }
    public bool Running { get; private set; }
    public bool Finished { get; private set; }

    private int shownSeconds = -1;

    private void Awake()
    {
        Remaining = duration;
        Refresh();
    }

    private IEnumerator Start()
    {
        // One frame first so the intro's Start has put its black screen up before we look at it.
        yield return null;
        while (waitWhileVisible != null && waitWhileVisible.isActiveAndEnabled && waitWhileVisible.color.a > 0.01f)
            yield return null;
        Running = true;
    }

    private void Update()
    {
        if (!Running || Finished) return;

        Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
        Refresh();
        if (Remaining > 0f) return;

        Finished = true;
        Running = false;
        if (hideWhenFinished && timerText != null) timerText.gameObject.SetActive(false);
        onTimerFinished.Invoke();
    }

    private void Refresh()
    {
        if (timerText == null) return;
        int seconds = Mathf.CeilToInt(Remaining);
        if (seconds == shownSeconds) return; // only rebuild the string when the display changes
        shownSeconds = seconds;
        timerText.text = $"<size=55%>{label}</size>\n{seconds / 60}:{seconds % 60:00}";
        timerText.color = Remaining <= warningTime ? warningColor : normalColor;
    }
}
