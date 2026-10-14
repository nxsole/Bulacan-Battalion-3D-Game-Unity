using UnityEngine;

// Put this on a GameObject with a trigger Collider (e.g. a Box Collider with Is Trigger ticked)
// placed at the location the player must reach to win. When the player is inside it (and, if
// required, every enemy is dead) it tells the GameOutcomeManager to run the win sequence.
[RequireComponent(typeof(Collider))]
public class WinZone : MonoBehaviour
{
    [Tooltip("The scene's GameOutcomeManager. If left empty, it's found automatically.")]
    [SerializeField] private GameOutcomeManager outcomeManager;
    [Tooltip("Tag of the player object that triggers the win.")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("Trigger only once.")]
    [SerializeField] private bool oneShot = true;

    [Header("Clear condition")]
    [Tooltip("If ticked, reaching this zone only wins once every enemy is dead.")]
    [SerializeField] private bool requireAllEnemiesDead = true;
    [Tooltip("Tag the enemy soldiers use.")]
    [SerializeField] private string enemyTag = "Enemy";

    private bool fired;
    private float nextLog;

    private void Reset()
    {
        // Convenience: auto-tick Is Trigger when the component is added in the editor.
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        if (outcomeManager == null) outcomeManager = FindObjectOfType<GameOutcomeManager>();
    }

    // Stay (not just Enter) so the win still fires if the last enemy dies while the player is already standing here.
    private void OnTriggerEnter(Collider other) { TryWin(other); }
    private void OnTriggerStay(Collider other) { TryWin(other); }

    private void TryWin(Collider other)
    {
        if (fired && oneShot) return;
        if (!other.CompareTag(playerTag)) return;
        if (outcomeManager == null) return;

        if (requireAllEnemiesDead)
        {
            int left = EnemyAI.CountLivingWithTag(enemyTag);
            if (left > 0)
            {
                if (Time.time >= nextLog) { Debug.Log("[WinZone] Reached the end point, but " + left + " enemies are still alive.", this); nextLog = Time.time + 3f; }
                return;
            }
        }

        fired = true;
        // Pass the player's transform so the manager can freeze its controller during the pan.
        outcomeManager.TriggerWin(other.transform);
    }
}
