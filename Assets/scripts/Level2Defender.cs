using System.Collections;
using UnityEngine;

// Level 2 only. Put this on a Filipino troop (alongside its EnemyAI) to keep it on its post behind
// the sandbags. It never edits EnemyAI - it only uses its public API - so Level 1 is untouched.
//
// The heavy lifting is EnemyAI's own tuning on these units (set by Tools > Level 2 > Deploy troops):
// repositionChance 0, seekCoverBeforeReload off, searchDuration 0 and alertExpireTime -1, which
// switch off every behaviour that walks a unit away by itself. What's left is drift - root motion
// creep, being shoved by a squadmate - and this component walks the soldier back to its post with a
// normal move order when that happens.
//
// Player move orders still work: when an order leaves the soldier somewhere new, that becomes its
// post and it holds there instead.
[RequireComponent(typeof(EnemyAI))]
public class Level2Defender : MonoBehaviour
{
    [Tooltip("How far the soldier may drift from its post before it walks back to it.")]
    [SerializeField] private float leashDistance = 2f;
    [Tooltip("Seconds between post checks.")]
    [SerializeField] private float checkInterval = 1f;
    [Tooltip("Don't walk back while a unit with this tag is this close to the post. EnemyAI treats such an order point as contested and would stop at combat range instead, pulling the soldier off the sandbags. Keep in step with EnemyAI's orderContestedCheckRadius.")]
    [SerializeField] private string enemyTag = "Enemy";
    [SerializeField] private float contestedRadius = 12f;

    private EnemyAI ai;
    private Animator animator;
    private Vector3 post;
    private bool wasMoving;

    public Vector3 Post => post;

    // Caches EnemyAI and the Animator, and takes the starting position as the post.
    private void Awake()
    {
        ai = GetComponent<EnemyAI>();
        animator = GetComponent<Animator>();
        post = transform.position;
    }

    // Starts the post-guarding loop.
    private void OnEnable()
    {
        StartCoroutine(GuardPost());
    }

    // Every checkInterval, walks the soldier back if it drifted off its post, or adopts a new post after a player order.
    private IEnumerator GuardPost()
    {
        var wait = new WaitForSeconds(checkInterval);
        while (true)
        {
            yield return wait;

            // EnemyAI.Die() switches the Animator off for the ragdoll - nothing left to guard.
            if (ai == null || animator == null || !animator.enabled) yield break;

            if (ai.HasActiveOrder) { wasMoving = true; continue; }

            float drift = FlatDistance(transform.position, post);
            if (wasMoving)
            {
                wasMoving = false;
                // Our own walk-backs finish on the post; anything that finishes away from it was a
                // player order, so hold the new spot rather than dragging the soldier back.
                if (drift > leashDistance) { post = transform.position; continue; }
            }

            if (drift > leashDistance && !EnemyNearPost())
            {
                ai.ReceiveMoveOrder(post);
                wasMoving = true;
            }
        }
    }

    // Same tag test EnemyAI uses (CompareTag on the unit), found through the units' body colliders.
    private bool EnemyNearPost()
    {
        foreach (var hit in Physics.OverlapSphere(post, contestedRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            var unit = hit.GetComponentInParent<EnemyAI>();
            if (unit != null && unit != ai && unit.CompareTag(enemyTag)) return true;
        }
        return false;
    }

    // Distance between two points ignoring height.
    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Draws the leash radius around the post in the Scene view.
    private void OnDrawGizmosSelected()
    {
        Vector3 p = Application.isPlaying ? post : transform.position;
        Gizmos.color = new Color(1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(p, leashDistance);
    }
}
