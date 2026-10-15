using UnityEngine;

// Level 2 only. The second way the level can end: if the Americans reach the Filipino camp in the
// middle of the pass, GameOutcomeManager plays its ending sequence (camera pan, second line, ending
// cutscene) but opens on its sadder overrun message instead of "The pass holds.".
//
// Sits on the camp's centre marker (Camp Centre, under the Frontline HUD) and finds everything else
// by itself. "Reached" = at least enemiesNeeded living Americans within radius of this point at once.
// Americans stop short of a point the Filipinos still hold (EnemyAI's contested-order check), so in
// practice it fires once the camp's defenders are gone.
//
// When it fires the waves and the survival timer stop, and the Americans stand down (their EnemyAI is
// switched off) so nobody shoots the player during the ending. Holding out until the timer runs out
// still wins as before, and after that this does nothing.
public class Level2CampOverrun : MonoBehaviour
{
    [Tooltip("Ground distance from this point that counts as inside the camp.")]
    [SerializeField] private float radius = 10f;
    [Tooltip("How many living Americans must be inside the camp at once.")]
    [SerializeField, Min(1)] private int enemiesNeeded = 3;
    [Tooltip("Seconds between checks.")]
    [SerializeField] private float checkInterval = 0.5f;
    [Tooltip("Tag the American soldiers use.")]
    [SerializeField] private string enemyTag = "Enemy";

    [Header("Found automatically if left empty")]
    [SerializeField] private GameOutcomeManager outcomeManager;
    [SerializeField] private Level2WaveSpawner[] spawners;
    [SerializeField] private Level2WaveDirector director;
    [SerializeField] private Level2SurvivalTimer timer;
    [SerializeField] private Transform player;

    private bool fired;
    private float nextCheck;

    // Fills in any references left empty.
    private void Start()
    {
        if (outcomeManager == null) outcomeManager = FindFirstObjectByType<GameOutcomeManager>();
        if (spawners == null || spawners.Length == 0) spawners = FindObjectsByType<Level2WaveSpawner>(FindObjectsSortMode.None);
        if (director == null) director = FindFirstObjectByType<Level2WaveDirector>();
        if (timer == null) timer = FindFirstObjectByType<Level2SurvivalTimer>();
        if (player == null)
        {
            var fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) player = fpc.transform;
        }
        if (outcomeManager == null)
        {
            Debug.LogWarning("Level2CampOverrun: no GameOutcomeManager in the scene - the camp can't be overrun.", this);
            enabled = false;
        }
    }

    // Every checkInterval, counts the Americans inside the camp and plays the overrun ending once enough are in.
    private void Update()
    {
        if (fired || Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;
        if (timer != null && timer.Finished) { enabled = false; return; } // held out: the win has already played

        int inside = 0;
        foreach (var s in spawners) if (s != null) inside += s.CountWithin(transform.position, radius);
        if (inside >= enemiesNeeded) Overrun(inside);
    }

    // Ends the level: stops the timer and the waves, stands the Americans down, and starts the ending.
    private void Overrun(int inside)
    {
        fired = true;
        Debug.Log($"[CampOverrun] {inside} Americans reached the camp - the pass is lost.", this);

        if (timer != null) timer.Cancel();
        if (director != null) director.StopSpawning();
        else foreach (var s in spawners) if (s != null) s.StopSpawning();
        StandDownAmericans();

        outcomeManager.TriggerOverrun(player);
    }

    // Switches off every living American's AI (so nobody keeps shooting) and settles them into idle.
    private void StandDownAmericans()
    {
        foreach (var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (!ai.enabled || !ai.CompareTag(enemyTag)) continue;
            var animator = ai.GetComponent<Animator>();
            if (animator == null || !animator.enabled) continue; // already dead (ragdoll)

            ai.enabled = false; // stops its state machine, so it never fires again
            animator.SetBool("Aiming", false);
            animator.SetBool("Walking", false);
            animator.SetBool("Kneel", false);
            animator.SetBool("Crouching", false);
            animator.SetBool("Idle", true);
        }
    }

    // Draws the camp radius in the Scene view.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.2f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
