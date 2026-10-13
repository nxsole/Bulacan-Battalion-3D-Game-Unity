using TMPro;
using UnityEngine;

// Level 2 only. On-screen bar showing how close each American column is to the Filipino camp. One
// track with the camp marked in the middle: each side's fill grows from its own end toward the
// centre as the mean distance of that side's living Americans to the camp shrinks (empty = back at
// their spawn, meeting the middle = at the camp). The label gives that mean distance in metres, or
// "clear" when the side has nobody alive.
public class Level2FrontlineHud : MonoBehaviour
{
    [System.Serializable]
    public class Side
    {
        public string label = "SOUTH";
        public Level2WaveSpawner spawner;
        [Tooltip("Fill bar inside the track. Its anchors are driven by this script.")]
        public RectTransform fill;
        public TMP_Text text;
        [Tooltip("Fill grows from the right end of the track (the north side) instead of the left.")]
        public bool fromRight;

        [System.NonSerialized] public float startDistance;
        [System.NonSerialized] public float shown;
        [System.NonSerialized] public int lastMetres = int.MinValue;
    }

    [Tooltip("The point both columns converge on (the Filipino camp).")]
    [SerializeField] private Transform centre;
    [SerializeField] private Side[] sides;
    [Tooltip("How quickly the bars catch up with the real distance (higher = snappier).")]
    [SerializeField] private float smoothing = 4f;

    private void Start()
    {
        foreach (var s in sides)
        {
            // "empty" = as far out as this side's spawn
            s.startDistance = s.spawner != null && centre != null ? FlatDistance(s.spawner.SpawnCentre, centre.position) : 100f;
            if (s.startDistance < 1f) s.startDistance = 100f;
            Apply(s, 0f);
            SetLabel(s, 0f, 0);
        }
    }

    private void Update()
    {
        if (centre == null) return;
        foreach (var s in sides)
        {
            int count = 0;
            float mean = s.spawner != null ? s.spawner.MeanDistanceTo(centre.position, out count) : 0f;
            float target = count > 0 ? Mathf.Clamp01(1f - mean / s.startDistance) : 0f;
            s.shown = Mathf.Lerp(s.shown, target, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
            Apply(s, s.shown);
            SetLabel(s, mean, count);
        }
    }

    // Each side owns half the track, so a full bar from either end just reaches the camp marker.
    private static void Apply(Side s, float t)
    {
        if (s.fill == null) return;
        float w = 0.5f * t;
        s.fill.anchorMin = new Vector2(s.fromRight ? 1f - w : 0f, 0f);
        s.fill.anchorMax = new Vector2(s.fromRight ? 1f : w, 1f);
        s.fill.offsetMin = s.fill.offsetMax = Vector2.zero;
    }

    private static void SetLabel(Side s, float mean, int count)
    {
        if (s.text == null) return;
        int metres = count > 0 ? Mathf.RoundToInt(mean) : -1;
        if (metres == s.lastMetres) return; // only rebuild the string when the shown value changes
        s.lastMetres = metres;
        s.text.text = count > 0 ? $"{s.label}  {metres} m" : $"{s.label}  clear";
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
