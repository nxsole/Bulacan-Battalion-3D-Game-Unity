using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

// Level 2 only. Sends the Americans up the pass in waves from the bottom of the trail.
//
// Every soldier is a copy of `template`: an inactive American in the scene that's already set up the
// way they should all be (tags, tuning, and an AllyPathFollower pointed at the advance path), so
// spawned troops behave exactly like hand-placed ones and EnemyAI/AllyPathFollower stay untouched.
//
// The next wave starts once the current one has been whittled down to `nextWaveWhenAlive` troops,
// or `maxTimeBetweenWaves` after its last soldier arrived, whichever comes first. `maxAlive` caps how
// many can be on the field at once; anything over it trickles in as troops fall. With repeatLastWave
// on, waves keep coming at the last size until StopSpawning() is called.
//
// With runOwnWaves off, the spawner has no schedule of its own and only spawns when SpawnWave() is
// called - Level 2's Level2WaveDirector uses that to alternate waves between the two ends.
public class Level2WaveSpawner : MonoBehaviour
{
    [System.Serializable]
    public class Wave
    {
        [Min(1)] public int count = 8;
        [Tooltip("Extra pause (seconds) before this wave, on top of the usual wave trigger.")]
        [Min(0f)] public float extraDelay = 0f;
    }

    [Header("Troops")]
    [Tooltip("Inactive, fully set-up American. It is only ever copied, never used itself.")]
    [SerializeField] private GameObject template;
    [Tooltip("Where troops appear, used in turn. Each spawn is nudged randomly so a wave doesn't stack up on itself.")]
    [SerializeField] private Transform[] spawnPoints;
    [Tooltip("Parent for spawned troops, to keep the Hierarchy tidy. Optional.")]
    [SerializeField] private Transform container;
    [SerializeField] private float spawnJitter = 1f;
    [Tooltip("Seconds between soldiers within a wave, so a wave files in instead of popping in all at once.")]
    [SerializeField] private float spawnInterval = 0.35f;

    [Header("Waves")]
    [Tooltip("Run the wave schedule below by itself. Turn off when a Level2WaveDirector hands this spawner its waves instead (Level 2 alternates sides that way); the settings below are then unused except Max Alive.")]
    [SerializeField] private bool runOwnWaves = true;
    [SerializeField] private Wave[] waves =
    {
        new Wave { count = 6 },
        new Wave { count = 8 },
        new Wave { count = 10 },
        new Wave { count = 12 },
        new Wave { count = 14 },
    };
    [Tooltip("After the last wave above, keep sending more of that size until StopSpawning() is called (Level 2's survival timer does this when time runs out). Off = stop after the last wave.")]
    [SerializeField] private bool repeatLastWave = true;
    [Tooltip("Seconds after the level starts before the first wave sets off.")]
    [SerializeField] private float firstWaveDelay = 5f;
    [Tooltip("Start the next wave early once this few troops from earlier waves are still alive.")]
    [SerializeField] private int nextWaveWhenAlive = 3;
    [Tooltip("Start the next wave after this many seconds regardless.")]
    [SerializeField] private float maxTimeBetweenWaves = 75f;
    [Tooltip("Most Americans alive at once (performance and fairness cap).")]
    [SerializeField] private int maxAlive = 30;

    [Header("Events")]
    [Tooltip("Fired as each wave starts, with its 1-based number. Hook up a 'Wave 2' banner, music cue, etc.")]
    public UnityEvent<int> onWaveStarted = new UnityEvent<int>();
    [Tooltip("Fired once every wave has been spawned and every spawned soldier is dead. Never fires while Repeat Last Wave is on.")]
    public UnityEvent onAllWavesDefeated = new UnityEvent();

    // Each soldier right after it's spawned (already active and in place) - e.g. the Goyo boss uses it
    // to point his reinforcements at the player.
    public event System.Action<GameObject> Spawned;

    private readonly List<Animator> alive = new List<Animator>();
    private int spawnIndex;
    private int spawnedThisWave;

    public int CurrentWave { get; private set; }
    public int WaveCount => waves.Length;
    public bool IsSpawning { get; private set; }

    // Middle of the spawn points - where this side's troops start out.
    public Vector3 SpawnCentre
    {
        get
        {
            Vector3 sum = Vector3.zero; int n = 0;
            if (spawnPoints != null) foreach (var p in spawnPoints) if (p != null) { sum += p.position; n++; }
            return n > 0 ? sum / n : transform.position;
        }
    }

    // EnemyAI.Die() switches the Animator off for the ragdoll, and the body is destroyed later.
    public int AliveCount
    {
        get
        {
            alive.RemoveAll(a => a == null || !a.enabled);
            return alive.Count;
        }
    }

    // Mean ground distance from this spawner's living troops to a point; count = how many are alive.
    public float MeanDistanceTo(Vector3 point, out int count)
    {
        count = AliveCount;
        if (count == 0) return 0f;
        float sum = 0f;
        foreach (var a in alive)
        {
            Vector3 d = a.transform.position - point;
            d.y = 0f;
            sum += d.magnitude;
        }
        return sum / count;
    }

    // How many of this spawner's living troops are within radius (ground distance) of a point.
    public int CountWithin(Vector3 point, float radius)
    {
        int n = 0;
        if (AliveCount == 0) return 0;
        foreach (var a in alive)
        {
            Vector3 d = a.transform.position - point;
            d.y = 0f;
            if (d.sqrMagnitude <= radius * radius) n++;
        }
        return n;
    }

    private void Start()
    {
        if (template == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("Level2WaveSpawner: needs a template soldier and at least one spawn point.", this);
            enabled = false;
            return;
        }
        template.SetActive(false);
        if (runOwnWaves) StartCoroutine(RunWaves());
    }

    // Sends one wave of `count` troops from this spawner (used by Level2WaveDirector). Wave number is
    // only for naming/reporting.
    public void SpawnWave(int count, int waveNumber)
    {
        if (!enabled || template == null || spawnPoints == null || spawnPoints.Length == 0) return;
        StartCoroutine(SpawnBatch(count, waveNumber));
    }

    private IEnumerator RunWaves()
    {
        yield return new WaitForSeconds(firstWaveDelay);
        var poll = new WaitForSeconds(0.5f);

        for (int w = 0; w < waves.Length || repeatLastWave; w++)
        {
            var wave = waves[Mathf.Min(w, waves.Length - 1)];
            if (w > 0)
            {
                float waited = 0f;
                while (waited < maxTimeBetweenWaves && AliveCount > nextWaveWhenAlive)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }
            if (wave.extraDelay > 0f) yield return new WaitForSeconds(wave.extraDelay);
            yield return SpawnBatch(wave.count, w + 1);
        }

        while (AliveCount > 0) yield return poll;
        onAllWavesDefeated?.Invoke();
    }

    private IEnumerator SpawnBatch(int count, int waveNumber)
    {
        IsSpawning = true;
        CurrentWave = waveNumber;
        spawnedThisWave = 0;
        onWaveStarted?.Invoke(CurrentWave);

        var poll = new WaitForSeconds(0.5f);
        for (int i = 0; i < count; i++)
        {
            while (AliveCount >= maxAlive) yield return poll;
            Spawn();
            yield return new WaitForSeconds(spawnInterval);
        }
        IsSpawning = false;
    }

    // No more troops after this; anyone already on the field carries on.
    public void StopSpawning()
    {
        StopAllCoroutines();
        IsSpawning = false;
        enabled = false;
    }

    private void Spawn()
    {
        Transform point = spawnPoints[spawnIndex++ % spawnPoints.Length];
        Vector2 nudge = Random.insideUnitCircle * spawnJitter;
        Vector3 pos = point.position + new Vector3(nudge.x, 0f, nudge.y);
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, spawnJitter + 1f, NavMesh.AllAreas)) pos = hit.position;

        // Height from the terrain, not the NavMesh: EnemyAI moves by root motion from the pivot, and
        // the NavMesh's approximate height has put units into the floor before.
        var terrain = Terrain.activeTerrain;
        if (terrain != null) pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;

        var go = Instantiate(template, pos, point.rotation, container); // copy of an inactive object stays inactive
        go.name = "American Soldier (wave " + CurrentWave + " #" + (++spawnedThisWave) + ")";
        go.SetActive(true); // Awake/OnEnable run now, already in place

        var animator = go.GetComponent<Animator>();
        if (animator != null) alive.Add(animator);
        Spawned?.Invoke(go);
    }

    private void OnDrawGizmos()
    {
        if (spawnPoints == null) return;
        Gizmos.color = new Color(0.2f, 0.45f, 1f);
        foreach (var p in spawnPoints)
        {
            if (p == null) continue;
            Gizmos.DrawWireSphere(p.position, spawnJitter);
            Gizmos.DrawLine(p.position, p.position + p.forward * 2f);
        }
    }
}
