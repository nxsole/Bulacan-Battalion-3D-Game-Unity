using TMPro;
using UnityEngine;

// Boss health bar on the player's HUD (top centre): the boss's name over a bar that drops as he takes
// damage, with a pale trail that catches up a moment later so each hit reads clearly. It finds the
// boss in the scene by itself, so the player can carry it everywhere: it stays hidden in levels
// without a boss, and fades out once he's dead. While he's away during his reinforcement phase the
// title shows how many of his men are still alive.
[RequireComponent(typeof(CanvasGroup))]
public class BossHealthBar : MonoBehaviour
{
    [Tooltip("Leave empty to use the first GoyoBossAI in the scene.")]
    [SerializeField] private GoyoBossAI boss;
    [SerializeField] private RectTransform fill;
    [SerializeField] private RectTransform trail;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private string bossName = "GEN. GREGORIO DEL PILAR";
    [Tooltip("Seconds before the pale trail starts catching up after a hit.")]
    [SerializeField] private float trailDelay = 0.5f;
    [SerializeField] private float trailSpeed = 0.6f;
    [SerializeField] private float fadeSpeed = 3f;

    private CanvasGroup group;
    private float shownFill = 1f, shownTrail = 1f, trailWaitUntil, nextSearch;

    // Starts the bar hidden and sets the boss name label.
    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        if (nameText != null) nameText.text = bossName;
    }

    // Finds the boss if needed, fades the bar in/out, updates the fill and catch-up trail, and shows the reinforcement count.
    private void Update()
    {
        if (boss == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 1f; // levels without a boss: look again once a second, not every frame
            boss = FindFirstObjectByType<GoyoBossAI>();
        }
        bool show = boss != null && !boss.IsDead && boss.isActiveAndEnabled;
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
        if (boss == null) return;

        float target = boss.MaxHealth > 0 ? Mathf.Clamp01((float)boss.Health / boss.MaxHealth) : 0f;
        if (target < shownFill) trailWaitUntil = Time.unscaledTime + trailDelay;
        shownFill = target;
        if (Time.unscaledTime >= trailWaitUntil) shownTrail = Mathf.MoveTowards(shownTrail, shownFill, trailSpeed * Time.unscaledDeltaTime);
        shownTrail = Mathf.Max(shownTrail, shownFill);

        SetWidth(fill, shownFill);
        SetWidth(trail, shownTrail);

        // while he's hidden and his men are fighting, the title says how many are left
        if (nameText != null)
        {
            string label = boss.InReinforcementPhase ? $"{bossName}  -  HIS MEN: {boss.ReinforcementsAlive}" : bossName;
            if (label != shownLabel) { nameText.text = label; shownLabel = label; }
        }
    }

    private string shownLabel;

    // Stretches a bar image to fill fraction t (0-1) of its parent's width.
    private static void SetWidth(RectTransform rt, float t)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(t, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
