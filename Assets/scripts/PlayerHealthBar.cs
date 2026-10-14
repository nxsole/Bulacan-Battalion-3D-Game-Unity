using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// On-screen health bar for the player: one segment per point of health (FirstPersonController's
// maxHealth - 2 by default), dark when lost, with a quick flash when you take a hit. It follows the
// player script's own regen and respawn, which refill health.
//
// FirstPersonController keeps its health private and has no public way to read it, and it's left
// untouched, so this reads the two fields directly (read-only). If they're ever renamed it logs a
// warning once and hides itself rather than breaking.
public class PlayerHealthBar : MonoBehaviour
{
    [SerializeField] private FirstPersonController player;
    [Tooltip("Segments are created inside this at runtime, one per health point (lay it out with a HorizontalLayoutGroup).")]
    [SerializeField] private RectTransform segmentContainer;
    [SerializeField] private Color fullColour = new Color(0.82f, 0.18f, 0.14f, 1f);
    [SerializeField] private Color emptyColour = new Color(0.1f, 0.1f, 0.1f, 0.6f);
    [SerializeField] private Color hitFlashColour = new Color(1f, 0.9f, 0.75f, 1f);
    [SerializeField] private float hitFlashTime = 0.25f;

    private static readonly FieldInfo CurrentField = typeof(FirstPersonController).GetField("currentHealth", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo MaxField = typeof(FirstPersonController).GetField("maxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    private Image[] segments;
    private int shown = -1;
    private float flashUntil;

    private void Start()
    {
        if (player == null) player = GetComponentInParent<FirstPersonController>();
        if (player == null || segmentContainer == null || CurrentField == null || MaxField == null)
        {
            Debug.LogWarning("PlayerHealthBar: can't read the player's health (missing player, container, or FirstPersonController fields renamed) - hiding the bar.", this);
            gameObject.SetActive(false);
            return;
        }

        int max = Mathf.Max(1, (int)MaxField.GetValue(player));
        segments = new Image[max];
        for (int i = 0; i < max; i++)
        {
            var go = new GameObject("Segment " + (i + 1), typeof(RectTransform));
            go.layer = segmentContainer.gameObject.layer;
            go.transform.SetParent(segmentContainer, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            segments[i] = img;
        }
    }

    private void Update()
    {
        if (segments == null) return;
        int current = Mathf.Clamp((int)CurrentField.GetValue(player), 0, segments.Length);
        if (current < shown) flashUntil = Time.unscaledTime + hitFlashTime;
        shown = current;

        bool flashing = Time.unscaledTime < flashUntil;
        for (int i = 0; i < segments.Length; i++)
        {
            bool filled = i < current;
            // the segment just lost flashes before going dark
            segments[i].color = flashing && i == current ? hitFlashColour : filled ? fullColour : emptyColour;
        }
    }
}
