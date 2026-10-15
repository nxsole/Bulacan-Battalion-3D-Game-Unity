using System.Collections.Generic;
using UnityEngine;

// Attach to the player (or camera). Raycasts from wherever the player is currently
// looking and, on the order key, sends every active ally EnemyAI to that point.
//
// NOTE: this now targets the EnemyAI component directly (via its new ReceiveMoveOrder
// method) instead of a separate AllyOrderModule - since allies and enemies both run the
// same EnemyAI script (just with different tags/targetTag), the order logic lives inside
// EnemyAI itself now, as a new PlayerOrder state alongside its existing Idle/Aim/Reposition/
// etc. states. AllyOrderModule.cs is no longer needed and can be deleted from the project.
public class PlayerOrderIssuer : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode orderKey = KeyCode.G;

    [Header("Raycast")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float maxOrderDistance = 60f;
    [Tooltip("Should match whatever your ground/level geometry sits on, so the order point lands on walkable terrain rather than a wall or a prop.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Squad")]
    [Tooltip("Tag used by player-allied troops.")]
    [SerializeField] private string allyTag = "Ally";
    [Tooltip("If set, only allies within this radius of the player receive the order - useful if allies from a different part of the level share the same tag.")]
    [SerializeField] private bool limitToNearbyAllies = false;
    [SerializeField] private float nearbyAllyRadius = 50f;

    [Header("Feedback (optional)")]
    [SerializeField] private GameObject orderMarkerPrefab;
    [SerializeField] private float orderMarkerLifetime = 2f;

    [Header("Voice (optional)")]
    [Tooltip("If left empty, no voice line is played.")]
    [SerializeField] private AudioClip[] orderVoiceClips;
    [SerializeField, Range(0f, 1f)] private float orderVoiceVolume = 1f;
    [Tooltip("If left unassigned, an AudioSource is added to this GameObject automatically.")]
    [SerializeField] private AudioSource voiceAudioSource;

    [Header("Debug")]
    [Tooltip("Logs to the Console every time an order is issued, and why it might not have been (no raycast hit, no allies found, etc). Turn off once everything's confirmed working.")]
    [SerializeField] private bool debugLogging = true;

    // Falls back to the main camera and makes sure there's an AudioSource for the voice lines.
    private void Awake()
    {
        if (playerCamera == null) playerCamera = Camera.main;

        if (voiceAudioSource == null && orderVoiceClips != null && orderVoiceClips.Length > 0)
        {
            voiceAudioSource = GetComponent<AudioSource>();
            if (voiceAudioSource == null) voiceAudioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    // Issues an order when the order key is pressed.
    private void Update()
    {
        if (Input.GetKeyDown(orderKey))
        {
            TryIssueOrder();
        }
    }

    // Raycasts to where the player is looking and sends every eligible ally there, with an optional marker and voice line.
    private void TryIssueOrder()
    {
        if (playerCamera == null)
        {
            if (debugLogging) Debug.LogWarning("[PlayerOrderIssuer] No camera assigned and Camera.main is null. Assign Player Camera or tag your camera 'MainCamera'.");
            return;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxOrderDistance, groundMask))
        {
            if (debugLogging) Debug.Log("[PlayerOrderIssuer] Raycast hit nothing on Ground Mask - check the mask and that you're looking at ground within Max Order Distance.");
            return; // Player isn't looking at anywhere valid to send troops.
        }

        List<EnemyAI> squad = GatherSquad();
        if (squad.Count == 0)
        {
            if (debugLogging) Debug.Log($"[PlayerOrderIssuer] No EnemyAI found on any GameObject tagged '{allyTag}'. Check the tag and that the component is attached and enabled.");
            return;
        }

        if (debugLogging) Debug.Log($"[PlayerOrderIssuer] Order issued to {squad.Count} ally(ies) -> {hit.point}");

        foreach (var ally in squad)
        {
            ally.ReceiveMoveOrder(hit.point);
        }

        if (orderMarkerPrefab != null)
        {
            GameObject marker = Instantiate(orderMarkerPrefab, hit.point, Quaternion.identity);
            Destroy(marker, orderMarkerLifetime);
        }

        PlayOrderVoiceLine();
    }

    // Plays a random order voice clip, if any are assigned.
    private void PlayOrderVoiceLine()
    {
        if (voiceAudioSource == null || orderVoiceClips == null || orderVoiceClips.Length == 0) return;

        AudioClip clip = orderVoiceClips[Random.Range(0, orderVoiceClips.Length)];
        if (clip != null) voiceAudioSource.PlayOneShot(clip, orderVoiceVolume);
    }

    // Collects the active ally EnemyAIs (optionally only those near the player) that should receive the order.
    private List<EnemyAI> GatherSquad()
    {
        var result = new List<EnemyAI>();
        GameObject[] allyObjects = GameObject.FindGameObjectsWithTag(allyTag);

        foreach (var obj in allyObjects)
        {
            var ally = obj.GetComponent<EnemyAI>();
            if (ally == null || !ally.isActiveAndEnabled) continue;

            if (limitToNearbyAllies)
            {
                float dist = Vector3.Distance(transform.position, obj.transform.position);
                if (dist > nearbyAllyRadius) continue;
            }

            result.Add(ally);
        }

        return result;
    }
}