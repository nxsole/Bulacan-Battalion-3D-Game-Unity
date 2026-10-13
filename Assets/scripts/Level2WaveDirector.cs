using System.Collections;
using UnityEngine;
using UnityEngine.Events;

// Level 2 only. Runs the American wave schedule and sends each wave from the next spawner in turn -
// wave 1 from the first (south), wave 2 from the second (north), wave 3 from the first again, and so
// on - so the two fronts are attacked alternately instead of at once. The spawners just do the
// spawning (their own schedules are switched off; their Max Alive cap still applies).
//
// The next wave starts once only `nextWaveWhenAlive` Americans are left on the whole field, or
// `maxTimeBetweenWaves` after the previous wave finished arriving, whichever comes first. With
// repeatLastWave on, waves keep coming at the last size until StopSpawning() is called (the survival
// timer does this when time runs out).
public class Level2WaveDirector : MonoBehaviour
{
    [Tooltip("Spawners taken in turn, one per wave. Level 2: South, then North.")]
    [SerializeField] private Level2WaveSpawner[] spawners;
    [Tooltip("Troops in each wave, in order. Each wave comes from the next spawner in the list.")]
    [SerializeField] private int[] waveSizes = { 6, 8, 10, 12, 14 };
    [Tooltip("After the last size above, keep sending waves of that size until StopSpawning().")]
    [SerializeField] private bool repeatLastWave = true;
    [Tooltip("Seconds after the level starts before the first wave sets off.")]
    [SerializeField] private float firstWaveDelay = 5f;
    [Tooltip("Start the next wave early once this few Americans are still alive across both sides.")]
    [SerializeField] private int nextWaveWhenAlive = 3;
    [Tooltip("Start the next wave after this many seconds regardless.")]
    [SerializeField] private float maxTimeBetweenWaves = 75f;

    [Tooltip("Fired as each wave starts, with its 1-based number.")]
    public UnityEvent<int> onWaveStarted = new UnityEvent<int>();

    public int CurrentWave { get; private set; }
    public Level2WaveSpawner CurrentSpawner { get; private set; }

    public int TotalAlive
    {
        get
        {
            int n = 0;
            foreach (var s in spawners) if (s != null) n += s.AliveCount;
            return n;
        }
    }

    private IEnumerator Start()
    {
        if (spawners == null || spawners.Length == 0 || waveSizes == null || waveSizes.Length == 0)
        {
            Debug.LogWarning("Level2WaveDirector: needs at least one spawner and one wave size.", this);
            yield break;
        }

        yield return new WaitForSeconds(firstWaveDelay);

        for (int w = 0; w < waveSizes.Length || repeatLastWave; w++)
        {
            if (w > 0)
            {
                // let the previous wave finish arriving, then wait for the field to thin out (or time out)
                while (CurrentSpawner != null && CurrentSpawner.IsSpawning) yield return null;
                float waited = 0f;
                while (waited < maxTimeBetweenWaves && TotalAlive > nextWaveWhenAlive)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }

            var spawner = spawners[w % spawners.Length];
            if (spawner == null || !spawner.enabled) { yield return null; continue; } // never spin without yielding
            CurrentWave = w + 1;
            CurrentSpawner = spawner;
            spawner.SpawnWave(waveSizes[Mathf.Min(w, waveSizes.Length - 1)], CurrentWave);
            onWaveStarted?.Invoke(CurrentWave);
        }
    }

    // No more waves from any side; troops already on the field carry on.
    public void StopSpawning()
    {
        StopAllCoroutines();
        foreach (var s in spawners) if (s != null) s.StopSpawning();
        enabled = false;
    }
}
