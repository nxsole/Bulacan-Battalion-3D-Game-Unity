using System.Reflection;
using UnityEngine;

// Lets the player's rifle hurt the Goyo boss. FirstPersonController's shot only damages EnemyAI
// units, and it's left untouched, so this watches for the player firing (its ammo count drops) and
// casts the very same ray the shot uses - from the camera, straight ahead, at the weapon's range.
// Every shot aimed at the boss - a hit, or one passing within his near-miss radius - goes to
// GoyoBossAI.OnShotAt, which may dodge-roll (no damage) or take bodyDamage / headDamage.
// (While he's alive only his body capsule is hittable - his limb colliders are off, like every
// soldier's - so headDamage only applies if that ever changes.)
//
// The ammo count and weapon range are private on FirstPersonController, so they're read directly
// (read-only). If they're ever renamed this logs a warning once and switches itself off.
public class PlayerBossDamage : MonoBehaviour
{
    [SerializeField] private FirstPersonController player;
    [SerializeField, Min(1)] private int bodyDamage = 1;
    [SerializeField, Min(1)] private int headDamage = 2;
    [SerializeField] private bool debugHits = true;

    private static readonly FieldInfo AmmoField = typeof(FirstPersonController).GetField("currentAmmo", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo RangeField = typeof(FirstPersonController).GetField("weaponRange", BindingFlags.NonPublic | BindingFlags.Instance);

    private int lastAmmo = int.MinValue;

    // Finds the player script and checks the private fields this relies on still exist; disables itself if not.
    private void Start()
    {
        if (player == null) player = GetComponent<FirstPersonController>();
        if (player == null || AmmoField == null || RangeField == null)
        {
            Debug.LogWarning("PlayerBossDamage: can't follow the player's shots (missing player, or FirstPersonController fields renamed) - the boss can't be damaged.", this);
            enabled = false;
        }
    }

    // LateUpdate: after FirstPersonController's Update has fired (and decremented the ammo) this frame.
    private void LateUpdate()
    {
        int ammo = (int)AmmoField.GetValue(player);
        bool fired = lastAmmo != int.MinValue && ammo < lastAmmo; // reloads only ever raise it
        lastAmmo = ammo;
        if (!fired || player.PlayerCamera == null) return;

        var cam = player.PlayerCamera;
        float range = (float)RangeField.GetValue(player);
        var ray = new Ray(cam.position, cam.forward);
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, range);

        // a direct hit...
        var boss = hitSomething ? hit.collider.GetComponentInParent<GoyoBossAI>() : null;
        bool direct = boss != null;
        // ...or a near miss: the shot's path (up to whatever stopped it) passed close to him
        if (!direct)
        {
            boss = cachedBoss != null ? cachedBoss : (cachedBoss = FindFirstObjectByType<GoyoBossAI>());
            if (boss == null) return;
            Vector3 chest = boss.transform.position + Vector3.up * 1.2f;
            float along = Vector3.Dot(chest - ray.origin, ray.direction);
            float reach = hitSomething ? hit.distance : range;
            if (along <= 0f || along > reach) return;                               // behind the player, or the shot was stopped first
            if (Vector3.Distance(ray.GetPoint(along), chest) > boss.NearMissRadius) return; // not close enough to count
        }
        if (boss.IsDead) return;

        bool head = direct && hit.collider.name.Contains("Head");
        string result = boss.OnShotAt(direct, head ? headDamage : bodyDamage, transform);
        if (debugHits) Debug.Log($"[PlayerBossDamage] Shot at the boss ({(direct ? "hit" : "near miss")}): {result} - health {boss.Health}/{boss.MaxHealth}", this);
    }

    private GoyoBossAI cachedBoss;
}
