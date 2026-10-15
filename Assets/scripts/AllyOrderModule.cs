using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Drop this on any troop tagged "Ally" alongside its NavMeshAgent. It only owns
// player-issued move orders - it doesn't replace combat AI (aiming/firing/cover).
// If the troop also runs something like EnemyAI's state machine for combat, that
// script can poll HasActiveOrder / CurrentDestination to know whether this module
// currently wants control of movement, and yield to it (see notes at bottom).
[RequireComponent(typeof(NavMeshAgent))]
public class AllyOrderModule : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float arrivalDistance = 1f;

    [Header("Formation Spacing (anti-clump)")]
    [Tooltip("Minimum distance kept between allies in the same order. This is also the base spacing of the spiral formation.")]
    [SerializeField] private float formationSpacing = 3f;

    [Header("Contested Check")]
    [SerializeField] private string enemyTag = "Enemy";
    [Tooltip("If any enemy is within this radius of the ordered point, the point is treated as contested.")]
    [SerializeField] private float contestedCheckRadius = 12f;
    [Tooltip("Distance allies try to hold from the nearest contesting enemy, instead of walking into the ordered point itself.")]
    [SerializeField] private float optimalFiringRange = 15f;

    [Header("Re-evaluation")]
    [Tooltip("How often the destination is recomputed while an order is active, so allies react if enemies move or the area clears.")]
    [SerializeField] private float recheckInterval = 0.75f;

    [Header("Terrain / Line-of-sight sanity check")]
    [SerializeField] private LayerMask obstructionMask;
    [SerializeField] private float clearanceRadius = 0.5f;

    [Header("Debug")]
    [Tooltip("Logs to the Console when an order is received and when a destination is set (or fails to be set). Turn off once everything's confirmed working.")]
    [SerializeField] private bool debugLogging = true;

    private NavMeshAgent agent;
    private Vector3 orderPoint;
    private List<AllyOrderModule> squad;
    private int mySquadIndex;
    private bool hasOrder;
    private Coroutine orderRoutine;

    private static readonly List<AllyOrderModule> AllOrderedAllies = new List<AllyOrderModule>();

    // Golden angle in radians - the Vogel/sunflower spiral constant. Placing point i at
    // radius = spacing * sqrt(i) and angle = i * goldenAngle gives an even spread with a
    // guaranteed minimum gap between neighbors, with no per-unit sampling or scoring needed.
    // This is what actually prevents the clumping: every ally in the order gets a distinct,
    // pre-spaced slot instead of everyone independently deciding "closer to the point is best."
    private const float GoldenAngle = 2.39996323f;

    public bool HasActiveOrder => hasOrder;
    public Vector3 CurrentDestination => agent != null ? agent.destination : transform.position;

    // Mirrors EnemyAI's PlanningPosition: other allies check where THIS unit is headed,
    // not just where it currently stands, so two orders issued back-to-back don't both
    // claim the same spot before either unit has actually moved.
    public Vector3 PlanningPosition => (agent != null && agent.hasPath) ? agent.destination : transform.position;

    // Sets up the NavMeshAgent's speed and stopping distance, and logs the unit's components when debugging.
    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        agent.stoppingDistance = arrivalDistance;

        if (debugLogging)
        {
            var behaviours = GetComponents<MonoBehaviour>();
            string names = string.Join(", ", System.Array.ConvertAll(behaviours, b => b.GetType().Name));
            Debug.Log($"[AllyOrderModule] {name} scripts on this GameObject: [{names}] | NavMeshAgent.updatePosition={agent.updatePosition}, updateRotation={agent.updateRotation}", this);
        }
    }

    // Keep the shared list of order-capable allies in step with which ones are active.
    private void OnEnable() => AllOrderedAllies.Add(this);
    private void OnDisable() => AllOrderedAllies.Remove(this);

    // Called by PlayerOrderIssuer. squad is the full group that received this order together -
    // used to assign each unit a distinct formation slot instead of all sampling independently.
    public void ReceiveOrder(Vector3 point, List<AllyOrderModule> orderedSquad)
    {
        if (!agent.isOnNavMesh)
        {
            if (debugLogging) Debug.LogWarning($"[AllyOrderModule] {name} received an order but its NavMeshAgent is NOT on a baked NavMesh - it will not move. Bake a NavMesh (Window > AI > Navigation) and make sure this unit spawns on a walkable surface.", this);
            return;
        }

        orderPoint = point;
        squad = orderedSquad;
        hasOrder = true;

        // Assign slot by current distance to the order point, closest-first, so units
        // roughly keep their relative position in the pack rather than crossing paths
        // to swap places for an arbitrary slot number.
        //
        // IMPORTANT: sort a COPY, not orderedSquad itself. orderedSquad is the exact same
        // List<AllyOrderModule> instance shared by every ally in this order, and the caller
        // (PlayerOrderIssuer) is still foreach-ing over it while calling ReceiveOrder on each
        // one - sorting the original in place mutates the list mid-enumeration and throws
        // "Collection was modified; enumeration operation may not execute" after the first ally.
        List<AllyOrderModule> sortedForSlotting = new List<AllyOrderModule>(orderedSquad);
        sortedForSlotting.Sort((a, b) =>
            Vector3.SqrMagnitude(a.transform.position - point)
            .CompareTo(Vector3.SqrMagnitude(b.transform.position - point)));
        mySquadIndex = sortedForSlotting.IndexOf(this);

        if (debugLogging) Debug.Log($"[AllyOrderModule] {name} received order -> {point} (slot {mySquadIndex})", this);

        if (orderRoutine != null) StopCoroutine(orderRoutine);
        orderRoutine = StartCoroutine(ExecuteOrder());
    }

    // Drops the current order and stops the unit where it is.
    public void CancelOrder()
    {
        hasOrder = false;
        if (orderRoutine != null) StopCoroutine(orderRoutine);
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
    }

    // Repeatedly recomputes and sets the destination until the unit arrives, so it reacts to enemies moving.
    private IEnumerator ExecuteOrder()
    {
        while (hasOrder)
        {
            Vector3 destination = ComputeDestination();

            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, formationSpacing * 2f, NavMesh.AllAreas))
            {
                agent.SetDestination(navHit.position);
                if (debugLogging) Debug.Log($"[AllyOrderModule] {name} destination set -> {navHit.position} (isStopped={agent.isStopped})", this);
            }
            else if (debugLogging)
            {
                Debug.LogWarning($"[AllyOrderModule] {name} could not find a valid NavMesh point near {destination}. Is that area actually walkable / baked?", this);
            }

            float t = 0f;
            while (t < recheckInterval)
            {
                // Order complete: close enough and not still computing a path.
                if (!agent.pathPending && agent.remainingDistance <= arrivalDistance)
                {
                    if (debugLogging) Debug.Log($"[AllyOrderModule] {name} arrived at order destination.", this);
                    hasOrder = false;
                    yield break;
                }
                t += Time.deltaTime;
                yield return null;
            }
            // Loop back and recompute - picks up on enemies moving in/out of the contested check.
        }
    }

    // This unit's target: its formation slot around the order point, or a firing line short of the nearest enemy if the point is contested.
    private Vector3 ComputeDestination()
    {
        Vector3 formationOffset = SpiralOffset(mySquadIndex, formationSpacing);
        Vector3 nearestEnemy;
        bool contested = TryFindNearestEnemy(orderPoint, out nearestEnemy);

        Vector3 candidate;

        if (!contested)
        {
            candidate = orderPoint + formationOffset;
        }
        else
        {
            // Don't walk into the contested area - hold a firing line at optimalFiringRange
            // from the nearest enemy instead, still along the approach direction from us
            // (via the order point) toward that enemy. The same spiral offset is reused here
            // so the firing line itself stays spread out rather than bunching at one range ring.
            Vector3 approachDir = (orderPoint - nearestEnemy);
            approachDir.y = 0f;
            approachDir = approachDir.sqrMagnitude > 0.0001f ? approachDir.normalized : Vector3.forward;

            Vector3 standoffPoint = nearestEnemy + approachDir * optimalFiringRange;
            candidate = standoffPoint + formationOffset;
        }

        candidate = ResolveAgainstAllies(candidate);
        candidate.y = transform.position.y;
        return candidate;
    }

    // Offset of formation slot index on a sunflower spiral, so slots are evenly spread at least spacing apart.
    private Vector3 SpiralOffset(int index, float spacing)
    {
        float radius = spacing * Mathf.Sqrt(index + 0.5f);
        float angle = index * GoldenAngle;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
    }

    // Finds the closest enemy within contestedCheckRadius of point; returns false if there is none.
    private bool TryFindNearestEnemy(Vector3 point, out Vector3 enemyPosition)
    {
        enemyPosition = Vector3.zero;
        Collider[] hits = Physics.OverlapSphere(point, contestedCheckRadius);
        float bestDist = float.MaxValue;
        bool found = false;

        foreach (var hit in hits)
        {
            if (!hit.CompareTag(enemyTag)) continue;
            float d = Vector3.SqrMagnitude(hit.transform.position - point);
            if (d < bestDist)
            {
                bestDist = d;
                enemyPosition = hit.transform.position;
                found = true;
            }
        }

        return found;
    }

    // Nudges the candidate away from any ally (in this order or otherwise) that has
    // already claimed a spot too close to it. The spiral formation already keeps units
    // within the same order spread out from each other; this catches the remaining case
    // where a different, already-moving ally happens to be planning to pass through the
    // same slot (e.g. two separate orders overlapping).
    private Vector3 ResolveAgainstAllies(Vector3 candidate)
    {
        const int maxNudges = 4;

        for (int attempt = 0; attempt < maxNudges; attempt++)
        {
            float closestDist = float.MaxValue;
            Vector3 closestOther = Vector3.zero;

            foreach (var other in AllOrderedAllies)
            {
                if (other == null || other == this) continue;

                float d = Vector3.Distance(candidate, other.PlanningPosition);
                if (d < closestDist)
                {
                    closestDist = d;
                    closestOther = other.PlanningPosition;
                }
            }

            if (closestDist >= formationSpacing || closestDist == float.MaxValue)
            {
                break; // Clear of everyone - good spot.
            }

            Vector3 pushDir = candidate - closestOther;
            pushDir.y = 0f;
            pushDir = pushDir.sqrMagnitude > 0.0001f ? pushDir.normalized : Random.insideUnitSphere;
            candidate += pushDir * (formationSpacing - closestDist + 0.25f);
        }

        return candidate;
    }

    // Draws the order point and its contested-check radius in the Scene view.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(orderPoint, 0.5f);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(orderPoint, contestedCheckRadius);
    }
}