using UnityEngine;

/// <summary>
/// Attach to a GameObject with a trigger Collider. When the player walks
/// through it, it becomes their new respawn point. Place these throughout
/// the level (e.g. at the start of each section/checkpoint).
/// </summary>
[RequireComponent(typeof(Collider))]
public class respawnPoint : MonoBehaviour
{
    [Tooltip("Tag used on the player object. Make sure your player GameObject has this tag.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Optional: play a sound/VFX/UI popup the first time this checkpoint is activated.")]
    [SerializeField] private GameObject activationEffectPrefab;

    private bool hasBeenActivated = false;

    // Editor callback when the component is added: makes the collider a trigger.
    private void Reset()
    {
        // Make sure the collider is set up correctly as soon as this is added
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    // When the player walks in, makes this their respawn point and plays the activation effect the first time.
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        FirstPersonController player = other.GetComponent<FirstPersonController>();
        if (player == null)
        {
            // Handles the case where the collider is on a child object
            player = other.GetComponentInParent<FirstPersonController>();
        }

        if (player == null) return;

        player.RegisterRespawnPoint(transform);

        if (!hasBeenActivated)
        {
            hasBeenActivated = true;

            if (activationEffectPrefab != null)
            {
                Instantiate(activationEffectPrefab, transform.position, transform.rotation);
            }
        }
    }
}