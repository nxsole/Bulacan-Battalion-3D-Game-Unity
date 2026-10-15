using System.Collections;
using UnityEngine;

// Press Space to dash a few metres in the direction you're moving (WASD, relative to where you face).
// Standing still does nothing - there's no direction to dash in. FirstPersonController is left
// untouched: the dash is extra movement through the same CharacterController, like Level2Sprint.
//
// No dashing while dead (death screen up) or while the player script is switched off (pause menu,
// win/lose screen). There's a short cooldown so it's a dodge, not a way to travel.
[RequireComponent(typeof(CharacterController))]
public class PlayerDash : MonoBehaviour
{
    [SerializeField] private FirstPersonController player;
    [SerializeField] private KeyCode dashKey = KeyCode.Space;
    [Tooltip("How far one dash carries you.")]
    [SerializeField] private float dashDistance = 4f;
    [Tooltip("How long the dash takes. Short = snappy.")]
    [SerializeField] private float dashDuration = 0.18f;
    [Tooltip("Seconds after a dash before you can dash again.")]
    [SerializeField] private float cooldown = 1.2f;
    [Tooltip("The player's death screen. While it's showing the player is dead and can't dash.")]
    [SerializeField] private CanvasGroup deathScreen;
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Optional whoosh played on each dash.")]
    [SerializeField] private AudioClip dashSound;
    [SerializeField, Range(0f, 1f)] private float dashVolume = 0.8f;

    private CharacterController controller;
    private float readyAt;

    public bool IsDashing { get; private set; }
    // 0 = just dashed, 1 = ready again (for a cooldown indicator)
    public float Readiness => cooldown <= 0f ? 1f : Mathf.Clamp01(1f - (readyAt - Time.time) / cooldown);

    // Caches the CharacterController and fills in any player/audio references left empty.
    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (player == null) player = GetComponent<FirstPersonController>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    // Starts a dash on the dash key, in the current WASD direction, when off cooldown and the player is alive and in control.
    private void Update()
    {
        if (IsDashing || Time.time < readyAt || !Input.GetKeyDown(dashKey)) return;
        if (player == null || !player.enabled || !controller.enabled) return;
        if (deathScreen != null && deathScreen.alpha > 0.5f) return;

        Vector3 dir = transform.right * Input.GetAxisRaw("Horizontal") + transform.forward * Input.GetAxisRaw("Vertical");
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return; // not moving: nowhere to dash
        StartCoroutine(Dash(dir.normalized));
    }

    // Moves the player dashDistance along dir over dashDuration, stopping early if the player is paused or dies.
    private IEnumerator Dash(Vector3 dir)
    {
        IsDashing = true;
        readyAt = Time.time + dashDuration + cooldown;
        if (audioSource != null && dashSound != null) audioSource.PlayOneShot(dashSound, dashVolume);

        float speed = dashDistance / Mathf.Max(0.01f, dashDuration);
        float t = 0f;
        while (t < dashDuration)
        {
            if (player == null || !player.enabled || !controller.enabled) break; // paused or died mid-dash
            float step = Mathf.Min(Time.deltaTime, dashDuration - t);
            controller.Move(dir * speed * step); // walls stop it like any other movement
            t += step;
            yield return null;
        }
        IsDashing = false;
    }
}
