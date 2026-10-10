using UnityEngine;
using TMPro;

// Shows the number of living allied troops in a text element (e.g. one in your pause menu).
// Reads EnemyAI.CountLivingWithTag, the same source the GameOutcomeManager uses, so it always
// matches the real count.
//
// Because it's a pause-menu readout, it doesn't need to update every frame during play. By
// default it refreshes whenever this object becomes active (i.e. when the pause menu opens) and,
// optionally, on a slow repeating tick while it stays open. Call Refresh() yourself if you open
// the menu without enabling this object.
public class allycounter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The text element to write the count into (your pause-menu text).")]
    [SerializeField] private TMP_Text counterText;

    [Header("Counting")]
    [Tooltip("Tag of the allied troops to count.")]
    [SerializeField] private string allyTag = "Ally";

    [Header("Format")]
    [Tooltip("Format string; {0} is replaced with the count. e.g. 'Troops remaining: {0}'.")]
    [SerializeField] private string format = "Troops remaining: {0}";

    [Header("Refresh")]
    [Tooltip("Also refresh on a repeating timer while this object is active, so the number stays live if the menu is left open. Set to 0 to only refresh when the menu opens.")]
    [SerializeField] private float refreshInterval = 0.5f;

    private float timer;

    private void OnEnable()
    {
        Refresh();
        timer = refreshInterval;
    }

    private void Update()
    {
        if (refreshInterval <= 0f) return;

        // Use unscaled time so it still ticks when the game is paused via Time.timeScale = 0.
        timer -= Time.unscaledDeltaTime;
        if (timer <= 0f)
        {
            Refresh();
            timer = refreshInterval;
        }
    }

    // Writes the current living-ally count into the text. Safe to call anytime.
    public void Refresh()
    {
        if (counterText == null) return;
        int count = EnemyAI.CountLivingWithTag(allyTag);
        counterText.text = string.Format(format, count);
    }
}