using UnityEngine;

// Level 2 only. Hold Shift to sprint. FirstPersonController (the Level 1 player script) is left
// untouched: it keeps doing its normal walk, and this adds the extra speed on top through the same
// CharacterController, in the direction the player is already moving.
//
// No sprinting while crouched (C), aiming (right mouse), dead (death screen up), or while the player
// script is switched off (pause menu, win/lose screen). Moving backwards doesn't sprint either, unless
// Sprint In Any Direction is on (the Level 3 boss fight), then any of W/A/S/D sprints.
[RequireComponent(typeof(CharacterController))]
public class Level2Sprint : MonoBehaviour
{
    [SerializeField] private FirstPersonController player;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
    [Tooltip("Speed added on top of the player's walk speed while sprinting. Level 2's player walks at 1.3, so 3.2 makes a 4.5 m/s sprint.")]
    [SerializeField] private float sprintBonusSpeed = 3.2f;
    [Tooltip("Seconds to reach full sprint speed, and to slow back down.")]
    [SerializeField] private float rampTime = 0.25f;
    [Tooltip("Off: only forward (W) sprints. On: any movement key (W/A/S/D) sprints.")]
    [SerializeField] private bool sprintInAnyDirection = false;

    [Header("Feel")]
    [Tooltip("Camera field of view widens by this many degrees at full sprint. 0 = off.")]
    [SerializeField] private float fovKick = 6f;
    [SerializeField] private Camera playerCamera;

    [Header("Matching FirstPersonController")]
    [SerializeField] private KeyCode crouchKey = KeyCode.C;
    [SerializeField] private string aimButton = "Fire2";
    [Tooltip("The player's death screen. While it's showing the player is dead and can't sprint.")]
    [SerializeField] private CanvasGroup deathScreen;

    private CharacterController controller;
    private float sprintBlend; // 0 = walking, 1 = full sprint
    private float baseFov;

    public bool IsSprinting => sprintBlend > 0.5f;

    // Caches the CharacterController, finds the player script and remembers the camera's normal field of view.
    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (player == null) player = GetComponent<FirstPersonController>();
        if (playerCamera != null) baseFov = playerCamera.fieldOfView;
    }

    // Ramps the sprint up or down from input and player state, adds the extra speed, and widens the FOV while sprinting.
    private void Update()
    {
        float forward = Input.GetAxis("Vertical");
        float strafe = Input.GetAxis("Horizontal");

        bool canSprint = player != null && player.enabled && controller.enabled
            && Input.GetKey(sprintKey)
            && (sprintInAnyDirection ? Mathf.Abs(forward) > 0.1f || Mathf.Abs(strafe) > 0.1f : forward > 0.1f)
            && !Input.GetKey(crouchKey) && !Input.GetButton(aimButton)
            && (deathScreen == null || deathScreen.alpha < 0.5f);

        float step = rampTime > 0f ? Time.deltaTime / rampTime : 1f;
        sprintBlend = Mathf.MoveTowards(sprintBlend, canSprint ? 1f : 0f, step);

        if (sprintBlend > 0f && controller.enabled)
        {
            Vector3 move = Vector3.ClampMagnitude(transform.right * strafe + transform.forward * forward, 1f);
            controller.Move(move * (sprintBonusSpeed * sprintBlend * Time.deltaTime));
        }

        if (playerCamera != null && fovKick != 0f && player != null && player.enabled)
            playerCamera.fieldOfView = baseFov + fovKick * sprintBlend;
    }
}
