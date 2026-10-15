using System.Collections;
using UnityEngine;

// Put this on each ally troop (alongside its EnemyAI). It walks an AllyPath by issuing the
// unit's OWN movement system one waypoint at a time via EnemyAI.ReceiveMoveOrder, advancing
// to the next only once EnemyAI reports the current leg is finished (HasActiveOrder == false).
//
// Because it goes through ReceiveMoveOrder, everything EnemyAI already does for orders comes
// for free: formation spacing between units on the same point, breaking off to fight enemies
// spotted en route, then resuming the path afterward. It also means this never fights EnemyAI
// for the NavMeshAgent/root motion - EnemyAI remains the single owner of movement.
[RequireComponent(typeof(EnemyAI))]
public class AllyPathFollower : MonoBehaviour
{
    [Tooltip("The path to walk. If left empty, the first AllyPath found in the scene is used.")]
    [SerializeField] private AllyPath path;
    [Tooltip("Start walking the path automatically on Start. Turn off if you'd rather trigger it yourself via BeginPath().")]
    [SerializeField] private bool followOnStart = true;
    [Tooltip("Safety cap (seconds) on a single leg, so a unit that can never reach a waypoint (blocked, off-mesh) still advances instead of stalling forever.")]
    [SerializeField] private float perLegTimeout = 30f;

    [Header("Start Audio (optional)")]
    [Tooltip("Played once before the unit starts toward the first waypoint. The unit waits for the clip to finish before moving. Leave empty to start immediately.")]
    [SerializeField] private AudioClip startMoveClip;
    [SerializeField, Range(0f, 1f)] private float startMoveVolume = 1f;
    [Tooltip("If assigned, the clip plays through this source. If left empty, an AudioSource on this object is used (or one is added).")]
    [SerializeField] private AudioSource startAudioSource;
    [Tooltip("If several allies share this path, only ONE should play the start clip (so it isn't 12 overlapping voices). Tick this on exactly one unit, or leave on and it self-limits so only the first to start plays it.")]
    [SerializeField] private bool onlyOneStartClip = true;
    [Tooltip("Extra gap (seconds) after the start clip finishes before the squad reply cry answers it. 0 = reply immediately when the start clip ends.")]
    [SerializeField] private float startReplyExtraDelay = 0.2f;

    private EnemyAI ai;
    private int currentIndex;
    private bool walking;

    // Shared across all followers so the start clip only plays once for the squad when
    // onlyOneStartClip is set, rather than every unit playing it simultaneously.
    private static bool startClipPlayedThisSession;

    // Caches EnemyAI, finds a path if none is assigned, and resets the shared start-clip guard.
    private void Awake()
    {
        ai = GetComponent<EnemyAI>();
        if (path == null) path = FindObjectOfType<AllyPath>();

        // Reset the shared start-clip guard each fresh scene load. Statics survive editor Play
        // sessions, so without this the start clip would only ever play on the first playthrough.
        startClipPlayedThisSession = false;

        if (startAudioSource == null) startAudioSource = GetComponent<AudioSource>();
    }

    // Begins walking the path straight away if followOnStart is on.
    private void Start()
    {
        if (followOnStart) BeginPath();
    }

    // Call this to (re)start the unit walking from the first waypoint.
    public void BeginPath()
    {
        if (path == null || path.Count == 0 || walking) return;
        currentIndex = 0;
        walking = true;
        StartCoroutine(FollowRoutine());
    }

    // Stops following the path (the unit keeps whatever order EnemyAI is already carrying out).
    public void StopPath()
    {
        walking = false;
        StopAllCoroutines();
    }

    // Plays the start clip, then sends EnemyAI to each waypoint in turn, waiting for each leg to finish (or time out).
    private IEnumerator FollowRoutine()
    {
        // Optional start bark: play it and hold until it finishes before moving off.
        yield return StartCoroutine(PlayStartClipAndWait());

        while (walking && currentIndex < path.Count)
        {
            // Issue this waypoint as a normal move order and let EnemyAI carry it out.
            ai.ReceiveMoveOrder(path.GetPoint(currentIndex));

            // Give EnemyAI a frame to pick the order up (it switches into PlayerOrder at its
            // next state check, so HasActiveOrder won't be true on the very first frame).
            yield return null;

            float legTimer = 0f;
            // Wait until this leg is done. HasActiveOrder goes false when the unit arrives.
            // If the unit peels off to fight, HasActiveOrder stays true (EnemyAI routes back
            // into the order after combat), so we correctly keep waiting through the fight.
            while (ai.HasActiveOrder && legTimer < perLegTimeout)
            {
                legTimer += Time.deltaTime;
                yield return null;
            }

            currentIndex++;
        }

        walking = false; // reached the final point (or path ended) - stop, per design.
    }

    // Plays the optional start bark (once for the squad if onlyOneStartClip), waits for it to end, then triggers the squad's reply cry.
    private IEnumerator PlayStartClipAndWait()
    {
        if (startMoveClip == null) yield break;

        // If only one unit should voice the start order, the first to arrive here plays it and
        // the rest skip the clip but still wait out its duration, so the whole squad steps off
        // together when it finishes.
        bool iPlay = true;
        if (onlyOneStartClip)
        {
            if (startClipPlayedThisSession) iPlay = false;
            else startClipPlayedThisSession = true;
        }

        if (iPlay && startAudioSource != null)
        {
            startAudioSource.PlayOneShot(startMoveClip, startMoveVolume);
        }

        // Wait the clip's length whether or not this unit is the one playing it, so movement
        // begins only after the audio has finished for everyone.
        yield return new WaitForSeconds(startMoveClip.length);

        // Now that the start clip has finished, have the squad answer it with a reply cry. This is
        // safe to call from every follower - EnemyAI.PlayStartReplyCry is guarded to fire once per
        // side, so only the first call actually plays and the rest no-op.
        if (ai != null) ai.PlayStartReplyCry(startReplyExtraDelay);
    }
}