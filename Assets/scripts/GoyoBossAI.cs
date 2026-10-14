using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Boss AI for Goyo (Gregorio del Pilar). A standalone copy of EnemyAI - which is left untouched so
// Level 1 plays exactly as before - with the boss's differences layered on:
//
//  - Movement: same states and logic as a normal soldier, but the boss's Animator plays the
//    "Rifle Run" clip for every movement state (Walk and Crouch Walk), so he always runs.
//  - Aiming: he only tracks the target for the first part of each aim (aimTrackFraction, half by
//    default). After that his body and aim lock where the target WAS, and the shot is fired along
//    that locked line - a real raycast from the muzzle - so the player can dodge it by moving once
//    he stops tracking. (A normal soldier snaps onto the target before firing and a shot at the
//    player always lands.)
//
//  - Aim correction: after the animation, his upper chest turns so the barrel points exactly at his
//    aim point (the shared aim clip holds the rifle tilted up), lining up with the locked shot.
//  - Traits: he can only be hurt while reloading - any other shot at him makes him dive-roll
//    (invincible). Every 20-30 s he battle-cries and smoke-teleports, calling one of his men. At half
//    health he calls his men: turns invisible while reinforcements come from a spawner, and returns
//    once they're all dead. From then on the teleport becomes a firing line: riflemen appear in smoke,
//    aim at the player and volley at standing height (crouch to survive), then vanish (VolleyRifleman).
//    After every 2nd shot, if he can run to the player within about one reload, he rushes and strikes
//    with the Rifle Punch instead of reloading (farther away he reloads and the rush waits):
//    he can be hit during the rush (only half health stops it), and only a well-timed dash avoids the blow.
//  - Health: unlike a soldier (one hit kills), he has maxHealth and TakeDamage(). The player's gun
//    looks for EnemyAI, so it can't reach him by itself; PlayerBossDamage on the player variant
//    mirrors each shot and damages him instead (FirstPersonController is untouched).
//
// Being a separate class he keeps his own unit list, so squad alerts/spacing only involve other bosses.
[RequireComponent(typeof(Animator))]
public class GoyoBossAI : MonoBehaviour
{
    private enum State { Idle, Aim, Fire, Kneel, Reposition, Investigate, Search, TakeCover, PlayerOrder }

    [Header("Detection")]
    [Tooltip("Tags this unit treats as enemies. A unit is hostile if it carries ANY of these tags. " +
        "e.g. an Ally-tagged soldier with targetTags = [Enemy] fights enemies; set multiple to treat " +
        "several factions as hostile at once.")]
    [SerializeField] private string[] targetTags = { "Ally" };
    [SerializeField] private float detectionRadius = 40f;
    [SerializeField] private float fieldOfViewAngle = 120f;
    [SerializeField] private LayerMask lineOfSightBlockers;
    [SerializeField] private float detectionCheckInterval = 0.25f;
    [SerializeField] private float eyeHeightStanding = 1.6f;
    [SerializeField] private float eyeHeightCrouched = 0.9f;
    [SerializeField] private float targetAimHeight = 1f;
    [SerializeField] private float wallClearanceDistance = 0.5f;

    [Header("Aiming / Firing")]
    [Tooltip("Boss default is longer than a soldier's (0.4-0.9 s) so the locked half of the aim is long enough to react to.")]
    [SerializeField] private float aimDurationMin = 1.2f;
    [SerializeField] private float aimDurationMax = 1.6f;
    [SerializeField] private float turnSpeed = 8f;
    [SerializeField] private float aimRotationOffsetDegrees = 0f;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private float muzzleFlashLifetime = 1f;

    [Tooltip("Against AI soldiers standing in the locked line of fire: 0 = always miss, 1 = always hit. (The player is hit whenever they're still in the line - the dodge is the counterplay.)")]
    [SerializeField, Range(0f, 1f)] private float hitChance = 0.6f;

    [Header("Boss Aim Lock")]
    [Tooltip("Fraction of each aim spent tracking the target. After this the boss stops turning, his aim freezes on where the target was, and the shot goes down that line. 1 = tracks the whole time like a normal soldier.")]
    [SerializeField, Range(0f, 1f)] private float aimTrackFraction = 0.5f;
    [Tooltip("How far the locked shot reaches.")]
    [SerializeField] private float lockedShotRange = 120f;
    [Tooltip("What the locked shot can hit (and be stopped by). Everything by default; his own colliders are always ignored.")]
    [SerializeField] private LayerMask lockedShotMask = ~0;
    [SerializeField] private bool debugLockedShots = true;

    private bool aimLocked;
    private Vector3 lockedAimPoint;

    [Header("Boss Aim Correction")]
    [Tooltip("Upper-body bone turned after the animation so the barrel points exactly at the aim point (the aim clip holds the rifle tilted up). The builder uses mixamorig:Spine2. Empty = off.")]
    [SerializeField] private Transform aimBone;
    [Tooltip("1 = barrel exactly on the aim point, 0 = animation only.")]
    [SerializeField, Range(0f, 1f)] private float aimCorrection = 1f;
    [Tooltip("Most the chest is ever turned to line up the barrel, so extreme angles don't contort him.")]
    [SerializeField] private float maxAimCorrectionAngle = 40f;

    [Header("Boss Health")]
    [Tooltip("Damage the boss can take before he dies. The player's rifle does 1 per body hit and 2 per headshot (PlayerBossDamage on the player).")]
    [SerializeField, Min(1)] private int maxHealth = 10;
    private int currentHealth;

    [Header("Boss Trait: Dodge Roll")]
    [Tooltip("He can only be hurt while reloading (kneeling). Any other time a shot at him makes him dive-roll (if there's room) and does no damage - so the player has to wait for him to fire and reload.")]
    [SerializeField] private bool vulnerableOnlyWhileReloading = true;
    [Tooltip("Chance he dive-rolls when the player shoots at him outside a reload (a hit, or a shot passing within Near Miss Radius). He's invincible for the roll.")]
    [SerializeField, Range(0f, 1f)] private float rollChance = 1f;
    [Tooltip("A missed shot passing this close to him still counts as 'shot at' and can make him roll.")]
    [SerializeField] private float nearMissRadius = 1.5f;
    [Tooltip("Animator state that plays the Running Dive Roll clip (fired by the DiveRoll trigger).")]
    [SerializeField] private string rollStateName = "Dive Roll";
    [Tooltip("How long the roll lasts (the clip's length - the builder fills this in).")]
    [SerializeField] private float rollDuration = 2f;
    [Tooltip("He only rolls toward a side with at least this much clear NavMesh, so he never rolls into a wall.")]
    [SerializeField] private float rollClearance = 4.5f;
    [Tooltip("Seconds after a roll before he can roll again.")]
    [SerializeField] private float rollCooldown = 0.25f;

    [Header("Boss Trait: Reinforcement Phase")]
    [Tooltip("When his health falls to this fraction he calls his men: battle cry, smoke, and he turns invisible (hidden, inactive, invincible). Reinforcement Count soldiers come from Reinforcement Spawner, and he only comes back - in smoke, ready to fight - once they're all dead.")]
    [SerializeField, Range(0f, 1f)] private float reinforcementAtHealth = 0.5f;
    [Tooltip("Spawner (spawn points around the map + a Filipino soldier template set to target the player). Scene object - set in each scene.")]
    [SerializeField] private Level2WaveSpawner reinforcementSpawner;
    [SerializeField, Min(1)] private int reinforcementCount = 5;

    [Tooltip("Soldiers called from Reinforcement Spawner each time he smoke-teleports.")]
    [SerializeField, Min(0)] private int soldiersPerTeleport = 1;

    [Header("Boss Trait: Firing Line (at/below Reinforcement At Health)")]
    [Tooltip("From half health on this replaces the smoke teleport, on the same cooldown: he shouts the order and Volley Count riflemen appear in smoke in a line, already aiming at the player. They track him, fire together after Volley Aim Time at standing chest height - crouch to get under it - and vanish in smoke.")]
    [SerializeField] private GameObject volleySoldierPrefab;
    [SerializeField, Min(1)] private int volleyCount = 6;
    [Tooltip("How far from the player the line appears.")]
    [SerializeField] private float volleyDistance = 16f;
    [SerializeField] private float volleySpacing = 2f;
    [Tooltip("Seconds from the line appearing to the volley - the player's time to crouch.")]
    [SerializeField] private float volleyAimTime = 2f;
    [Tooltip("Height above the player's feet the volley is aimed at. Standing the player is 2 m tall, crouched 1 m, so anything between clears a crouch.")]
    [SerializeField] private float volleyChestHeight = 1.4f;
    [Tooltip("Seconds after firing before the riflemen vanish in smoke.")]
    [SerializeField] private float volleyDespawnDelay = 0.8f;
    [Tooltip("Seconds between firing lines (its own cooldown - quicker than the teleport's).")]
    [SerializeField] private float volleyCooldownMin = 10f;
    [SerializeField] private float volleyCooldownMax = 14f;
    [Tooltip("Quick puff the riflemen appear in (shorter than the teleport smoke). Empty = use Smoke Prefab.")]
    [SerializeField] private GameObject volleySpawnSmokePrefab;

    [Header("Boss Trait: Melee Rush")]
    [Tooltip("After every this-many shots he skips the reload and rushes the player instead, then strikes with the Rifle Punch. Nothing stops the rush (no rolls) except dropping to half health, and shots DO hurt him while he rushes. The blow lands at Melee Impact Time; he stops tracking Melee Commit Lead before that - dash after he commits to get clear. 0 = never.")]
    [SerializeField, Min(0)] private int shotsPerMelee = 2;
    [Tooltip("He stops running and starts the strike this close to the player.")]
    [SerializeField] private float meleeStartDistance = 1.8f;
    [Tooltip("The blow hits a player within this distance... (the builder sets it from the punch's reach: the rifle's full extension plus the player's width)")]
    [SerializeField] private float meleeRange = 3.2f;
    [Tooltip("During the wind-up he steps after a player who backs off, up to this speed, so only a dash after he commits gets clear.")]
    [SerializeField] private float meleeWindupStepSpeed = 4.5f;
    [Tooltip("...and within this angle either side of the direction he committed to.")]
    [SerializeField] private float meleeConeAngle = 50f;
    [Tooltip("Seconds into the punch animation the blow lands (the builder measures it from the clip).")]
    [SerializeField] private float meleeImpactTime = 1.1f;
    [Tooltip("He stops turning toward the player this long before the blow - the dash window. Dash earlier and he follows you; dash after and you're clear.")]
    [SerializeField] private float meleeCommitLead = 0.3f;
    [Tooltip("How long the whole strike takes before he goes back to fighting (the builder sets it from the clip).")]
    [SerializeField] private float meleeDuration = 2.5f;
    [Tooltip("He gives up the rush if he hasn't reached the player in this time.")]
    [SerializeField] private float rushTimeout = 6f;
    [Tooltip("He only rushes if his run would reach the player within this many seconds; farther away he reloads as usual and the rush waits for a later shot from closer in. 0 = one reload's worth (Kneel Reload Duration).")]
    [SerializeField, Min(0f)] private float rushReachSeconds = 0f;

    private int shotsSinceMelee;
    private float runSpeed; // m/s his Rifle Run actually moves him - estimated from the clip, then measured while he runs
    private bool inMeleeRush;
    private static readonly int HashMelee = Animator.StringToHash("Melee");
    public bool IsRushing => inMeleeRush;

    private bool reinforcementPhaseUsed, inReinforcementPhase;
    private Transform phaseTarget;
    public bool InReinforcementPhase => inReinforcementPhase;
    public int ReinforcementsAlive => reinforcementSpawner != null ? reinforcementSpawner.AliveCount : 0;

    [Header("Boss Trait: Battle Cry + Smoke Teleport")]
    [Tooltip("Once he's engaged the player, he uses this every Min-Max seconds: shouts (vulnerable - the tell), smoke goes up, he vanishes and reappears elsewhere in smoke.")]
    [SerializeField] private float teleportCooldownMin = 20f;
    [SerializeField] private float teleportCooldownMax = 30f;
    [Tooltip("Seconds after first spotting the player before the first teleport.")]
    [SerializeField] private float firstTeleportDelay = 12f;
    [Tooltip("Shout before the teleport (one picked at random). Placeholder until a Goyo battle cry is recorded.")]
    [SerializeField] private AudioClip[] teleportCryClips;
    [SerializeField, Range(0f, 1f)] private float teleportCryVolume = 1f;
    [Tooltip("Smoke cloud spawned where he vanishes and where he reappears.")]
    [SerializeField] private GameObject smokePrefab;
    [Tooltip("Where he reappears: this far from the player (min/max), and at least Min From Old Spot away from where he was.")]
    [SerializeField] private float teleportMinFromPlayer = 12f;
    [SerializeField] private float teleportMaxFromPlayer = 22f;
    [SerializeField] private float teleportMinFromOldSpot = 10f;
    [Tooltip("Seconds between the smoke going up and him disappearing inside it.")]
    [SerializeField] private float vanishDelay = 0.4f;
    [Tooltip("Seconds he stays hidden in the arrival smoke before reappearing.")]
    [SerializeField] private float reappearDelay = 0.6f;
    [SerializeField] private bool debugTraits = true;

    private bool invincible;
    private bool busyWithTrait;
    private float nextRollAt;
    private float nextTeleportAt = -1f;
    private Renderer[] bodyRenderers;
    private static readonly int HashDiveRoll = Animator.StringToHash("DiveRoll");

    public int MaxHealth => maxHealth;
    public int Health => currentHealth;
    public bool IsDead => isDead;
    public bool IsInvincible => invincible;
    public float NearMissRadius => nearMissRadius;

    // Boss damage (normal soldiers just Die() in one hit). Being shot also tells him where the shooter
    // is, so he doesn't stand idle while taking hits from behind. No damage while invincible (rolling,
    // or inside the teleport smoke).
    public void TakeDamage(int amount, Transform attacker = null)
    {
        if (isDead || amount <= 0 || invincible) return;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        if (currentHealth == 0) { Die(); return; }
        if (!reinforcementPhaseUsed && currentHealth <= Mathf.CeilToInt(maxHealth * reinforcementAtHealth))
        {
            reinforcementPhaseUsed = true;
            phaseTarget = attacker != null ? attacker : currentTarget;
            StartTrait(ReinforcementPhase());
            return;
        }
        if (attacker != null && currentTarget == null) ReceiveAlert(attacker, attacker.position);
    }

    // Called by the player's gun (PlayerBossDamage) for every shot aimed at him - a hit, or a near miss.
    // Returns what happened, for feedback/debug.
    public string OnShotAt(bool hit, int damage, Transform shooter)
    {
        if (isDead) return "dead";
        if (invincible) return hit ? "hit while invincible - no damage" : "near miss while invincible";

        // the rush: he doesn't dodge or stop, and he can be hurt (half health still interrupts it, via TakeDamage)
        if (inMeleeRush)
        {
            if (hit) { TakeDamage(damage, shooter); return $"HIT DURING RUSH for {damage}"; }
            return "near miss (he's rushing)";
        }

        // the damage window: kneeling to reload
        bool reloading = state == State.Kneel && !busyWithTrait;
        if (vulnerableOnlyWhileReloading && reloading)
        {
            if (hit) { TakeDamage(damage, shooter); return $"HIT WHILE RELOADING for {damage}"; }
            return "near miss (he's reloading)";
        }

        if (!busyWithTrait && Time.time >= nextRollAt && Random.value < rollChance && TryPickRollDirection(shooter, out Vector3 dir))
        {
            StartTrait(DiveRoll(dir));
            return hit ? "DODGED - dive roll (invincible), no damage" : "near miss - dive roll";
        }

        if (shooter != null && currentTarget == null) ReceiveAlert(shooter, shooter.position);
        if (vulnerableOnlyWhileReloading) return hit ? "no damage - he can only be hurt while reloading" : "near miss";
        if (hit) { TakeDamage(damage, shooter); return $"hit for {damage}"; }
        return "near miss";
    }

    // ---------------------------------------------------------------- boss traits
    // A trait interrupts whatever he was doing (aiming, reloading, moving), runs, then hands back to the
    // normal state machine - straight into aiming if he still has the target.
    private void StartTrait(IEnumerator trait)
    {
        StopAllCoroutines(); // ends the running state and its waits (including a rush, if half health cut it short)
        busyWithTrait = true;
        aimLocked = false;
        inMeleeRush = false;
        // Not aiming any more: the head look-at IK and the chest aim correction key off state Aim/Fire, and a
        // trait usually interrupts right after a shot - left as Fire, they'd keep his head and chest twisted
        // toward the player while he runs, rolls or shouts. RunTrait sets the real state when it hands back.
        state = State.Idle;
        animator.SetBool(HashAiming, false);
        animator.SetBool(HashKneel, false);
        animator.SetBool(HashWalking, false);
        SetCrouching(false);
        StopAgentMovement();
        behaviorRoutine = StartCoroutine(RunTrait(trait));
    }

    private IEnumerator RunTrait(IEnumerator trait)
    {
        yield return trait;
        busyWithTrait = false;
        invincible = false;
        SetBodyVisible(true);
        state = currentTarget != null && TargetStillValid() ? State.Aim : State.Search;
        yield return BehaviorLoop();
    }

    // Roll sideways, across the shooter's line of fire, toward whichever side has clear NavMesh; if both
    // sides are blocked, a diagonal away from the shooter, then straight away from them.
    private bool TryPickRollDirection(Transform shooter, out Vector3 dir)
    {
        dir = Vector3.zero;
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit start, 1f, NavMesh.AllAreas)) return false;
        Vector3 from = shooter != null ? shooter.position : transform.position - transform.forward;
        Vector3 away = transform.position - from; away.y = 0f;
        away = away.sqrMagnitude < 0.01f ? transform.forward : away.normalized;
        Vector3 side = Vector3.Cross(Vector3.up, away);
        if (Random.value < 0.5f) side = -side;
        Vector3[] candidates = { side, -side, (side + away).normalized, (-side + away).normalized, away };
        foreach (var candidate in candidates)
        {
            if (NavMesh.Raycast(start.position, start.position + candidate * rollClearance, out _, NavMesh.AllAreas)) continue; // wall or edge that way
            dir = candidate;
            return true;
        }
        return false;
    }

    private IEnumerator DiveRoll(Vector3 dir)
    {
        invincible = true;
        transform.rotation = Quaternion.LookRotation(dir); // the clip dives forward; face the way to go
        animator.SetTrigger(HashDiveRoll);
        if (debugTraits) Debug.Log("[Boss] Dive roll (invincible).", this);
        yield return new WaitForSeconds(rollDuration);
        invincible = false;
        nextRollAt = Time.time + rollCooldown;
        SnapOntoNavMesh();
    }

    private void Start()
    {
        if (reinforcementSpawner != null) reinforcementSpawner.Spawned += SendAtPlayer;
        runSpeed = EstimateRunSpeed();
    }

    // His movement is root motion from the run clip, not the agent's speed - start from the clip's own
    // average speed; OnAnimatorMove then refines it from how fast he actually runs.
    private float EstimateRunSpeed()
    {
        float best = 0f;
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip != null && clip.name.Contains("Run"))
                    best = Mathf.Max(best, new Vector3(clip.averageSpeed.x, 0f, clip.averageSpeed.z).magnitude * (animator.isHuman ? animator.humanScale : 1f));
        return best > 0.5f ? best : 4f;
    }

    // close enough that his run reaches the player in about one reload
    private bool CloseEnoughToRush(Transform target)
    {
        if (target == null) return false;
        float seconds = rushReachSeconds > 0f ? rushReachSeconds : kneelReloadDuration;
        float reach = runSpeed * seconds + meleeStartDistance;
        float distance = FlatDistance(transform.position, target.position);
        if (debugTraits && distance > reach) Debug.Log($"[Boss] Too far to rush ({distance:F1} m > {reach:F1} m) - reloading instead.", this);
        return distance <= reach;
    }

    private void OnDestroy()
    {
        if (reinforcementSpawner != null) reinforcementSpawner.Spawned -= SendAtPlayer;
    }

    private bool AtOrBelowHalf => currentHealth <= Mathf.CeilToInt(maxHealth * reinforcementAtHealth);

    private void Update()
    {
        if (isDead || busyWithTrait) return;
        if (currentTarget != null && nextTeleportAt < 0f) nextTeleportAt = Time.time + firstTeleportDelay; // first engagement
        // his periodic ability, between actions and never mid-shot: the smoke teleport, or from half
        // health on the firing line instead
        if (nextTeleportAt > 0f && Time.time >= nextTeleportAt && currentTarget != null && state != State.Fire)
            StartTrait(AtOrBelowHalf && volleySoldierPrefab != null ? FiringLine() : BattleCryTeleport());
    }

    private IEnumerator BattleCryTeleport()
    {
        nextTeleportAt = Time.time + Random.Range(teleportCooldownMin, teleportCooldownMax);
        Transform target = currentTarget;
        yield return CryOut(target);
        if (reinforcementSpawner != null && soldiersPerTeleport > 0)
            reinforcementSpawner.SpawnWave(soldiersPerTeleport, 0); // one of his men joins in (SendAtPlayer points him at the player)
        yield return VanishInSmoke();
        yield return ReappearInSmoke(target, "Smoke teleport");
    }

    // From half health: the firing line. He shouts the order, the riflemen appear and take it from there;
    // he goes straight back to fighting.
    private IEnumerator FiringLine()
    {
        nextTeleportAt = Time.time + Random.Range(volleyCooldownMin, volleyCooldownMax);
        Transform target = currentTarget;
        yield return CryOut(target); // the order
        if (target == null || !TryPlanFiringLine(target, out var spots)) { if (debugTraits) Debug.Log("[Boss] Firing line: no room for the line this time.", this); yield break; }

        float fireAt = Time.time + volleyAimTime;
        VolleyRifleman.HitsThisVolley = 0;
        foreach (var spot in spots)
        {
            if (volleySpawnSmokePrefab != null) Instantiate(volleySpawnSmokePrefab, spot, Quaternion.identity);
            else SpawnSmoke(spot);
            SpawnRifleman(spot, target, fireAt);
        }
        if (debugTraits) Debug.Log($"[Boss] Firing line: {spots.Count} riflemen, volley in {volleyAimTime:F1}s - crouch!", this);
    }

    // The rush: run straight at the player (Rifle Run, re-pathing as they move), then the Rifle Punch.
    // He tracks the player through the wind-up and commits meleeCommitLead before the blow; at the
    // blow, a player still within meleeRange and inside the cone he committed to is hit. Only a dash
    // after the commit gets clear - walking is far too slow.
    private IEnumerator MeleeRush()
    {
        inMeleeRush = true;
        Transform target = currentTarget;
        if (debugTraits) Debug.Log("[Boss] Melee rush!", this);

        // 1. the charge. Crossfade straight into the run: if he fired from a crouch, the Animator's Crouch
        // Fire state only exits into the kneeling reload (which the rush skips), so the Walking flag alone
        // would leave him frozen in place.
        animator.SetBool(HashIdle, false);
        animator.SetBool(HashWalking, true);
        SetCrouching(false);
        animator.CrossFadeInFixedTime(walkStateName, 0.15f);
        if (agent != null && agent.isOnNavMesh) { ResumeAgentMovement(); agent.speed = repositionSpeed; }
        float t = 0f, repath = 0f;
        while (target != null && t < rushTimeout && FlatDistance(transform.position, target.position) > meleeStartDistance)
        {
            if (repath <= 0f && agent != null && agent.isOnNavMesh) { agent.SetDestination(target.position); repath = 0.2f; }
            SteerTowardAgentPath();
            t += Time.deltaTime; repath -= Time.deltaTime;
            yield return null;
        }
        animator.SetBool(HashWalking, false);
        StopAgentMovement();
        if (target == null || FlatDistance(transform.position, target.position) > meleeRange * 2f)
        {
            if (debugTraits) Debug.Log("[Boss] Melee rush gave up (couldn't reach the player).", this);
            inMeleeRush = false;
            yield break;
        }

        // 2. the strike: through the wind-up he turns to follow the player and steps after them (the punch
        // clip itself doesn't travel), so backing off doesn't work; then he commits
        animator.SetTrigger(HashMelee);
        Vector3 committed = transform.forward;
        float s = 0f;
        while (s < meleeImpactTime)
        {
            if (s < meleeImpactTime - meleeCommitLead)
            {
                Vector3 to = target.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), turnSpeed * 2f * Time.deltaTime);
                committed = transform.forward;
                float gap = to.magnitude - meleeStartDistance;
                if (gap > 0f) StepToward(to.normalized * Mathf.Min(gap, meleeWindupStepSpeed * Time.deltaTime));
            }
            s += Time.deltaTime;
            yield return null;
        }

        // 3. the blow
        Vector3 toPlayer = target.position - transform.position; toPlayer.y = 0f;
        bool connects = toPlayer.magnitude <= meleeRange && Vector3.Angle(committed, toPlayer) <= meleeConeAngle;
        var player = target.GetComponentInParent<FirstPersonController>();
        if (connects && player != null) player.TakeHit();
        if (debugTraits) Debug.Log(connects ? $"[Boss] Melee HIT the player ({toPlayer.magnitude:F1} m, {Vector3.Angle(committed, toPlayer):F0} deg)." :
                                              $"[Boss] Melee dodged ({toPlayer.magnitude:F1} m, {Vector3.Angle(committed, toPlayer):F0} deg off his swing).", this);

        yield return new WaitForSeconds(Mathf.Max(0f, meleeDuration - meleeImpactTime)); // finish the swing
        inMeleeRush = false;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // a small step during the strike's wind-up, kept on the NavMesh
    private void StepToward(Vector3 step)
    {
        Vector3 next = transform.position + step;
        if (NavMesh.SamplePosition(next, out NavMeshHit hit, 0.5f, NavMesh.AllAreas)) next = hit.position;
        else return; // edge or wall - don't step off the walkable area
        transform.position = next;
        if (agent != null) agent.nextPosition = next;
    }

    // A row of NavMesh spots volleyDistance from the player, across his line of sight to them; prefers a
    // place with a clear view of the player.
    private bool TryPlanFiringLine(Transform target, out System.Collections.Generic.List<Vector3> spots)
    {
        spots = null;
        System.Collections.Generic.List<Vector3> fallback = null;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector2 r = Random.insideUnitCircle.normalized;
            Vector3 toLine = new Vector3(r.x, 0f, r.y);
            if (!NavMesh.SamplePosition(target.position + toLine * volleyDistance, out NavMeshHit c, 3f, NavMesh.AllAreas)) continue;
            Vector3 across = Vector3.Cross(Vector3.up, toLine);
            var row = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < volleyCount; i++)
            {
                Vector3 want = c.position + across * ((i - (volleyCount - 1) * 0.5f) * volleySpacing);
                if (NavMesh.SamplePosition(want, out NavMeshHit h, 1.5f, NavMesh.AllAreas)) row.Add(h.position);
            }
            if (row.Count < volleyCount) continue;
            bool clear = lineOfSightBlockers.value == 0 ||
                         !Physics.Linecast(c.position + Vector3.up * eyeHeightStanding, target.position + Vector3.up * volleyChestHeight, lineOfSightBlockers);
            if (clear) { spots = row; return true; }
            if (fallback == null) fallback = row;
        }
        spots = fallback;
        return spots != null;
    }

    // The soldier model, stripped of its AI before it ever runs (built inside an inactive holder), then
    // handed to VolleyRifleman.
    private void SpawnRifleman(Vector3 at, Transform target, float fireAt)
    {
        var holder = new GameObject("Volley holder");
        holder.SetActive(false);
        var go = Instantiate(volleySoldierPrefab, at, Quaternion.identity, holder.transform);
        go.name = "Volley Rifleman";
        foreach (var f in go.GetComponents<AllyPathFollower>()) DestroyImmediate(f); // needs EnemyAI, so it goes first
        foreach (var ai in go.GetComponents<EnemyAI>()) DestroyImmediate(ai);
        foreach (var nav in go.GetComponents<NavMeshAgent>()) DestroyImmediate(nav);
        go.tag = "Untagged"; // nobody's target
        go.transform.SetParent(null, true);
        Destroy(holder);
        go.SetActive(true);
        go.AddComponent<VolleyRifleman>().Init(target, fireAt, volleyChestHeight, aimRotationOffsetDegrees, volleyDespawnDelay,
                                              muzzleFlashPrefab, gunShotSound, gunShotVolume, smokePrefab);
    }

    // At reinforcementAtHealth: he calls his men and turns invisible until they're all dead.
    private IEnumerator ReinforcementPhase()
    {
        inReinforcementPhase = true;
        invincible = true; // from the moment it starts - nothing can finish him early
        Transform target = phaseTarget != null ? phaseTarget : currentTarget;
        if (debugTraits) Debug.Log("[Boss] Half health - calling his men!", this);
        yield return CryOut(target);
        yield return VanishInSmoke();

        if (reinforcementSpawner != null)
        {
            reinforcementSpawner.SpawnWave(reinforcementCount, 1); // SendAtPlayer (hooked up in Start) points each at the player
            yield return null;
            while (reinforcementSpawner.IsSpawning || reinforcementSpawner.AliveCount > 0)
                yield return new WaitForSeconds(0.25f);
            if (debugTraits) Debug.Log("[Boss] His men are all down - he returns.", this);
        }
        else
        {
            Debug.LogWarning("GoyoBossAI: no Reinforcement Spawner set in this scene - he just vanishes briefly instead.", this);
            yield return new WaitForSeconds(3f);
        }

        yield return ReappearInSmoke(target, "Returns");
        inReinforcementPhase = false;
        // from here on his ability is the firing line, on its own (quicker) cooldown
        nextTeleportAt = volleySoldierPrefab != null
            ? Time.time + Random.Range(volleyCooldownMin, volleyCooldownMax)
            : Time.time + Random.Range(teleportCooldownMin, teleportCooldownMax);
    }

    // Each reinforcement starts out knowing where the player is, so they come looking instead of
    // standing at the map edge waiting to happen to see him.
    private void SendAtPlayer(GameObject soldier)
    {
        var ai = soldier.GetComponent<EnemyAI>();
        var target = phaseTarget != null ? phaseTarget : currentTarget;
        if (ai != null && target != null) ai.ReceiveAlert(target, target.position);
    }

    // The battle cry: standing, facing the player.
    private IEnumerator CryOut(Transform target)
    {
        if (target != null)
        {
            Vector3 face = target.position - transform.position; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
        }
        animator.SetBool(HashIdle, true);
        animator.CrossFadeInFixedTime(idleStateName, 0.15f); // stand up from whatever he was doing (crouch states don't all lead back to Idle)
        var cry = teleportCryClips != null && teleportCryClips.Length > 0 ? teleportCryClips[Random.Range(0, teleportCryClips.Length)] : null;
        if (cry != null && audioSource != null) audioSource.PlayOneShot(cry, teleportCryVolume);
        if (debugTraits) Debug.Log("[Boss] Battle cry...", this);
        yield return new WaitForSeconds(cry != null ? Mathf.Clamp(cry.length, 1f, 2.5f) : 1.2f);
    }

    // Smoke goes up and he disappears inside it (invincible from here).
    private IEnumerator VanishInSmoke()
    {
        invincible = true;
        SpawnSmoke(transform.position);
        yield return new WaitForSeconds(vanishDelay);
        SetBodyVisible(false);
    }

    // Reappear somewhere around the player, in smoke, then a breath before he can be hurt again.
    private IEnumerator ReappearInSmoke(Transform target, string what)
    {
        Vector3 dest = PickTeleportPoint(target);
        if (agent != null && agent.isOnNavMesh) agent.Warp(dest);
        transform.position = dest;
        var body = GetComponent<Rigidbody>();
        if (body != null) body.position = dest; // keep physics in step with the jump
        if (target != null)
        {
            Vector3 face = target.position - dest; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
        }
        SpawnSmoke(dest);
        if (debugTraits) Debug.Log($"[Boss] {what} at {dest}.", this);
        yield return new WaitForSeconds(reappearDelay);
        SetBodyVisible(true);
        yield return new WaitForSeconds(0.3f);
        invincible = false;
    }

    // A NavMesh point around the player, at a fighting distance and well away from where he was;
    // prefers spots with a clear line of sight to the player so he can resume shooting.
    private Vector3 PickTeleportPoint(Transform target)
    {
        Vector3 centre = target != null ? target.position : transform.position;
        Vector3 best = transform.position;
        float bestScore = float.MinValue;
        for (int i = 0; i < 24; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(teleportMinFromPlayer, teleportMaxFromPlayer);
            if (!NavMesh.SamplePosition(centre + new Vector3(r.x, 0f, r.y), out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            Vector3 p = hit.position;
            float fromOld = Vector3.Distance(p, transform.position);
            if (fromOld < teleportMinFromOldSpot) continue;
            float score = fromOld * 0.1f;
            if (target != null && lineOfSightBlockers.value != 0 &&
                !Physics.Linecast(p + Vector3.up * eyeHeightStanding, target.position + Vector3.up * targetAimHeight, lineOfSightBlockers))
                score += 10f; // can see the player from there
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    private void SpawnSmoke(Vector3 at)
    {
        if (smokePrefab != null) Instantiate(smokePrefab, at, Quaternion.identity);
    }

    // Hidden = not drawn and not hittable (shots pass through the smoke rather than striking an
    // invisible body). The ragdoll colliders stay as they are - they're off while he's alive anyway.
    // His root Rigidbody has gravity and rests on the ground through that body collider, so it's frozen
    // while the collider is off - otherwise he falls through the world while hidden.
    private void SetBodyVisible(bool visible)
    {
        if (bodyRenderers == null) bodyRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in bodyRenderers) if (r != null) r.enabled = visible;

        var rb = GetComponent<Rigidbody>();
        if (rb != null && !visible && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
            frozeRootBody = true;
        }
        if (bodyCapsule != null) bodyCapsule.enabled = visible;
        if (rb != null && visible && frozeRootBody)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            frozeRootBody = false;
        }
    }

    private bool frozeRootBody;

    private void SnapOntoNavMesh()
    {
        if (agent == null || !NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas)) return;
        agent.Warp(hit.position);
        transform.position = hit.position;
    }

    [SerializeField] private AudioClip gunShotSound;
    [SerializeField, Range(0f, 1f)] private float gunShotVolume = 1.0f;
    [SerializeField] private AudioSource audioSource;

    [Header("IK Aiming")]
    [SerializeField] private float bodyWeight = 0.5f;
    [SerializeField] private float headWeight = 1.0f;
    [SerializeField] private float clampWeight = 0.5f;
    [SerializeField] private float ikTransitionSpeed = 5f;
    private float currentIKWeight = 0f;
    private Vector3 currentLookPos;

    [Header("Procedural Recoil")]
    [SerializeField] private Transform weaponRoot;
    [SerializeField] private Vector3 maxRecoilAngles = new Vector3(-10f, 2f, 0f);
    [SerializeField] private float recoilSnap = 25f;
    [SerializeField] private float recoilReturnSpeed = 5f;
    private Vector3 currentRecoilRotation;
    private Vector3 targetRecoilRotation;

    [Header("Kneel / Reload")]
    [SerializeField] private float kneelReloadDuration = 1.5f;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField, Range(0f, 1f)] private float reloadVolume = 0.4f;

    [Header("Battleline Repositioning")]
    [Range(0f, 1f)]
    [SerializeField] private float repositionChance = 0.6f;
    [SerializeField] private float minRepositionDistance = 2f;
    [SerializeField] private float repositionRadius = 8f;
    [SerializeField] private float repositionSpeed = 3.5f;
    [SerializeField] private bool pressAdvantage = true;
    [SerializeField] private float optimalCombatRange = 15f; // The distance they will try to form a line at
    [SerializeField] private float advanceScoreBonus = 5f;

    [Header("Spacing (avoid clumping in groups)")]
    [SerializeField] private float minSeparationDistance = 3f;
    [SerializeField] private int repositionCandidateAttempts = 8;
    [Tooltip("How strongly units actively push away from allies that get too close while moving, on top of picking spaced-out destinations. NavMeshAgent's own local avoidance can't be relied on here since updatePosition is false.")]
    [SerializeField] private float allyAvoidanceStrength = 2f;

    [Header("Alert / Squad Communication")]
    [SerializeField] private float alertShareRadius = 20f;
    [SerializeField] private float alertExpireTime = 8f;
    [SerializeField] private float investigateArrivalDistance = 1.5f;

    [Header("Cover Seeking")]
    [Tooltip("When reloading, first try to move to a spot that blocks the target's sightline to us instead of reloading out in the open.")]
    [SerializeField] private bool seekCoverBeforeReload = true;
    [SerializeField] private float coverSearchRadius = 12f;
    [SerializeField] private int coverCandidateAttempts = 10;
    [SerializeField] private float coverArrivalDistance = 0.75f;

    [Header("Player Move Orders")]
    [Tooltip("Reused for both teams via targetTags: if any unit carrying one of our target tags is within this radius of the ordered point, the point counts as contested and we hold at optimalCombatRange from the nearest one instead of walking in.")]
    [SerializeField] private float orderContestedCheckRadius = 12f;
    [SerializeField] private float orderRecheckInterval = 0.75f;
    [SerializeField] private float orderArrivalDistance = 1f;
    [Tooltip("Chance (0-1) a troop crouch-walks rather than stands for a given move order. Rolled once per order, so the whole trip is one stance.")]
    [SerializeField, Range(0f, 1f)] private float orderCrouchWalkChance = 0.5f;

    [Header("Search (Alert State)")]
    [SerializeField] private float searchDuration = 30f;
    [SerializeField] private float searchRadius = 8f;
    [SerializeField] private float searchWaitTime = 2f;

    [Header("Crouch Collider Adjustment")]
    [SerializeField] private float standingColliderHeight = 1.8f;
    [SerializeField] private float crouchedColliderHeight = 1.2f;
    [SerializeField] private float standingColliderCenterY = 0.9f;
    [SerializeField] private float crouchedColliderCenterY = 0.6f;

    [Header("Enemy Spotted Audio")]
    [Tooltip("Played the FIRST time any allied troop (units sharing this one's tag) spots an enemy. Fires once for the whole squad, not per-unit. Leave empty for no bark.")]
    [SerializeField] private AudioClip[] enemySpottedSounds;
    [SerializeField, Range(0f, 1f)] private float enemySpottedVolume = 1f;
    [Tooltip("Only units with this tag trigger/hear the spotted bark. Set to your ally tag so enemies spotting the player don't fire it.")]
    [SerializeField] private string spottedAudioTag = "Ally";

    [Header("Battle Cry Audio")]
    [Tooltip("Squad-wide cry played once, right after the enemy-spotted bark, to answer it when the fight begins. Fires once for the squad. Leave empty to skip.")]
    [SerializeField] private AudioClip[] battleCryReplySounds;
    [Tooltip("Extra gap (seconds) added AFTER the triggering clip finishes before the squad reply cry plays. The reply now waits for the spotted clip to actually end, then waits this much longer.")]
    [SerializeField] private float battleCryReplyDelay = 0.2f;
    [Tooltip("Reply cries make EVERY ally shout. This is the max extra random delay (seconds) each unit adds, so voices scatter across a short window instead of hitting in perfect unison (which sounds like one flanged clip, not a crowd). ~0.3-0.6 reads as a natural ragged chorus.")]
    [SerializeField] private float replyCryMaxStagger = 0.4f;
    [Tooltip("Random pitch shift (+/-) applied per voice so the same clips sound like different people. ~0.1 is subtle, ~0.2 is noticeably varied.")]
    [SerializeField, Range(0f, 0.5f)] private float replyCryPitchVariation = 0.12f;
    [Tooltip("Occasional individual cries while a unit is in active combat. Each unit rolls independently on a cooldown. Leave empty to skip.")]
    [SerializeField] private AudioClip[] battleCryCombatSounds;
    [SerializeField, Range(0f, 1f)] private float battleCryVolume = 1f;
    [Tooltip("Minimum seconds between one unit's individual combat cries.")]
    [SerializeField] private float battleCryMinInterval = 6f;
    [Tooltip("Maximum seconds between one unit's individual combat cries.")]
    [SerializeField] private float battleCryMaxInterval = 14f;
    [Tooltip("Chance (0-1) that an eligible cry actually plays when its cooldown elapses, so cries feel sporadic rather than metronomic.")]
    [SerializeField, Range(0f, 1f)] private float battleCryChance = 0.6f;

    [Header("Death / Ragdoll")]
    [SerializeField] private float ragdollDisappearDelay = 10f;
    [SerializeField] private AudioClip[] deathSounds;
    [SerializeField, Range(0f, 1f)] private float deathVolume = 1f;
    [Tooltip("Layer the corpse's ragdoll pieces are moved to on death, so it can be walked over. " +
        "Create a 'Corpse' layer and, in Edit > Project Settings > Physics, uncheck its collision " +
        "with the Player and NPC layers (keep it checked against ground/environment layers so " +
        "the ragdoll still settles onto the floor).")]
    [SerializeField] private string corpseLayerName = "Corpse";
    [Tooltip("Seconds after death to let the ragdoll flop and settle on the ground before ALL its " +
        "colliders are disabled. After this, the corpse has no colliders at all (it just rests as " +
        "visual mesh). Keep it under the ragdoll disappear delay. Set to 0 to disable colliders " +
        "instantly on death (the body may sink through the floor).")]
    [SerializeField] private float corpseColliderSettleDelay = 3f;
    [Tooltip("Logs a one-time report on death showing whether the Corpse layer and Physics collision matrix are set up so corpses pass through living units. Turn off once confirmed working.")]
    [SerializeField] private bool corpseSetupDiagnostics = true;

    [Header("Animator State Names")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string kneelStateName = "Kneel";
    [SerializeField] private string walkStateName = "Walk";
    [SerializeField] private string crouchWalkStateName = "Crouch Walk";
    [SerializeField] private string crouchIdleStateName = "Crouch Idle";
    [SerializeField] private float stateWaitTimeout = 5f;

    private Animator animator;
    private NavMeshAgent agent;
    private CapsuleCollider bodyCapsule;
    private CharacterController characterController;
    private Transform currentTarget;
    private State state = State.Idle;
    private Coroutine behaviorRoutine;
    private bool isCrouchedStance;
    private Transform alertedTarget;
    private Vector3 alertedPosition;
    private float alertedTime = -Mathf.Infinity;
    private bool isDead;
    private Rigidbody[] ragdollRigidbodies;
    private Collider[] ragdollColliders;

    private Vector3 lastKnownPosition = Vector3.zero;
    private Vector3 pendingCoverDestination;

    private float nextBattleCryTime = -Mathf.Infinity; // per-unit cooldown gate for individual combat cries

    private bool hasOrder;
    private bool orderIsFresh; // true only from when an order arrives until the unit first acts on it
    private bool orderCrouchWalk; // stance chosen once per order: true = crouch-walk, false = stand
    private Vector3 orderPoint;

    // True while a move order is being carried out. AllyPathFollower watches this: when it flips
    // back to false the unit has arrived at the current waypoint, so the follower issues the next.
    // Note it stays true while the unit peels off to fight (EnemyAI resumes the order after
    // combat), so the follower correctly waits through fights rather than skipping a waypoint.
    public bool HasActiveOrder => hasOrder;

    // Golden angle in radians - the Vogel/sunflower spiral constant. Placing point i at
    // radius = spacing * sqrt(i) and angle = i * goldenAngle gives an even spread with a
    // guaranteed minimum gap between neighbors, so units executing the same order fan out
    // into a formation by construction instead of all independently heading for one point.
    private const float GoldenAngle = 2.39996323f;

    private static readonly System.Collections.Generic.List<GoyoBossAI> ActiveEnemies = new System.Collections.Generic.List<GoyoBossAI>();

    // Fired whenever any unit dies, AFTER it's removed from ActiveEnemies (so counts are accurate
    // when handlers run). The argument is the tag of the unit that died. GameOutcomeManager
    // subscribes to this to detect when the last ally has fallen.
    public static event System.Action<string> OnAnyUnitDied;

    // Number of living units currently carrying the given tag. Used by the outcome manager to
    // check whether any allies remain.
    public static int CountLivingWithTag(string tag)
    {
        int n = 0;
        foreach (var u in ActiveEnemies)
        {
            if (u != null && !u.isDead && HasTag(u, tag)) n++;
        }
        return n;
    }

    // Tracks, per tag, whether that side has already played its "enemy spotted" bark, so the
    // whole squad only shouts once rather than every unit firing on the same frame. Keyed by tag
    // so allies and enemies (if you ever give enemies a bark too) track independently.
    private static readonly System.Collections.Generic.Dictionary<string, bool> SpottedBarkPlayed = new System.Collections.Generic.Dictionary<string, bool>();

    // Per-tag flag for the one-time squad reply cry that answers the spotted bark when a fight begins.
    private static readonly System.Collections.Generic.Dictionary<string, bool> ReplyCryPlayed = new System.Collections.Generic.Dictionary<string, bool>();

    // Per-tag flag for the one-time reply cry that answers the start-of-scene move clip. Separate
    // from ReplyCryPlayed so the two cries are independent - one firing never consumes the other.
    private static readonly System.Collections.Generic.Dictionary<string, bool> StartReplyCryPlayed = new System.Collections.Generic.Dictionary<string, bool>();

    private static readonly int HashIdle = Animator.StringToHash("Idle");
    private static readonly int HashAiming = Animator.StringToHash("Aiming");
    private static readonly int HashFiring = Animator.StringToHash("Firing");
    private static readonly int HashKneel = Animator.StringToHash("Kneel");
    private static readonly int HashWalking = Animator.StringToHash("Walking");
    private static readonly int HashCrouching = Animator.StringToHash("Crouching");

    private void Awake()
    {
        currentHealth = maxHealth;
        animator = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();
        bodyCapsule = GetComponent<CapsuleCollider>();
        characterController = GetComponent<CharacterController>();

        if (firePoint == null) firePoint = transform;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        animator.applyRootMotion = true;

        if (agent != null)
        {
            // Position always comes from the animation's root motion (synced below in
            // OnAnimatorMove) - the agent is only used for pathfinding/steering data, never
            // to move the transform itself. Rotation stays independent of root motion; it's
            // driven manually via FaceTarget/SnapFaceTarget/SteerTowardAgentPath.
            agent.updatePosition = false;
            agent.updateRotation = false;

            // Every agent defaults to the same avoidancePriority (50), so when two units meet
            // at a chokepoint - like squeezing around the same obstacle corner - neither one
            // outranks the other and local avoidance can deadlock with both just pushing against
            // each other. Randomizing priority per-unit breaks the tie so one of them yields.
            agent.avoidancePriority = Random.Range(10, 90);
        }

        ragdollRigidbodies = System.Array.FindAll(
            GetComponentsInChildren<Rigidbody>(true),
            rb => rb.gameObject != gameObject);
        ragdollColliders = System.Array.FindAll(
            GetComponentsInChildren<Collider>(true),
            col => col.gameObject != gameObject);
        SetRagdollPhysicsEnabled(false);
    }

    private void OnEnable()
    {
        // Statics survive editor Play sessions, so clear this tag's spotted flag when the first
        // unit of that tag registers (no other same-tag unit active yet = fresh scene). Without
        // this, the bark would only ever play on the first playthrough after a domain reload.
        bool anySameTagActive = false;
        foreach (var other in ActiveEnemies)
        {
            if (other != null && other != this && HasTag(other, gameObject.tag)) { anySameTagActive = true; break; }
        }
        if (!anySameTagActive)
        {
            SpottedBarkPlayed[gameObject.tag] = false;
            ReplyCryPlayed[gameObject.tag] = false;
            StartReplyCryPlayed[gameObject.tag] = false;
        }

        ActiveEnemies.Add(this);
        behaviorRoutine = StartCoroutine(BehaviorLoop());
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
        if (behaviorRoutine != null) StopCoroutine(behaviorRoutine);
    }

    private void LateUpdate()
    {
        if (!isDead)
        {
            // Positional separation keeps units from stacking, but applying it in stationary
            // states (Aim/Fire/Kneel/Idle) is what makes crouched/kneeling units visibly GLIDE
            // across the floor - there's no walk animation playing, yet the transform is being
            // nudged every frame. Only push while actually moving (Reposition/Investigate/Search/
            // TakeCover/PlayerOrder); when stationary, hold position. Units end up spaced anyway
            // because they pick spaced-out destinations when they DO move.
            bool isMovingState = state == State.Reposition || state == State.Investigate
                || state == State.Search || state == State.TakeCover || state == State.PlayerOrder;

            if (isMovingState)
            {
                Vector3 separation = ComputeAllySeparation();
                if (separation.sqrMagnitude > 0.0001f)
                {
                    transform.position += separation * allyAvoidanceStrength * Time.deltaTime;
                    if (agent != null) agent.nextPosition = transform.position;
                }
            }
        }

        if (!isDead) ApplyAimCorrection(); // boss: barrel onto the aim point, after animation + head IK

        if (weaponRoot != null && !isDead)
        {
            targetRecoilRotation = Vector3.Lerp(targetRecoilRotation, Vector3.zero, recoilReturnSpeed * Time.deltaTime);
            currentRecoilRotation = Vector3.Slerp(currentRecoilRotation, targetRecoilRotation, recoilSnap * Time.deltaTime);
            weaponRoot.localRotation = Quaternion.Euler(currentRecoilRotation);
        }
    }

    private void OnAnimatorMove()
    {
        // applyRootMotion=true applies BOTH root position and root rotation to the transform.
        // We only want position from root motion - rotation is driven manually (FaceTarget/
        // SnapFaceTarget/SteerTowardAgentPath). Any rotation baked into the aim/idle clips
        // otherwise fights our manual Slerp every frame, producing "spinning/jittering while
        // aiming". Snapshot the rotation our own code set, apply root-motion position exactly
        // as before, then restore that rotation. Position handling is untouched from the
        // original - the earlier NavMesh-snap attempt caused units to fall through the floor
        // (the sampled NavMesh height didn't match the character pivot height) and was reverted.
        Quaternion manualRotation = transform.rotation;

        if (agent != null)
        {
            agent.nextPosition = animator.rootPosition;
            transform.position = animator.rootPosition;
        }
        else
        {
            transform.position = animator.rootPosition;
        }

        transform.rotation = manualRotation; // discard any root-motion rotation

        // learn his real running speed (for the rush range check)
        if (animator.GetBool(HashWalking) && Time.deltaTime > 0f)
        {
            Vector3 v = animator.velocity; v.y = 0f;
            if (v.magnitude > 1f) runSpeed = Mathf.Lerp(runSpeed, v.magnitude, Time.deltaTime);
        }
    }

    // Halts any in-progress NavMeshAgent path when entering a stationary state
    // (Idle/Aim/Fire/Kneel), so a path left over from an interrupted Reposition/Investigate/
    // Search/TakeCover doesn't keep the agent's steering/arrival calculations pointed at a
    // stale destination.
    private void StopAgentMovement()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void ResumeAgentMovement()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || isDead) return;

        bool isAiming = (state == State.Aim || state == State.Fire);
        float targetWeight = isAiming ? 1f : 0f;

        // during a trait (rush, roll, cry...) let go of the aim quickly so the body follows the new motion
        currentIKWeight = Mathf.Lerp(currentIKWeight, targetWeight, Time.deltaTime * ikTransitionSpeed * (busyWithTrait ? 4f : 1f));

        if (isAiming && aimLocked)
        {
            // Boss: past the tracking part of the aim, head/body stay on the locked point.
            currentLookPos = Vector3.Lerp(currentLookPos, lockedAimPoint, Time.deltaTime * ikTransitionSpeed);
        }
        else if (currentTarget != null)
        {
            Vector3 targetAimPoint = currentTarget.position + (Vector3.up * targetAimHeight);
            currentLookPos = Vector3.Lerp(currentLookPos, targetAimPoint, Time.deltaTime * ikTransitionSpeed);
        }
        else if (lastKnownPosition != Vector3.zero)
        {
            Vector3 targetAimPoint = lastKnownPosition + (Vector3.up * targetAimHeight);
            currentLookPos = Vector3.Lerp(currentLookPos, targetAimPoint, Time.deltaTime * ikTransitionSpeed);
        }

        animator.SetLookAtWeight(currentIKWeight, bodyWeight, headWeight, 1f, clampWeight);
        animator.SetLookAtPosition(currentLookPos);
    }

    private IEnumerator BehaviorLoop()
    {
        while (true)
        {
            switch (state)
            {
                case State.Idle: yield return StateIdle(); break;
                case State.Aim: yield return StateAim(); break;
                case State.Fire: yield return StateFire(); break;
                case State.Kneel: yield return StateKneel(); break;
                case State.Reposition: yield return StateReposition(); break;
                case State.Investigate: yield return StateInvestigate(); break;
                case State.Search: yield return StateSearch(); break;
                case State.TakeCover: yield return StateTakeCover(); break;
                case State.PlayerOrder: yield return StatePlayerOrder(); break;
            }
        }
    }

    private IEnumerator StateIdle()
    {
        StopAgentMovement();

        animator.SetBool(HashIdle, true);
        animator.SetBool(HashAiming, false);
        animator.SetBool(HashWalking, false);
        animator.SetBool(HashKneel, false);

        while (true)
        {
            currentTarget = FindTarget();
            if (currentTarget != null)
            {
                NotifyEnemySpotted();
                BroadcastAlert(currentTarget);
                ClearAlert();
                state = State.Aim;
                yield break;
            }

            if (alertedTarget != null && Time.time - alertedTime <= alertExpireTime)
            {
                state = State.Investigate;
                yield break;
            }

            if (hasOrder)
            {
                state = State.PlayerOrder;
                yield break;
            }

            yield return new WaitForSeconds(detectionCheckInterval);
        }
    }

    private IEnumerator StateAim()
    {
        StopAgentMovement();

        // A brand-new player order pre-empts combat: the player is explicitly commanding a
        // move, so break off even a valid engagement. (orderIsFresh is cleared the moment the
        // unit enters PlayerOrder, so this only fires for a genuinely new order, not for one
        // the unit is already in the middle of executing.)
        if (hasOrder && orderIsFresh)
        {
            animator.SetBool(HashAiming, false);
            state = State.PlayerOrder;
            yield break;
        }

        if (!TargetStillValid())
        {
            if (currentTarget != null) lastKnownPosition = currentTarget.position;
            state = hasOrder ? State.PlayerOrder : State.Search;
            yield break;
        }

        animator.SetBool(HashIdle, false);
        animator.SetBool(HashAiming, true);

        TryPlayCombatBattleCry(); // sporadic individual cry while actively engaging (self-cooldowns)

        isCrouchedStance = DetermineCrouchStance();
        SetCrouching(isCrouchedStance);

        if (!HasLineOfSightAtCurrentStance())
        {
            if (currentTarget != null) lastKnownPosition = currentTarget.position;
            animator.SetBool(HashAiming, false);
            state = hasOrder ? State.PlayerOrder : State.Search;
            yield break;
        }

        // Boss: track the target only for the first part of the aim, then lock onto where they were.
        float aimDuration = Random.Range(aimDurationMin, aimDurationMax);
        float trackUntil = aimDuration * aimTrackFraction;
        aimLocked = false;
        float t = 0f;
        while (t < aimDuration)
        {
            if (hasOrder && orderIsFresh)
            {
                aimLocked = false;
                animator.SetBool(HashAiming, false);
                state = State.PlayerOrder;
                yield break;
            }
            if (!aimLocked)
            {
                if (t < trackUntil || currentTarget == null) FaceTarget();
                else LockAim();
            }
            t += Time.deltaTime;
            yield return null;
        }
        if (!aimLocked) LockAim(); // aimTrackFraction = 1: tracked the whole way, lock at the moment of firing

        state = State.Fire;
    }

    // The aim clip holds the rifle tilted up (~8 degrees) and the look-at IK only turns the head, so the
    // barrel would sit well above the target. After the animation, turn the upper chest so the muzzle
    // points exactly at the IK aim point - which follows the target while tracking and freezes once the
    // aim locks, so the rifle visibly lines up with the locked shot's path. Fades in/out with aiming.
    private void ApplyAimCorrection()
    {
        if (aimBone == null || firePoint == null || aimCorrection <= 0f || currentIKWeight <= 0.01f) return;
        for (int i = 0; i < 2; i++) // turning the chest moves the muzzle too; a second pass settles it
        {
            Vector3 desired = currentLookPos - firePoint.position;
            if (desired.sqrMagnitude < 0.01f) return;
            Quaternion.FromToRotation(firePoint.forward, desired.normalized).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -maxAimCorrectionAngle, maxAimCorrectionAngle) * aimCorrection * currentIKWeight;
            aimBone.rotation = Quaternion.AngleAxis(angle, axis) * aimBone.rotation;
        }
    }

    private void LockAim()
    {
        aimLocked = true;
        Vector3 at = currentTarget != null ? currentTarget.position : lastKnownPosition;
        lockedAimPoint = at + Vector3.up * targetAimHeight;
    }

    private IEnumerator StateFire()
    {
        StopAgentMovement();

        if (hasOrder && orderIsFresh)
        {
            aimLocked = false;
            animator.SetBool(HashAiming, false);
            state = State.PlayerOrder;
            yield break;
        }

        // Boss: a locked shot is committed - it goes down the locked line even if the target has since
        // ducked behind cover or stepped out of range (the bullet then hits whatever is in the way).
        if (!aimLocked && (!TargetStillValid() || !HasLineOfSightAtCurrentStance()))
        {
            if (currentTarget != null) lastKnownPosition = currentTarget.position;
            animator.SetBool(HashAiming, false);
            state = hasOrder ? State.PlayerOrder : State.Search;
            yield break;
        }

        if (!aimLocked) SnapFaceTarget(); // only a normal (unlocked) shot snaps onto the target
        animator.SetTrigger(HashFiring);
        Shoot();

        yield return new WaitForSeconds(0.1f);
        aimLocked = false;
        if (currentTarget != null && !TargetStillValid()) lastKnownPosition = currentTarget.position;

        // Aiming was turned on back in StateAim and needs to come back off here, before we
        // hand off to TakeCover/Kneel. Otherwise Aiming stays true while Walking (TakeCover)
        // or Kneel also goes true, and those flags fighting in the Animator is what left units
        // stuck on the aim/fire pose - never actually entering a walk or kneel animation - while
        // the rotation code kept steering the transform every frame regardless, producing the
        // "spinning in place with no animation" behavior.
        animator.SetBool(HashAiming, false);

        // boss: every shotsPerMelee-th shot is followed by a rush and a melee strike instead of a reload -
        // but only if the player is within one reload's run. Otherwise the rush stays due and goes off
        // after the next shot he fires from close enough.
        if (shotsPerMelee > 0 && ++shotsSinceMelee >= shotsPerMelee && CloseEnoughToRush(currentTarget))
        {
            shotsSinceMelee = 0;
            StartTrait(MeleeRush());
            yield break;
        }

        if (seekCoverBeforeReload && TryFindCoverPoint(out Vector3 coverPoint))
        {
            pendingCoverDestination = coverPoint;
            state = State.TakeCover;
        }
        else
        {
            state = State.Kneel;
        }
    }

    private void Shoot()
    {
        targetRecoilRotation += new Vector3(
            maxRecoilAngles.x,
            Random.Range(-maxRecoilAngles.y, maxRecoilAngles.y),
            Random.Range(-maxRecoilAngles.z, maxRecoilAngles.z)
        );

        if (muzzleFlashPrefab != null)
        {
            GameObject fx = Instantiate(muzzleFlashPrefab, firePoint.position, firePoint.rotation);
            Destroy(fx, muzzleFlashLifetime);
        }

        if (audioSource != null && gunShotSound != null)
        {
            audioSource.PlayOneShot(gunShotSound, gunShotVolume);
        }

        if (aimLocked)
        {
            FireLockedShot();
            return;
        }

        if (currentTarget != null)
        {
            var playerTarget = currentTarget.GetComponentInParent<FirstPersonController>();
            if (playerTarget != null)
            {
                playerTarget.TakeHit();
            }
            else if (Random.value <= hitChance)
            {
                var enemyTarget = currentTarget.GetComponentInParent<EnemyAI>();
                if (enemyTarget != null) enemyTarget.Die();
            }
        }
    }

    // Boss shot: a ray from the muzzle toward the point the aim locked on. Whatever it meets first
    // decides it - the player takes a hit, a hostile soldier rolls hitChance, anything else (ground,
    // sandbags, walls, a friendly soldier) stops the bullet. The boss's own colliders are skipped.
    private void FireLockedShot()
    {
        Vector3 origin = firePoint != null ? firePoint.position : transform.position + Vector3.up * eyeHeightStanding;
        Vector3 dir = lockedAimPoint - origin;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        var hits = Physics.RaycastAll(origin, dir, lockedShotRange, lockedShotMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue; // own body, ragdoll pieces, rifle

            if (debugLockedShots) Debug.DrawLine(origin, hit.point, Color.red, 2f);

            var player = hit.collider.GetComponentInParent<FirstPersonController>();
            if (player != null)
            {
                player.TakeHit();
                if (debugLockedShots) Debug.Log($"[Boss] Locked shot hit the player ({hit.distance:F1} m).", this);
                return;
            }

            var soldier = hit.collider.GetComponentInParent<EnemyAI>();
            if (soldier != null)
            {
                bool hostile = IsHostile(soldier); // the soldier root carries the faction tag
                if (hostile && Random.value <= hitChance) soldier.Die();
                if (debugLockedShots) Debug.Log($"[Boss] Locked shot reached soldier '{soldier.name}' ({(hostile ? "hostile" : "friendly - absorbed")}).", this);
                return;
            }

            if (debugLockedShots) Debug.Log($"[Boss] Locked shot blocked by '{hit.collider.name}' ({hit.distance:F1} m) - target dodged or took cover.", this);
            return;
        }

        if (debugLockedShots)
        {
            Debug.DrawRay(origin, dir * lockedShotRange, Color.yellow, 2f);
            Debug.Log("[Boss] Locked shot missed - nothing in the line.", this);
        }
    }

    private IEnumerator StateKneel()
    {
        StopAgentMovement();

        if (hasOrder && orderIsFresh)
        {
            animator.SetBool(HashAiming, false);
            animator.SetBool(HashKneel, false);
            state = State.PlayerOrder;
            yield break;
        }

        animator.SetBool(HashAiming, false);
        SetCrouching(isCrouchedStance);
        animator.SetBool(HashKneel, true);

        yield return WaitUntilInState(kneelStateName);

        if (audioSource != null && reloadSound != null)
        {
            audioSource.PlayOneShot(reloadSound, reloadVolume);
        }

        yield return new WaitForSeconds(kneelReloadDuration);

        animator.SetBool(HashKneel, false);

        // If an order is waiting, don't burn time confirming we've settled into idle first -
        // PlayerOrder forcibly clears the kneel flag and drives Walking itself, so hand off
        // immediately rather than risk WaitUntilInState eating its full timeout here.
        if (hasOrder && !TargetStillValid())
        {
            state = State.PlayerOrder;
            yield break;
        }

        yield return WaitUntilInState(isCrouchedStance ? crouchIdleStateName : idleStateName);

        if (TargetStillValid() && Random.value < repositionChance)
        {
            state = State.Reposition;
        }
        else if (TargetStillValid())
        {
            state = State.Aim;
        }
        else if (hasOrder)
        {
            state = State.PlayerOrder;
        }
        else
        {
            if (currentTarget != null) lastKnownPosition = currentTarget.position;
            state = State.Search;
        }
    }

    private IEnumerator StateReposition()
    {
        Vector3 destination = PickTacticalDestination();

        SetCrouching(true);
        animator.SetBool(HashWalking, true);

        if (agent != null && agent.isOnNavMesh)
        {
            ResumeAgentMovement();
            agent.speed = repositionSpeed;

            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, repositionRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(navHit.position);
            }

            float timeout = stateWaitTimeout;
            while ((agent.pathPending || agent.remainingDistance > agent.stoppingDistance) && timeout > 0f)
            {
                SteerTowardAgentPath(); // rotation is always handled manually, independent of root motion
                if (hasOrder) { animator.SetBool(HashWalking, false); state = State.PlayerOrder; yield break; }
                timeout -= Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            float timeout = stateWaitTimeout;
            while (Vector3.Distance(transform.position, destination) > 0.5f && timeout > 0f)
            {
                Vector3 dir = destination - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                }
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        animator.SetBool(HashWalking, false);
        yield return WaitUntilInState(crouchIdleStateName);

        if (TargetStillValid())
        {
            state = State.Aim;
        }
        else if (hasOrder)
        {
            state = State.PlayerOrder;
        }
        else
        {
            if (currentTarget != null) lastKnownPosition = currentTarget.position;
            state = State.Search;
        }
    }

    private IEnumerator StateTakeCover()
    {
        Vector3 destination = pendingCoverDestination;

        // Keep the isCrouchedStance field in sync with the forced crouch here - StateKneel
        // reads this field for both SetCrouching(...) and its post-reload WaitUntilInState(...)
        // target, so if it's left stale (e.g. false from before we took cover) the unit gets
        // un-crouched right as reload starts and then waits on the wrong idle state name.
        isCrouchedStance = true;
        SetCrouching(true);
        animator.SetBool(HashWalking, true);

        if (agent != null && agent.isOnNavMesh)
        {
            ResumeAgentMovement();
            agent.speed = repositionSpeed;

            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, coverSearchRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(navHit.position);
            }

            float timeout = stateWaitTimeout;
            while ((agent.pathPending || agent.remainingDistance > coverArrivalDistance) && timeout > 0f)
            {
                SteerTowardAgentPath(); // rotation is always handled manually, independent of root motion
                timeout -= Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            float timeout = stateWaitTimeout;
            while (Vector3.Distance(transform.position, destination) > coverArrivalDistance && timeout > 0f)
            {
                Vector3 dir = destination - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                }
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        // Whether we made it all the way to the cover point or timed out partway there,
        // reload from wherever we ended up - better than standing frozen mid-route.
        animator.SetBool(HashWalking, false);
        state = State.Kneel;
    }

    // Looks for a nearby point where an obstacle sits between the target and us, so reloading
    // there doesn't leave us standing in the open. Unlike PickTacticalDestination (which wants
    // clear sightlines to shoot), this deliberately wants the sightline BLOCKED.
    private bool TryFindCoverPoint(out Vector3 coverPoint)
    {
        coverPoint = transform.position;

        if (currentTarget == null || lineOfSightBlockers.value == 0) return false;

        Vector3 targetEye = currentTarget.position + Vector3.up * targetAimHeight;

        // A uniform circle around us wastes most of its samples on open ground that can never
        // block line of sight - only the sliver on the far side of an obstacle (relative to the
        // target) can. Bias samples toward that side so more of them land somewhere plausible.
        Vector3 awayFromTarget = transform.position - currentTarget.position;
        awayFromTarget.y = 0f;
        awayFromTarget = awayFromTarget.sqrMagnitude > 0.0001f ? awayFromTarget.normalized : transform.forward;

        float bestScore = -float.MaxValue;
        bool found = false;

        for (int i = 0; i < coverCandidateAttempts; i++)
        {
            Vector2 rand2D = Random.insideUnitCircle * coverSearchRadius;
            Vector3 randomOffset = new Vector3(rand2D.x, 0f, rand2D.y);
            Vector3 biasedOffset = Vector3.Lerp(randomOffset, awayFromTarget * coverSearchRadius, 0.5f);
            Vector3 candidate = transform.position + biasedOffset;
            candidate.y = transform.position.y;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas)) continue;
            candidate = navHit.position;

            if (!HasWallClearance(candidate)) continue;

            // Check the sightline at standing height (we're walking upright to get there,
            // not crouched) - if the target can still see us here, it isn't real cover.
            Vector3 standEye = candidate + Vector3.up * eyeHeightStanding;
            if (!Physics.Linecast(targetEye, standEye, lineOfSightBlockers)) continue;

            // The nearest point that merely breaks the sightline is, by definition, right at
            // the near edge/corner of whatever's blocking it - that's what kept sending units
            // to obstacle edges instead of genuinely into cover. Reward candidates that still
            // block the sightline a bit further in, so real depth beats a bare corner-graze.
            Vector3 deeperPoint = candidate + biasedOffset.normalized * 0.75f;
            deeperPoint.y = candidate.y;
            Vector3 deeperEye = deeperPoint + Vector3.up * eyeHeightStanding;
            bool hasDepth = Physics.Linecast(targetEye, deeperEye, lineOfSightBlockers);

            // Nearest cover is usually the SAME nearest corner/edge for every unit standing
            // near each other, since they're all blocking sightline to the same target point.
            // Without spacing this against allies (like PickTacticalDestination already does
            // for repositioning), everyone independently picks that one closest spot and piles
            // up on it. Prefer close cover, but heavily penalize a spot an ally already claimed.
            float dist = Vector3.Distance(transform.position, candidate);
            float score = -dist + (hasDepth ? 20f : 0f);

            float closestAllyDist = float.MaxValue;
            foreach (var other in ActiveEnemies)
            {
                if (other == null || other == this) continue;
                if (!HasTag(other, gameObject.tag)) continue;

                float d = Vector3.Distance(candidate, other.PlanningPosition);
                if (d < closestAllyDist) closestAllyDist = d;
            }

            if (closestAllyDist < minSeparationDistance) score -= 100f;

            if (score > bestScore)
            {
                bestScore = score;
                coverPoint = candidate;
                found = true;
            }
        }

        return found;
    }

    // Executes a standing player move order: walk toward orderPoint, spread out in a
    // formation with squadmates heading to the same point, and hold at optimalCombatRange
    // instead of walking in if the point is contested by units carrying one of our target tags.
    // Combat still takes priority - TryAcquireDirectSight() below hands off to Aim exactly
    // like Investigate/Search do, and every "no target" fallback elsewhere in this file
    // already routes back into PlayerOrder (instead of Search/Idle) as long as hasOrder
    // is still true, so the unit naturally resumes the order once a fight is over.
    private IEnumerator StatePlayerOrder()
    {
        orderIsFresh = false; // we're now acting on it; further combat won't be pre-empted by this same order
        // Clear any animator flags left set by whatever state we came from (Kneel, Aim, Fire).
        // Just setting Walking=true isn't enough: if Kneel or Aiming is still true, those bools
        // fight Walking in the Animator and the unit stays stuck in its kneel/aim pose instead
        // of transitioning to the walk - the same "flags fighting" issue StateFire guards against
        // before it hands off. This is what left mid-reload units frozen when an order arrived.
        animator.SetBool(HashKneel, false);
        animator.SetBool(HashAiming, false);
        animator.SetBool(HashIdle, false);
        animator.SetBool(HashWalking, true);
        SetCrouching(orderCrouchWalk); // stance was rolled once per order in ReceiveMoveOrder

        if (agent != null && agent.isOnNavMesh)
        {
            ResumeAgentMovement();
            agent.speed = repositionSpeed;

            float recheckTimer = 0f;

            while (hasOrder)
            {
                if (recheckTimer <= 0f)
                {
                    Vector3 destination = ComputeOrderDestination();
                    if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, minSeparationDistance * 2f, NavMesh.AllAreas))
                    {
                        agent.SetDestination(navHit.position);
                    }
                    recheckTimer = orderRecheckInterval;
                }

                SteerTowardAgentPath(); // rotation is always handled manually, independent of root motion

                if (TryAcquireDirectSight()) yield break;

                if (!agent.pathPending && agent.remainingDistance <= orderArrivalDistance)
                {
                    hasOrder = false;
                    break;
                }

                recheckTimer -= Time.deltaTime;
                yield return null;
            }
        }

        animator.SetBool(HashWalking, false);
        SetCrouching(false); // stand back up on arrival regardless of the travel stance
        yield return WaitUntilInState(idleStateName);
        state = State.Idle;
    }

    // Where this unit's slot lands in the formation. Vogel-spiral offset scaled by
    // minSeparationDistance, indexed by this unit's rank (by current distance to the
    // order point) among all same-tag units currently executing the SAME order - reusing
    // the existing minSeparationDistance/ActiveEnemies infrastructure instead of a
    // separate squad list, which also sidesteps any shared-collection mutation issues.
    private Vector3 ComputeOrderDestination()
    {
        int slotIndex = OrderSlotIndex();
        Vector3 formationOffset = SpiralOffset(slotIndex, minSeparationDistance);

        Transform nearestOpponent = FindNearestOpponentTo(orderPoint, orderContestedCheckRadius);

        Vector3 candidate;
        if (nearestOpponent == null)
        {
            candidate = orderPoint + formationOffset;
        }
        else
        {
            // Contested: hold at optimalCombatRange from the nearest opponent, along the
            // line from that opponent toward the ordered point, rather than walking in.
            Vector3 approachDir = orderPoint - nearestOpponent.position;
            approachDir.y = 0f;
            approachDir = approachDir.sqrMagnitude > 0.0001f ? approachDir.normalized : transform.forward;

            Vector3 standoffPoint = nearestOpponent.position + approachDir * optimalCombatRange;
            candidate = standoffPoint + formationOffset;
        }

        candidate = NudgeAwayFromClaimedSpots(candidate);
        candidate.y = transform.position.y;
        return candidate;
    }

    private int OrderSlotIndex()
    {
        List<GoyoBossAI> group = new List<GoyoBossAI>();
        foreach (var other in ActiveEnemies)
        {
            if (other == null) continue;
            if (!HasTag(other, gameObject.tag)) continue;
            if (!other.hasOrder) continue;
            if (Vector3.Distance(other.orderPoint, orderPoint) > 0.5f) continue; // different order
            group.Add(other);
        }

        group.Sort((a, b) =>
            Vector3.SqrMagnitude(a.transform.position - orderPoint)
            .CompareTo(Vector3.SqrMagnitude(b.transform.position - orderPoint)));

        return group.IndexOf(this);
    }

    private Vector3 SpiralOffset(int index, float spacing)
    {
        float radius = spacing * Mathf.Sqrt(index + 0.5f);
        float angle = index * GoldenAngle;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
    }

    // Proximity-only opponent check (no FOV/line-of-sight requirement) - this is asking
    // "is this area held by the other side", not "can I currently see someone".
    private Transform FindNearestOpponentTo(Vector3 point, float radius)
    {
        Collider[] hits = Physics.OverlapSphere(point, radius);
        Transform best = null;
        float bestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            if (!IsHostile(hit)) continue;
            float d = Vector3.SqrMagnitude(hit.transform.position - point);
            if (d < bestDist)
            {
                bestDist = d;
                best = hit.transform;
            }
        }

        return best;
    }

    // The spiral already keeps squadmates on the SAME order spread out from each other;
    // this catches the remaining edge case of a different, already-moving ally (e.g. from
    // an overlapping earlier order) planning to pass through the same spot.
    private Vector3 NudgeAwayFromClaimedSpots(Vector3 candidate)
    {
        const int maxNudges = 4;

        for (int attempt = 0; attempt < maxNudges; attempt++)
        {
            float closestDist = float.MaxValue;
            Vector3 closestOther = Vector3.zero;

            foreach (var other in ActiveEnemies)
            {
                if (other == null || other == this) continue;
                if (!HasTag(other, gameObject.tag)) continue;

                float d = Vector3.Distance(candidate, other.PlanningPosition);
                if (d < closestDist)
                {
                    closestDist = d;
                    closestOther = other.PlanningPosition;
                }
            }

            if (closestDist >= minSeparationDistance || closestDist == float.MaxValue) break;

            Vector3 pushDir = candidate - closestOther;
            pushDir.y = 0f;
            pushDir = pushDir.sqrMagnitude > 0.0001f ? pushDir.normalized : Random.insideUnitSphere;
            candidate += pushDir * (minSeparationDistance - closestDist + 0.25f);
        }

        return candidate;
    }

    private IEnumerator StateInvestigate()
    {
        Vector3 destination = alertedPosition;

        animator.SetBool(HashWalking, true);
        SetCrouching(false);

        if (agent != null && agent.isOnNavMesh)
        {
            ResumeAgentMovement();
            agent.speed = repositionSpeed;

            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, repositionRadius * 3f, NavMesh.AllAreas))
            {
                agent.SetDestination(navHit.position);
            }

            float timeout = stateWaitTimeout * 3f;
            while ((agent.pathPending || agent.remainingDistance > investigateArrivalDistance) && timeout > 0f)
            {
                SteerTowardAgentPath(); // rotation is always handled manually, independent of root motion
                if (TryAcquireDirectSight()) yield break;
                if (hasOrder) { animator.SetBool(HashWalking, false); state = State.PlayerOrder; yield break; }
                timeout -= Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            float timeout = stateWaitTimeout * 3f;
            while (Vector3.Distance(transform.position, destination) > investigateArrivalDistance && timeout > 0f)
            {
                Vector3 dir = destination - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                }

                if (TryAcquireDirectSight()) yield break;
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        animator.SetBool(HashWalking, false);
        yield return WaitUntilInState(idleStateName);

        if (TryAcquireDirectSight()) yield break;

        ClearAlert();
        lastKnownPosition = alertedPosition;
        state = hasOrder ? State.PlayerOrder : State.Search;
    }

    private IEnumerator StateSearch()
    {
        float endTime = Time.time + searchDuration;

        while (Time.time < endTime)
        {
            Vector3 destination = PickSearchDestination();

            animator.SetBool(HashWalking, true);
            SetCrouching(false);

            if (agent != null && agent.isOnNavMesh)
            {
                ResumeAgentMovement();
                agent.speed = repositionSpeed;
                if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, searchRadius, NavMesh.AllAreas))
                {
                    agent.SetDestination(navHit.position);
                }

                float timeout = stateWaitTimeout * 2f;
                while ((agent.pathPending || agent.remainingDistance > agent.stoppingDistance) && timeout > 0f)
                {
                    SteerTowardAgentPath(); // rotation is always handled manually, independent of root motion

                    if (TryAcquireDirectSight()) yield break;

                    if (alertedTarget != null && Time.time - alertedTime <= alertExpireTime)
                    {
                        state = State.Investigate;
                        yield break;
                    }

                    if (hasOrder) { animator.SetBool(HashWalking, false); state = State.PlayerOrder; yield break; }

                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                float timeout = stateWaitTimeout * 2f;
                while (Vector3.Distance(transform.position, destination) > 0.5f && timeout > 0f)
                {
                    Vector3 dir = destination - transform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(dir);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                    }

                    if (TryAcquireDirectSight()) yield break;

                    if (alertedTarget != null && Time.time - alertedTime <= alertExpireTime)
                    {
                        state = State.Investigate;
                        yield break;
                    }

                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            animator.SetBool(HashWalking, false);
            yield return WaitUntilInState(idleStateName);

            float pauseTimer = searchWaitTime;
            while (pauseTimer > 0f)
            {
                if (TryAcquireDirectSight()) yield break;

                if (alertedTarget != null && Time.time - alertedTime <= alertExpireTime)
                {
                    state = State.Investigate;
                    yield break;
                }

                if (hasOrder) { state = State.PlayerOrder; yield break; }

                pauseTimer -= Time.deltaTime;
                yield return null;
            }
        }

        state = hasOrder ? State.PlayerOrder : State.Idle;
    }

    private Vector3 PickSearchDestination()
    {
        Vector3 searchCenter = (lastKnownPosition != Vector3.zero) ? lastKnownPosition : transform.position;

        for (int i = 0; i < 5; i++)
        {
            Vector3 randomDir = Random.onUnitSphere;
            randomDir.y = 0f;
            Vector3 candidate = searchCenter + (randomDir.normalized * Random.Range(minRepositionDistance, searchRadius));
            candidate.y = transform.position.y;

            if (HasWallClearance(candidate)) return candidate;
        }
        return transform.position;
    }

    private bool TryAcquireDirectSight()
    {
        Transform found = FindTarget();
        if (found == null) return false;

        currentTarget = found;
        animator.SetBool(HashWalking, false);
        ClearAlert();
        NotifyEnemySpotted();
        BroadcastAlert(currentTarget);
        state = State.Aim;
        return true;
    }

    // Plays the "enemy spotted" bark the first time anyone on this unit's side sees a target.
    // Guarded by a per-tag static flag so it fires once for the whole squad, not per unit.
    private void NotifyEnemySpotted()
    {
        if (!HasTag(this, spottedAudioTag)) return; // only the intended side barks
        if (enemySpottedSounds == null || enemySpottedSounds.Length == 0) return;

        if (SpottedBarkPlayed.TryGetValue(spottedAudioTag, out bool already) && already) return;
        SpottedBarkPlayed[spottedAudioTag] = true;

        AudioClip clip = enemySpottedSounds[Random.Range(0, enemySpottedSounds.Length)];
        if (clip == null) return;

        // Play at the spotting unit's position. Uses the shared AudioSource if present, else a
        // one-shot at the unit's location so it still works on units without an AudioSource.
        if (audioSource != null) audioSource.PlayOneShot(clip, enemySpottedVolume);
        else AudioSource.PlayClipAtPoint(clip, transform.position, enemySpottedVolume);

        // Kick off the one-time squad reply cry, timed to land right after this spotted clip ends.
        TriggerSpottedReplyCry(clip.length);
    }

    // Reply cry that answers the enemy-spotted bark. Triggered once per side (so it doesn't
    // re-fire per unit), but that single trigger makes EVERY ally cry - each with its own random
    // clip, a small random delay, and a slight pitch shift - so it sounds like a whole squad
    // shouting rather than one voice. Timed to start right after the spotted clip ends.
    private void TriggerSpottedReplyCry(float afterClipLength)
    {
        if (battleCryReplySounds == null || battleCryReplySounds.Length == 0) return;
        if (ReplyCryPlayed.TryGetValue(spottedAudioTag, out bool already) && already) return;
        ReplyCryPlayed[spottedAudioTag] = true;

        BroadcastReplyCry(afterClipLength + battleCryReplyDelay);
    }

    // Public: called by AllyPathFollower once the start-of-scene move clip finishes, so the squad
    // answers it too. Independent one-time flag, so it never blocks (or is blocked by) the spotted
    // reply.
    public void PlayStartReplyCry(float extraDelay = 0f)
    {
        if (battleCryReplySounds == null || battleCryReplySounds.Length == 0) return;
        if (!HasTag(this, spottedAudioTag)) return;
        if (StartReplyCryPlayed.TryGetValue(spottedAudioTag, out bool already) && already) return;
        StartReplyCryPlayed[spottedAudioTag] = true;

        BroadcastReplyCry(extraDelay);
    }

    // Tells every living ally on this side to cry out. Each unit staggers its own start slightly
    // and randomizes clip + pitch, turning one trigger into a chorus of distinct voices.
    private void BroadcastReplyCry(float baseDelay)
    {
        foreach (var other in ActiveEnemies)
        {
            if (other == null) continue;
            if (!HasTag(other, spottedAudioTag)) continue;
            other.StartCoroutine(other.ReplyCryRoutine(baseDelay));
        }
    }

    private System.Collections.IEnumerator ReplyCryRoutine(float delay)
    {
        // Per-unit random stagger on top of the shared base delay, so voices don't all hit on the
        // exact same frame (perfect unison sounds like one flanged clip, not a crowd).
        float stagger = Random.Range(0f, replyCryMaxStagger);
        float total = delay + stagger;
        if (total > 0f) yield return new WaitForSeconds(total);

        AudioClip clip = battleCryReplySounds[Random.Range(0, battleCryReplySounds.Length)];
        if (clip == null) yield break;

        PlayVoiceWithPitchVariation(clip, battleCryVolume);
    }

    // Plays a clip with a randomized pitch so repeated/overlapping uses of the same clips sound
    // like different people. Restores the source's pitch afterward. PlayOneShot can't carry a
    // per-shot pitch, so we set it on the source around the call; overlapping cries on ONE source
    // will share whatever pitch was last set, which is why a chorus really wants each unit to have
    // its own AudioSource (each unit plays its own cry on its own source here).
    private void PlayVoiceWithPitchVariation(AudioClip clip, float volume)
    {
        if (audioSource != null)
        {
            audioSource.pitch = Random.Range(1f - replyCryPitchVariation, 1f + replyCryPitchVariation);
            audioSource.PlayOneShot(clip, volume);
            audioSource.pitch = 1f; // restore for non-voice sounds (gunshots/reload) on this source
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }

    // Occasional individual combat cry. Called from active-combat states; self-limits via a
    // per-unit cooldown plus a random chance so cries are sporadic, not metronomic, and don't
    // all fire together. Allies only (gated on spottedAudioTag, same side as the other cries).
    private void TryPlayCombatBattleCry()
    {
        if (battleCryCombatSounds == null || battleCryCombatSounds.Length == 0) return;
        if (!HasTag(this, spottedAudioTag)) return;
        if (Time.time < nextBattleCryTime) return;

        // Schedule the next eligibility window regardless of whether this one actually fires,
        // so a failed chance roll doesn't retry every single frame.
        nextBattleCryTime = Time.time + Random.Range(battleCryMinInterval, battleCryMaxInterval);

        if (Random.value > battleCryChance) return;

        AudioClip clip = battleCryCombatSounds[Random.Range(0, battleCryCombatSounds.Length)];
        if (clip == null) return;

        PlayVoiceWithPitchVariation(clip, battleCryVolume);
    }

    private void BroadcastAlert(Transform target)
    {
        if (target == null) return;

        foreach (var other in ActiveEnemies)
        {
            if (other == null || other == this) continue;
            if (!HasTag(other, gameObject.tag)) continue; // Ensure alerts only go to allies

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist <= alertShareRadius)
            {
                other.ReceiveAlert(target, target.position);
            }
        }
    }

    // Called by PlayerOrderIssuer (or anything else directing this squad). Doesn't force
    // an immediate state switch - like ReceiveAlert, it's picked up at the next natural
    // check point so it never abruptly cuts off an in-progress Aim/Fire/Kneel.
    public void ReceiveMoveOrder(Vector3 point)
    {
        hasOrder = true;
        orderIsFresh = true;
        orderCrouchWalk = Random.value < orderCrouchWalkChance; // pick stance once, for the whole trip
        orderPoint = point;
    }

    public void CancelMoveOrder()
    {
        hasOrder = false;
        if (state == State.PlayerOrder) StopAgentMovement();
    }

    public void ReceiveAlert(Transform target, Vector3 position)
    {
        if (state != State.Idle && state != State.Search) return;

        alertedTarget = target;
        alertedPosition = position;
        lastKnownPosition = position;
        alertedTime = Time.time;
    }

    private void ClearAlert()
    {
        alertedTarget = null;
        alertedTime = -Mathf.Infinity;
    }

    private void SetCrouching(bool crouched)
    {
        animator.SetBool(HashCrouching, crouched);

        float height = crouched ? crouchedColliderHeight : standingColliderHeight;
        float centerY = crouched ? crouchedColliderCenterY : standingColliderCenterY;

        if (bodyCapsule != null)
        {
            bodyCapsule.height = height;
            Vector3 c = bodyCapsule.center;
            c.y = centerY;
            bodyCapsule.center = c;
        }

        if (characterController != null)
        {
            characterController.height = height;
            Vector3 c = characterController.center;
            c.y = centerY;
            characterController.center = c;
        }
    }

    private void SetRagdollPhysicsEnabled(bool active)
    {
        foreach (var rb in ragdollRigidbodies)
        {
            if (rb == null) continue;
            rb.isKinematic = !active;
        }

        foreach (var col in ragdollColliders)
        {
            if (col == null) continue;
            col.enabled = active;
        }
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;

        if (behaviorRoutine != null) StopCoroutine(behaviorRoutine);
        ActiveEnemies.Remove(this);

        // Notify listeners (e.g. GameOutcomeManager) after removal, so any living-count check they
        // run sees the accurate post-death count. Captured tag in case the object is destroyed.
        OnAnyUnitDied?.Invoke(gameObject.tag);

        if (audioSource != null && deathSounds != null && deathSounds.Length > 0)
        {
            AudioClip clip = deathSounds[Random.Range(0, deathSounds.Length)];
            if (clip != null) audioSource.PlayOneShot(clip, deathVolume);
        }

        animator.enabled = false;
        if (agent != null) agent.enabled = false;
        if (bodyCapsule != null) bodyCapsule.enabled = false;
        if (characterController != null) characterController.enabled = false;

        SetRagdollPhysicsEnabled(true);
        MoveRagdollToCorpseLayer();

        // Let the ragdoll flop and settle on the ground, then switch off all its colliders so the
        // corpse rests as pure visual mesh with no collision at all. The corpse-layer move already
        // stops it colliding with living units during the flop (via the layer collision matrix);
        // this removes collision entirely once it's down. Runs on this object, which lives until
        // ragdollDisappearDelay, so keep the settle delay shorter than that.
        StartCoroutine(DisableCorpseCollidersAfterSettle());

        Destroy(gameObject, ragdollDisappearDelay);
    }

    private IEnumerator DisableCorpseCollidersAfterSettle()
    {
        if (corpseColliderSettleDelay > 0f) yield return new WaitForSeconds(corpseColliderSettleDelay);

        // Turn off every ragdoll collider (and the body capsule, if it somehow survived). Also
        // make the ragdoll rigidbodies kinematic so nothing keeps simulating them once they have
        // no colliders - otherwise a stray force could drift the now-collisionless bones.
        foreach (var col in ragdollColliders)
        {
            if (col != null) col.enabled = false;
        }
        if (bodyCapsule != null) bodyCapsule.enabled = false;

        foreach (var rb in ragdollRigidbodies)
        {
            if (rb == null) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    private void MoveRagdollToCorpseLayer()
    {
        int corpseLayer = LayerMask.NameToLayer(corpseLayerName);
        if (corpseLayer < 0)
        {
            Debug.LogWarning($"GoyoBossAI: layer '{corpseLayerName}' doesn't exist. Add it in " +
                "Edit > Project Settings > Tags and Layers, then set its collision matrix entry " +
                "against Player/NPC layers off so corpses can be walked over.", this);
            return;
        }

        gameObject.layer = corpseLayer;
        foreach (var col in ragdollColliders)
        {
            if (col == null) continue;
            col.gameObject.layer = corpseLayer;
        }

        if (corpseSetupDiagnostics) LogCorpseSetupDiagnostics(corpseLayer);
    }

    // One-time-per-death report of whether the Corpse layer + collision matrix are set up so a
    // corpse actually passes through living units. Reads Physics.GetIgnoreLayerCollision, which is
    // exactly what the Project Settings > Physics matrix checkboxes control, so it tells you the
    // real runtime state rather than what you think the matrix says.
    private void LogCorpseSetupDiagnostics(int corpseLayer)
    {
        // Find which layers the living units are actually on, by sampling active units of both tags.
        var livingLayers = new System.Collections.Generic.HashSet<int>();
        foreach (var other in ActiveEnemies)
        {
            if (other != null) livingLayers.Add(other.gameObject.layer);
        }
        var player = FindObjectOfType<FirstPersonController>();
        if (player != null) livingLayers.Add(player.gameObject.layer);

        if (livingLayers.Count == 0)
        {
            Debug.LogWarning("[CorpseSetup] Couldn't sample any living units to check the matrix against " +
                "(everyone dead, or no FirstPersonController found). Re-check with units alive.", this);
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CorpseSetup] Corpse layer = '{corpseLayerName}' (index {corpseLayer}).");
        bool allGood = true;

        foreach (int layer in livingLayers)
        {
            string layerName = LayerMask.LayerToName(layer);
            // true == the two layers are set to IGNORE each other == corpse passes through == GOOD.
            bool ignored = Physics.GetIgnoreLayerCollision(corpseLayer, layer);
            sb.AppendLine($"   vs living layer '{(string.IsNullOrEmpty(layerName) ? layer.ToString() : layerName)}' (index {layer}): " +
                          $"{(ignored ? "IGNORED (good - corpse passes through)" : "COLLIDING (BAD - uncheck this pair in Physics matrix)")}");
            if (!ignored) allGood = false;
        }

        // Also flag the classic mistake: living units sitting on the Default layer, which usually
        // also collides with everything and is easy to forget in the matrix.
        if (livingLayers.Contains(0))
        {
            sb.AppendLine("   NOTE: at least one living unit is on the 'Default' layer. Put the player and " +
                          "troops on dedicated Player/NPC layers so you can cleanly uncheck them against Corpse.");
        }

        if (allGood) Debug.Log(sb.ToString().TrimEnd(), this);
        else Debug.LogWarning(sb.ToString().TrimEnd() + "\n=> Fix: Edit > Project Settings > Physics, and UNCHECK each pair marked BAD above.", this);
    }

    private IEnumerator WaitUntilInState(string stateName, int layer = 0)
    {
        float t = 0f;
        while (!animator.GetCurrentAnimatorStateInfo(layer).IsName(stateName) && t < stateWaitTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }
    }

    // Where this unit is either standing, or already headed. Other units use this (instead
    // of raw transform.position) when scoring spacing, so two units repositioning in the same
    // window don't both independently claim the same spot and end up stacked together.
    public Vector3 PlanningPosition => (agent != null && agent.hasPath) ? agent.destination : transform.position;

    private Vector3 PickTacticalDestination()
    {
        Vector3 best = transform.position;
        float bestScore = -float.MaxValue;
        Vector3 targetPos = currentTarget != null ? currentTarget.position : lastKnownPosition;

        for (int i = 0; i < repositionCandidateAttempts; i++)
        {
            Vector3 candidate;

            if (currentTarget != null)
            {
                // Sample around an arc at (roughly) the optimal combat range from the target,
                // rather than a circle around ourselves. Sampling around ourselves is what was
                // causing everyone to independently pick "closer to the target" as the best
                // move, which collapses the whole squad into a single-file column advancing
                // straight at the enemy instead of spreading into a line/arc around them.
                float angleDegrees = Random.Range(0f, 360f);
                Vector3 arcDir = Quaternion.Euler(0f, angleDegrees, 0f) * Vector3.forward;
                float rangeFromTarget = Random.Range(optimalCombatRange - 2f, optimalCombatRange + 2f);
                Vector3 arcPoint = targetPos + arcDir * rangeFromTarget;

                // Clamp how far we can move in one reposition so units don't teleport across the arc.
                candidate = Vector3.MoveTowards(transform.position, arcPoint, repositionRadius);
            }
            else
            {
                // No target yet (e.g. holding position) - fall back to a plain random offset.
                // Use insideUnitCircle instead of onUnitSphere-with-y-zeroed: onUnitSphere can
                // return a near-vertical vector, which after zeroing y and normalizing can
                // collapse to a zero-length vector and leave the unit stuck in place.
                Vector2 rand2D = Random.insideUnitCircle.normalized;
                Vector3 randomDir = new Vector3(rand2D.x, 0f, rand2D.y);
                candidate = transform.position + (randomDir * Random.Range(minRepositionDistance, repositionRadius));
            }

            candidate.y = transform.position.y;

            if (!HasWallClearance(candidate)) continue;

            float score = 0f;
            float closestAllyDist = float.MaxValue;

            foreach (var other in ActiveEnemies)
            {
                if (other == null || other == this) continue;

                // CRITICAL FIX: Only evaluate spacing against units on the SAME team
                if (!HasTag(other, gameObject.tag)) continue;

                float d = Vector3.Distance(candidate, other.PlanningPosition);
                if (d < closestAllyDist) closestAllyDist = d;
            }

            if (closestAllyDist < minSeparationDistance) score -= 100f;
            else score += closestAllyDist * 0.2f;

            if (currentTarget != null)
            {
                Vector3 eyePos = candidate + (Vector3.up * eyeHeightCrouched);
                Vector3 targetAimPoint = targetPos + (Vector3.up * targetAimHeight);

                if (!Physics.Linecast(eyePos, targetAimPoint, lineOfSightBlockers))
                {
                    score += 50f;
                }

                Vector3 currentDir = (targetPos - transform.position).normalized;
                Vector3 candidateDir = (targetPos - candidate).normalized;
                float angleDot = Vector3.Dot(currentDir, candidateDir);

                score += (1f - angleDot) * 15f;

                // Firing Line Logic: hold the optimal combat range and don't let units drift
                // closer than that. This now ALWAYS applies - previously it was nested inside
                // "if (pressAdvantage)", so turning pressAdvantage off removed all range
                // discipline instead of just the aggressive push, which is why units kept
                // creeping into close range even with the toggle off.
                float candidateDistToTarget = Vector3.Distance(candidate, targetPos);

                if (candidateDistToTarget > optimalCombatRange)
                {
                    float currentDistToTarget = Vector3.Distance(transform.position, targetPos);
                    float improvement = currentDistToTarget - candidateDistToTarget;

                    // pressAdvantage only controls how eagerly they close distance when they're
                    // too far out; the "don't get too close" penalty below is unconditional.
                    float bonus = pressAdvantage ? advanceScoreBonus : advanceScoreBonus * 0.3f;
                    score += improvement * bonus;
                }
                else if (candidateDistToTarget < optimalCombatRange - 3f)
                {
                    // Penalize pushing too deep past the line, preventing them from running directly into the enemy
                    score -= 50f;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    private bool HasWallClearance(Vector3 point)
    {
        if (lineOfSightBlockers.value == 0) return true;
        Vector3 checkPoint = point + Vector3.up * eyeHeightStanding;
        return !IsEmbeddedInObstruction(checkPoint, wallClearanceDistance);
    }

    private bool IsEmbeddedInObstruction(Vector3 point, float radius)
    {
        Collider[] hits = Physics.OverlapSphere(point, radius, lineOfSightBlockers);
        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            return true;
        }
        return false;
    }

    private bool DetermineCrouchStance()
    {
        if (currentTarget == null || lineOfSightBlockers.value == 0) return true;

        Vector3 crouchedEye = transform.position + Vector3.up * eyeHeightCrouched;
        Vector3 targetPoint = currentTarget.position + Vector3.up * targetAimHeight;

        if (IsEmbeddedInObstruction(crouchedEye, 0.05f)) return false;
        return !Physics.Linecast(crouchedEye, targetPoint, lineOfSightBlockers);
    }

    private bool HasLineOfSightAtCurrentStance()
    {
        if (currentTarget == null) return false;
        if (lineOfSightBlockers.value == 0) return true;

        float eyeHeight = isCrouchedStance ? eyeHeightCrouched : eyeHeightStanding;
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPoint = currentTarget.position + Vector3.up * targetAimHeight;

        if (IsEmbeddedInObstruction(eye, 0.05f)) return false;
        return !Physics.Linecast(eye, targetPoint, lineOfSightBlockers);
    }

    // True if the collider carries any of this unit's target tags. Centralizes the multi-tag
    // check so every "is this an enemy?" test (targeting, contested-point checks) stays consistent.
    // Tag check that never errors. CompareTag logs "Tag: X is not defined" whenever a tag isn't in the
    // project's tag list - this project's list is empty, and Ally/Enemy only exist at runtime when the
    // scene contains objects carrying them (so a scene with no American soldiers spams that error).
    private static bool HasTag(Component c, string tag)
    {
        return c != null && c.gameObject.tag == tag;
    }

    private bool IsHostile(Component c)
    {
        if (c == null || targetTags == null) return false;
        for (int i = 0; i < targetTags.Length; i++)
        {
            if (!string.IsNullOrEmpty(targetTags[i]) && HasTag(c, targetTags[i])) return true;
        }
        return false;
    }

    private Transform FindTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius);
        Transform best = null;
        float bestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            if (!IsHostile(hit)) continue;

            Vector3 toTarget = hit.transform.position - transform.position;

            if (fieldOfViewAngle < 360f)
            {
                float angle = Vector3.Angle(transform.forward, toTarget);
                if (angle > fieldOfViewAngle * 0.5f) continue;
            }

            if (lineOfSightBlockers.value != 0)
            {
                Vector3 eyeOrigin = transform.position + Vector3.up * eyeHeightStanding;
                if (IsEmbeddedInObstruction(eyeOrigin, 0.05f)) continue;

                Vector3 targetPoint = hit.transform.position + Vector3.up * targetAimHeight;
                if (Physics.Linecast(eyeOrigin, targetPoint, lineOfSightBlockers)) continue;
            }

            float dist = toTarget.sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = hit.transform;
            }
        }

        return best;
    }

    private bool TargetStillValid()
    {
        if (currentTarget == null) return false;
        return Vector3.Distance(transform.position, currentTarget.position) <= detectionRadius;
    }

    private void FaceTarget()
    {
        if (currentTarget == null) return;
        Vector3 dir = currentTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, aimRotationOffsetDegrees, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
    }

    private void SnapFaceTarget()
    {
        if (currentTarget == null) return;
        Vector3 dir = currentTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, aimRotationOffsetDegrees, 0f);
    }

    private void SteerTowardAgentPath()
    {
        if (agent == null || agent.pathPending || agent.remainingDistance < agent.stoppingDistance) return;

        Vector3 pathDir = agent.desiredVelocity;
        pathDir.y = 0f;

        if (pathDir.sqrMagnitude < 0.01f) return;
        pathDir.Normalize();

        // NavMeshAgent's own local avoidance can't reliably steer around allies here (updatePosition
        // is false and OnAnimatorMove overwrites agent.nextPosition from root motion each frame, so
        // desiredVelocity basically ignores neighbours). We blend in a manual separation push - but
        // it must only NUDGE the heading, never dominate it. Previously the raw, uncapped separation
        // sum (which grows with every crowded neighbour) could exceed the path direction and swing
        // the heading sideways or backwards; moving that way reshuffled the neighbours, which swung
        // it again next frame - a feedback loop that made clustered units run in circles until they
        // happened to drift clear. Clamping the push to a fraction of the (unit-length) path
        // direction, then rejecting any backward result, keeps the path primary and stops the spin.
        Vector3 separation = ComputeAllySeparation() * allyAvoidanceStrength;
        float maxNudge = 0.75f; // separation can bend heading up to this, but path stays dominant
        if (separation.magnitude > maxNudge) separation = separation.normalized * maxNudge;

        Vector3 dir = pathDir + separation;
        dir.y = 0f;

        // Never let the blended heading point against the path - if it does, drop separation and
        // just follow the path this frame (the unit will clear the crowd by moving forward anyway).
        if (Vector3.Dot(dir, pathDir) <= 0f) dir = pathDir;

        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
    }

    // Simple separation steering: pushes away from same-team allies that are closer than
    // minSeparationDistance, stronger the closer they are. Zero contribution from allies
    // already far enough away.
    private Vector3 ComputeAllySeparation()
    {
        Vector3 separation = Vector3.zero;

        foreach (var other in ActiveEnemies)
        {
            if (other == null || other == this) continue;
            if (!HasTag(other, gameObject.tag)) continue;

            Vector3 offset = transform.position - other.transform.position;
            offset.y = 0f;
            float dist = offset.magnitude;

            if (dist > 0.0001f && dist < minSeparationDistance)
            {
                separation += offset.normalized * (minSeparationDistance - dist);
            }
        }

        return separation;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
}