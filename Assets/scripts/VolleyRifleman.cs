using System.Collections;
using UnityEngine;

// Boss level: one of Goyo's firing-line riflemen (GoyoBossAI's volley ability). Appears already in the
// aim pose, tracks the player while the line aims, fires once together with the rest at fireAt, then
// vanishes in smoke.
//
// The shot is a real ray from the muzzle at the player's STANDING chest height. Crouching halves the
// player's collider (2 m -> 1 m), so a crouched player is under it - that's the counter. Walls stop it
// too. It tracks the player right up to the shot, so dashing doesn't help.
//
// Not an AI soldier: GoyoBossAI strips EnemyAI/NavMeshAgent from the soldier model before it appears,
// so the player's gun can't drop them - the only answer is to get low.
public class VolleyRifleman : MonoBehaviour
{
    private Transform target;
    private float fireAt;
    private float chestHeight;
    private float aimOffsetDegrees;
    private float despawnDelay;
    private GameObject muzzleFlash, smoke;
    private AudioClip shotSound;
    private float shotVolume;

    private Animator anim;
    private AudioSource audioSource;
    private Transform muzzle, chest;
    private bool fired;
    private static readonly int HashAiming = Animator.StringToHash("Aiming");
    private static readonly int HashIdle = Animator.StringToHash("Idle");
    private static readonly int HashFiring = Animator.StringToHash("Firing");

    public static int HitsThisVolley; // reset by the caller, for debugging

    // Called by GoyoBossAI when it spawns the rifleman: stores the volley settings and freezes the model in its aim pose facing the target.
    public void Init(Transform target, float fireAt, float chestHeight, float aimOffsetDegrees, float despawnDelay,
                     GameObject muzzleFlash, AudioClip shotSound, float shotVolume, GameObject smoke)
    {
        this.target = target;
        this.fireAt = fireAt;
        this.chestHeight = chestHeight;
        this.aimOffsetDegrees = aimOffsetDegrees;
        this.despawnDelay = despawnDelay;
        this.muzzleFlash = muzzleFlash;
        this.shotSound = shotSound;
        this.shotVolume = shotVolume;
        this.smoke = smoke;

        anim = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "MuzzlePoint") muzzle = t;
            else if (t.name == "mixamorig:Spine2") chest = t;
        }

        // standing aim pose, held in place (no root motion, no ragdoll flop)
        if (anim != null)
        {
            anim.applyRootMotion = false;
            anim.SetBool(HashIdle, false);
            anim.SetBool(HashAiming, true);
        }
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (var col in GetComponentsInChildren<Collider>(true)) if (col.gameObject != gameObject) col.enabled = false;
        Face();
    }

    // Keeps facing the target and fires once the volley time arrives.
    private void Update()
    {
        if (fired || target == null) return;
        Face();
        if (Time.time >= fireAt) Fire();
    }

    // after the animation: turn the chest so the barrel points right at the player (the aim clip holds
    // the rifle tilted up), like Goyo's own aim correction
    private void LateUpdate()
    {
        if (fired || target == null || chest == null || muzzle == null) return;
        for (int i = 0; i < 2; i++)
        {
            Vector3 want = AimPoint() - muzzle.position;
            if (want.sqrMagnitude < 0.01f) return;
            Quaternion.FromToRotation(muzzle.forward, want.normalized).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            chest.rotation = Quaternion.AngleAxis(Mathf.Clamp(angle, -40f, 40f), axis) * chest.rotation;
        }
    }

    // The point the shot is aimed at: the player's standing chest height.
    private Vector3 AimPoint() => target.position + Vector3.up * chestHeight;

    // Turns the body toward the target on the horizontal plane, plus the aim offset.
    private void Face()
    {
        Vector3 d = target.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d) * Quaternion.Euler(0f, aimOffsetDegrees, 0f);
    }

    // Plays the shot effects and casts the bullet ray; the first thing it hits takes it, damaging the player if that's them.
    private void Fire()
    {
        fired = true;
        if (anim != null) anim.SetTrigger(HashFiring);
        Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.5f;
        if (muzzleFlash != null && muzzle != null) Destroy(Instantiate(muzzleFlash, muzzle.position, muzzle.rotation), 1f);
        if (audioSource != null && shotSound != null) audioSource.PlayOneShot(shotSound, shotVolume);

        // standing chest height: hits a standing player, passes over a crouched one
        Vector3 to = AimPoint() - origin;
        foreach (var hit in SortedHits(origin, to.normalized, to.magnitude + 0.5f))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            var player = hit.collider.GetComponentInParent<FirstPersonController>();
            if (player != null) { player.TakeHit(); HitsThisVolley++; }
            break; // first thing in the way takes it - the player, a wall, or nothing
        }
        StartCoroutine(Despawn());
    }

    // Everything along the ray, nearest first.
    private static RaycastHit[] SortedHits(Vector3 origin, Vector3 dir, float range)
    {
        var hits = Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    // After despawnDelay, puffs smoke and removes the rifleman.
    private IEnumerator Despawn()
    {
        yield return new WaitForSeconds(despawnDelay);
        if (smoke != null) Instantiate(smoke, transform.position, Quaternion.identity);
        yield return new WaitForSeconds(0.35f); // gone inside the smoke
        Destroy(gameObject);
    }
}
