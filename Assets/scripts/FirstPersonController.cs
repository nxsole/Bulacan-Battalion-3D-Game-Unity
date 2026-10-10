using System.Collections;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AudioSource))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Crouching & Bobbing")]
    [SerializeField] private float standingHeight = 2f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float crouchTransitionSpeed = 10f;
    [SerializeField] private float walkBobSpeed = 14f;
    [SerializeField] private float walkBobAmount = 0.05f;
    [SerializeField] private float crouchBobSpeed = 8f;
    [SerializeField] private float crouchBobAmount = 0.02f;
    private bool isCrouching = false;
    private float cameraStandingY;
    private float cameraCrouchingY;
    private float bobTimer;

    [Header("Look Settings")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float upDownRange = 80f;

    [Header("References")]
    [SerializeField] private Transform playerCamera;
    // The camera this controller actually drives. The win sequence pans THIS one rather than
    // guessing via Camera.main, which may be a different object if the player cam isn't tagged
    // MainCamera - that mismatch is a classic "camera won't pan" cause.
    public Transform PlayerCamera => playerCamera;
    [SerializeField] private Animator weaponAnimator;
    [SerializeField] private AudioSource audioSource;

    [Header("Weapon Pickup")]
    [SerializeField] private GameObject weaponPickupObject;
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private LayerMask interactionLayerMask = ~0;
    [SerializeField] private CanvasGroup interactionPromptCanvasGroup;
    [SerializeField] private TMP_Text interactionPromptText;
    [SerializeField] private string interactionPromptMessage = "Press E to pick up";
    private bool hasWeapon = false;
    private bool isLookingAtPickup = false;

    [Header("Pickup Highlight")]
    [Tooltip("Tint the weapon's material emission when the player looks at it. Works with Standard / URP Lit shaders that have an _EmissionColor property.")]
    [SerializeField] private bool enableHighlight = true;
    [SerializeField] private Color highlightEmission = new Color(1f, 0.85f, 0.3f);
    [Tooltip("How brightly the highlight pulses. 0 = static tint, higher = stronger glow.")]
    [SerializeField] private float highlightIntensity = 1.2f;
    [Tooltip("Speed of the highlight's brightness pulse. Set to 0 for a steady, non-pulsing glow.")]
    [SerializeField] private float highlightPulseSpeed = 3f;

    [Header("Pickup Label Tracking")]
    [Tooltip("If true, the interaction prompt follows the weapon's screen position instead of staying fixed. Requires the prompt to live on a Screen Space canvas.")]
    [SerializeField] private bool trackPromptToWeapon = true;
    [Tooltip("Screen-space pixel offset from the weapon so the label sits above/beside it rather than dead-centre on it.")]
    [SerializeField] private Vector2 promptScreenOffset = new Vector2(0f, 40f);

    // --- highlight/tracking runtime state, populated lazily on first hover ---
    // Cached at the MATERIAL-INSTANCE level, not per-renderer: a renderer can have several
    // material slots (submeshes), and only touching renderer.material (slot 0) is exactly why
    // just one "(Instance)" material was changing. pickupMaterials flattens every slot of every
    // child renderer so all of them get highlighted.
    private Material[] pickupMaterials;
    private Color[] pickupOriginalEmission;
    private bool[] pickupHadEmissionKeyword;
    private Color[] pickupOriginalBaseColor;
    private int[] pickupBaseColorProp; // which base-color property each material has (-1 = none)
    private MaterialGlobalIlluminationFlags[] pickupOriginalGIFlags;
    private bool highlightCached;
    private bool highlightActive;
    private RectTransform promptRectTransform;
    private Canvas promptCanvas;
    private static readonly int HashEmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly int HashBaseColor = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int HashColor = Shader.PropertyToID("_Color");         // Standard / legacy

    [Header("Weapon & Ammo")]
    [SerializeField] private int maxAmmo = 1;
    [SerializeField] private float weaponRange = 100f;
    [SerializeField] private float reloadDuration = 1.5f;
    private int currentAmmo;
    private bool isReloading = false;

    [Header("Aiming (ADS) & Sway")]
    [Tooltip("The transform the AIM/SWAY movement is applied to. This must NOT be the same GameObject as the weapon Animator - if it is, the Animator and this script fight over localPosition and the weapon sinks. Make an empty parent 'AimHolder' between the weapon holder and the animated weapon, put the weapon (with its Animator) as its child, and assign the AimHolder here. If left empty, falls back to weaponTransform (the old, buggy behaviour).")]
    [SerializeField] private Transform aimHolder;
    [Tooltip("The animated weapon object (has the Animator). Activated/deactivated on pickup. Its OWN localPosition is left entirely to the Animator now.")]
    [SerializeField] private Transform weaponTransform;
    [SerializeField] private Vector3 hipFirePosition;
    [SerializeField] private Vector3 aimPosition;
    [Tooltip("Logs the weapon's local position each time you start/stop aiming, to diagnose position drift. Turn off once fixed.")]
    [SerializeField] private bool debugAimLogging = false;
    private bool wasAimingLastFrame = false;
    [SerializeField] private float aimSpeed = 10f;

    [Header("Mouse Sway")]
    [SerializeField] private float swayAmount = 0.02f;
    [SerializeField] private float maxSwayAmount = 0.06f;
    [SerializeField] private float aimSwayMultiplier = 0.3f;

    [Header("Idle/Aim Drift")]
    [SerializeField] private float idleDriftSpeed = 1f;
    [SerializeField] private float idleDriftAmount = 0.005f;
    [SerializeField] private float aimDriftSpeed = 3.5f;
    [SerializeField] private float aimDriftAmount = 0.03f;

    [Header("Effects")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private GameObject impactEffectPrefab;
    [SerializeField] private float muzzleFlashLifetime = 0.1f;
    [SerializeField] private AudioClip fireSound;
    [SerializeField, Range(0f, 1f)] private float fireVolume = 1.0f;

    [Header("Health / Death / Respawn")]
    [SerializeField] private Transform initialSpawnPoint;
    [SerializeField] private float respawnDelay = 3f;
    [SerializeField] private float hitInvulnerabilityDuration = 1f;
    [SerializeField] private float hitCooldownDuration = 0.5f;
    [SerializeField] private int maxHealth = 2;
    [SerializeField] private float lowHealthRegenDelay = 10f;
    private Transform currentRespawnPoint;
    private bool isDead = false;
    private bool isInvulnerable = false;
    private int currentHealth;
    private Coroutine lowHealthRegenCoroutine;

    [Header("Hit Feedback")]
    [SerializeField] private float hitCameraKickAmount = 15f;
    [SerializeField] private float hitCameraKickRecoverySpeed = 10f;
    [SerializeField] private AudioClip gettingShotSound;
    [SerializeField] private AudioSource lowHealthAudioSource;
    [SerializeField] private AudioClip lowHealthSound;
    private float cameraKickOffset = 0f;
    private Coroutine cameraKickCoroutine;

    [Header("Low Health Vignette")]
    [SerializeField] private CanvasGroup lowHealthVignetteCanvasGroup;
    [SerializeField] private float lowHealthVignettePulseSpeed = 4f;
    [SerializeField, Range(0f, 1f)] private float lowHealthVignetteMinAlpha = 0.15f;
    [SerializeField, Range(0f, 1f)] private float lowHealthVignetteMaxAlpha = 0.6f;
    private Coroutine lowHealthVignetteCoroutine;

    [Header("Death Screen UI & Audio")]
    [SerializeField] private CanvasGroup deathScreenCanvasGroup;
    [SerializeField] private TMP_Text deathScreenText;
    [SerializeField] private TMP_Text respawnTimerText;
    [SerializeField] private AudioLowPassFilter audioLowPassFilter;
    [SerializeField] private float muffledCutoffFrequency = 800f;

    [Header("Respawn / Game Over")]
    [Tooltip("How many times the player may respawn before a death triggers the lose sequence. 1 = first death respawns, second death is game over.")]
    [SerializeField] private int allowedRespawns = 1;
    [Tooltip("Seconds the death screen holds on the FINAL death before the lose sequence takes over.")]
    [SerializeField] private float finalDeathHold = 1.5f;
    [Tooltip("The scene's GameOutcomeManager, used to play the lose sequence on the final death. Found automatically if left empty.")]
    [SerializeField] private GameOutcomeManager outcomeManager;
    private int deathCount = 0;
    [SerializeField] private float normalCutoffFrequency = 22000f;

    [Header("Death Audio")]
    [SerializeField] private AudioSource deathAudioSource;
    [SerializeField] private AudioClip deathSoundClip;

    [SerializeField]
    private string[] deathMessages = new string[] { };
    private System.Collections.Generic.List<string> deathMessageBag = new System.Collections.Generic.List<string>();
    private string lastDeathMessage = null;

    private CharacterController controller;
    private Vector3 velocity;
    private float verticalRotation;

    private static readonly int HashFire = Animator.StringToHash("Fire");
    private static readonly int HashReload = Animator.StringToHash("Reload");

    private void OnValidate()
    {
        deathMessageBag.Clear();
        lastDeathMessage = null;
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        currentAmmo = maxAmmo;
        currentHealth = maxHealth;
        cameraStandingY = playerCamera.localPosition.y;
        cameraCrouchingY = cameraStandingY * (crouchHeight / standingHeight);
        currentRespawnPoint = initialSpawnPoint != null ? initialSpawnPoint : transform;

        if (deathScreenCanvasGroup != null)
        {
            deathScreenCanvasGroup.alpha = 0f;
            deathScreenCanvasGroup.blocksRaycasts = false;
            deathScreenCanvasGroup.interactable = false;
        }

        if (lowHealthVignetteCanvasGroup != null)
        {
            lowHealthVignetteCanvasGroup.alpha = 0f;
            lowHealthVignetteCanvasGroup.blocksRaycasts = false;
            lowHealthVignetteCanvasGroup.interactable = false;
        }

        if (weaponTransform != null)
        {
            weaponTransform.gameObject.SetActive(false);
        }

        if (interactionPromptCanvasGroup != null)
        {
            interactionPromptCanvasGroup.alpha = 0f;
        }

        if (interactionPromptText != null && !string.IsNullOrEmpty(interactionPromptMessage))
        {
            interactionPromptText.text = interactionPromptMessage;
        }

        if (interactionPromptCanvasGroup != null)
        {
            promptCanvas = interactionPromptCanvasGroup.GetComponentInParent<Canvas>();
        }

        // Track the TEXT's own RectTransform when we have it - the CanvasGroup is often a
        // full-screen container, and moving that does nothing visible. The text element is
        // the thing the player actually sees, so that's what should follow the weapon.
        if (interactionPromptText != null)
        {
            promptRectTransform = interactionPromptText.rectTransform;
        }
        else if (interactionPromptCanvasGroup != null)
        {
            promptRectTransform = interactionPromptCanvasGroup.GetComponent<RectTransform>();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (isDead) return;

        HandleMouseLook();
        HandleCrouchAndBob();
        HandleMovement();

        if (hasWeapon)
        {
            HandleAimingAndSway();
            HandleShootingAndReloading();
        }
        else
        {
            HandleWeaponPickup();
        }
    }

    private void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -upDownRange, upDownRange);
        playerCamera.localRotation = Quaternion.Euler(verticalRotation - cameraKickOffset, 0f, 0f);

        transform.Rotate(Vector3.up * mouseX);
    }

    private void HandleCrouchAndBob()
    {
        isCrouching = Input.GetKey(KeyCode.C);

        float targetHeight = isCrouching ? crouchHeight : standingHeight;
        float baseCameraY = isCrouching ? cameraCrouchingY : cameraStandingY;

        if (!Mathf.Approximately(controller.height, targetHeight))
        {
            controller.height = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);

            if (Mathf.Abs(controller.height - targetHeight) < 0.001f)
            {
                controller.height = targetHeight;
            }

            controller.center = new Vector3(0, controller.height / 2f, 0);
        }

        float currentSpeed = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
        float bobOffset = 0f;

        if (controller.isGrounded && currentSpeed > 0.1f)
        {
            bobTimer += Time.deltaTime * (isCrouching ? crouchBobSpeed : walkBobSpeed);
            bobOffset = Mathf.Sin(bobTimer) * (isCrouching ? crouchBobAmount : walkBobAmount);
        }
        else
        {
            bobTimer = 0f;
        }

        Vector3 camPos = playerCamera.localPosition;
        camPos.y = Mathf.Lerp(camPos.y, baseCameraY + bobOffset, Time.deltaTime * crouchTransitionSpeed);
        playerCamera.localPosition = camPos;
    }

    private void HandleMovement()
    {
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        float currentSpeed = isCrouching ? crouchSpeed : walkSpeed;

        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        controller.Move(move * currentSpeed * Time.deltaTime);

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    [Header("DEBUG (turn off once working)")]
    [SerializeField] private bool debugPickupLogging = true;

    private void HandleWeaponPickup()
    {
        bool foundPickup = false;

        if (weaponPickupObject != null && weaponPickupObject.activeInHierarchy)
        {
            Ray ray = new Ray(playerCamera.position, playerCamera.forward);

            if (Physics.Raycast(ray, out RaycastHit hit, interactionRange, interactionLayerMask))
            {
                if (hit.transform == weaponPickupObject.transform || hit.transform.IsChildOf(weaponPickupObject.transform))
                {
                    foundPickup = true;
                }
                else if (debugPickupLogging)
                {
                    Debug.Log($"[Pickup] Ray hit '{hit.transform.name}', but that isn't weaponPickupObject ('{weaponPickupObject.name}') or its child.", this);
                }
            }
            else if (debugPickupLogging)
            {
                Debug.Log("[Pickup] Ray hit nothing within interactionRange on interactionLayerMask.", this);
            }
        }
        else if (debugPickupLogging)
        {
            Debug.Log($"[Pickup] weaponPickupObject is {(weaponPickupObject == null ? "NOT ASSIGNED" : "inactive in hierarchy")} - handler can't run.", this);
        }

        if (foundPickup != isLookingAtPickup)
        {
            isLookingAtPickup = foundPickup;
            ShowInteractionPrompt(isLookingAtPickup);
            SetHighlight(isLookingAtPickup);
            if (debugPickupLogging) Debug.Log($"[Pickup] Hover changed -> {foundPickup}. Highlight + prompt toggled.", this);
        }

        // While the weapon is being looked at, keep the prompt glued to its screen position
        // and pulse the highlight. Both run per-frame only during hover, so they're cheap.
        if (foundPickup)
        {
            UpdatePromptTracking();
            UpdateHighlightPulse();
        }

        if (foundPickup && Input.GetKeyDown(KeyCode.E))
        {
            PickUpWeapon();
        }
    }

    // Lazily grabs the pickup's renderers the first time it's hovered, caches each one's
    // original emission AND base colour, then highlights. Emission gives the glow on lit
    // shaders; base-colour tint is a fallback that's visible even on shaders without working
    // emission (e.g. some URP/unlit setups), so SOMETHING always shows.
    private void SetHighlight(bool on)
    {
        if (!enableHighlight || weaponPickupObject == null) return;

        if (!highlightCached)
        {
            // Flatten every material slot of every child renderer into one list. Using
            // renderer.materials (plural) returns/creates an instance array covering ALL
            // submesh slots, so a multi-material mesh (e.g. wood + metal on one renderer)
            // gets every slot highlighted, not just slot 0.
            Renderer[] renderers = weaponPickupObject.GetComponentsInChildren<Renderer>(true);
            var mats = new System.Collections.Generic.List<Material>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                mats.AddRange(r.materials); // instances - safe to modify without touching shared assets
            }
            pickupMaterials = mats.ToArray();

            int n = pickupMaterials.Length;
            pickupOriginalEmission = new Color[n];
            pickupHadEmissionKeyword = new bool[n];
            pickupOriginalBaseColor = new Color[n];
            pickupBaseColorProp = new int[n];
            pickupOriginalGIFlags = new MaterialGlobalIlluminationFlags[n];

            for (int i = 0; i < n; i++)
            {
                Material mat = pickupMaterials[i];
                pickupOriginalGIFlags[i] = mat.globalIlluminationFlags;

                if (mat.HasProperty(HashEmissionColor))
                {
                    pickupOriginalEmission[i] = mat.GetColor(HashEmissionColor);
                    pickupHadEmissionKeyword[i] = mat.IsKeywordEnabled("_EMISSION");
                }

                if (mat.HasProperty(HashBaseColor))
                {
                    pickupBaseColorProp[i] = HashBaseColor;
                    pickupOriginalBaseColor[i] = mat.GetColor(HashBaseColor);
                }
                else if (mat.HasProperty(HashColor))
                {
                    pickupBaseColorProp[i] = HashColor;
                    pickupOriginalBaseColor[i] = mat.GetColor(HashColor);
                }
                else
                {
                    pickupBaseColorProp[i] = -1;
                }

                if (debugPickupLogging)
                {
                    Debug.Log($"[Highlight] Material '{mat.name}' shader='{mat.shader.name}' " +
                              $"hasEmission={mat.HasProperty(HashEmissionColor)} baseColorProp={(pickupBaseColorProp[i] == -1 ? "none" : (pickupBaseColorProp[i] == HashBaseColor ? "_BaseColor" : "_Color"))}", this);
                }
            }
            highlightCached = true;
        }

        highlightActive = on;

        if (!on)
        {
            for (int i = 0; i < pickupMaterials.Length; i++)
            {
                Material mat = pickupMaterials[i];
                if (mat == null) continue;

                if (mat.HasProperty(HashEmissionColor))
                {
                    mat.SetColor(HashEmissionColor, pickupOriginalEmission[i]);
                    if (!pickupHadEmissionKeyword[i]) mat.DisableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = pickupOriginalGIFlags[i];
                }
                if (pickupBaseColorProp[i] != -1)
                {
                    mat.SetColor(pickupBaseColorProp[i], pickupOriginalBaseColor[i]);
                }
            }
        }
        else
        {
            for (int i = 0; i < pickupMaterials.Length; i++)
            {
                Material mat = pickupMaterials[i];
                if (mat == null) continue;
                if (mat.HasProperty(HashEmissionColor))
                {
                    mat.EnableKeyword("_EMISSION");
                    // URP/Standard skip emission when GI flags mark the emissive as black,
                    // which is the default for a material authored with _EmissionColor=(0,0,0).
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
        }
    }

    private void UpdateHighlightPulse()
    {
        if (!enableHighlight || !highlightActive || pickupMaterials == null) return;

        // Pulse brightness with a sine wave (or hold steady if pulse speed is 0).
        float pulse = highlightPulseSpeed > 0f
            ? (0.6f + 0.4f * Mathf.Sin(Time.time * highlightPulseSpeed))
            : 1f;
        Color emission = highlightEmission * (highlightIntensity * pulse);

        // Base-colour tint: lerp the original toward the highlight colour by the pulse amount,
        // so shaders without emission still show a visible pulsing tint.
        float tintAmount = 0.35f + 0.25f * (pulse - 0.6f) / 0.4f; // maps pulse range to a subtle tint

        for (int i = 0; i < pickupMaterials.Length; i++)
        {
            Material mat = pickupMaterials[i];
            if (mat == null) continue;

            if (mat.HasProperty(HashEmissionColor)) mat.SetColor(HashEmissionColor, emission);

            if (pickupBaseColorProp[i] != -1)
            {
                Color tinted = Color.Lerp(pickupOriginalBaseColor[i], highlightEmission, Mathf.Clamp01(tintAmount));
                mat.SetColor(pickupBaseColorProp[i], tinted);
            }
        }
    }

    // Moves the prompt to follow the weapon. Handles both canvas types, because how you set
    // a RectTransform's position depends entirely on the canvas render mode:
    //   - Screen Space (Overlay/Camera): position is in SCREEN PIXELS, so we project the
    //     weapon to screen space and add a pixel offset.
    //   - World Space: position is in WORLD UNITS, so we place the label at the weapon's world
    //     position plus a small world offset and face it toward the camera (billboard).
    // Using screen pixels on a World Space canvas (the original bug) flings the label thousands
    // of units away, which is why nothing appeared to track.
    private void UpdatePromptTracking()
    {
        if (!trackPromptToWeapon || promptRectTransform == null || weaponPickupObject == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        bool worldSpace = promptCanvas != null && promptCanvas.renderMode == RenderMode.WorldSpace;

        if (worldSpace)
        {
            // World offset: promptScreenOffset.y is reused as a height offset (in metres) above
            // the weapon; x as a small sideways offset. Then billboard toward the camera.
            Vector3 worldPos = weaponPickupObject.transform.position
                + Vector3.up * (promptScreenOffset.y * 0.01f)
                + cam.transform.right * (promptScreenOffset.x * 0.01f);

            promptRectTransform.position = worldPos;
            promptRectTransform.rotation = Quaternion.LookRotation(promptRectTransform.position - cam.transform.position);

            if (interactionPromptCanvasGroup != null && isLookingAtPickup)
                interactionPromptCanvasGroup.alpha = 1f;
        }
        else
        {
            Vector3 screenPoint = cam.WorldToScreenPoint(weaponPickupObject.transform.position);

            // z < 0 means the point is behind the camera - WorldToScreenPoint flips it, so guard it.
            if (screenPoint.z < 0f)
            {
                if (interactionPromptCanvasGroup != null) interactionPromptCanvasGroup.alpha = 0f;
                return;
            }

            if (interactionPromptCanvasGroup != null && isLookingAtPickup)
                interactionPromptCanvasGroup.alpha = 1f;

            screenPoint += (Vector3)promptScreenOffset;

            // Setting RectTransform.position directly is unreliable when the element is anchored
            // or under a layout - the canvas can override it. Convert the screen point into the
            // parent rect's local space and set anchoredPosition instead, which layout respects.
            RectTransform parentRect = promptRectTransform.parent as RectTransform;
            if (parentRect != null)
            {
                Camera uiCam = promptCanvas != null && promptCanvas.renderMode == RenderMode.ScreenSpaceCamera
                    ? promptCanvas.worldCamera
                    : null; // Overlay canvases pass null

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parentRect, screenPoint, uiCam, out Vector2 localPoint))
                {
                    promptRectTransform.anchoredPosition = localPoint;
                }
            }
            else
            {
                promptRectTransform.position = screenPoint;
            }
        }
    }

    private void ShowInteractionPrompt(bool show)
    {
        if (interactionPromptCanvasGroup == null) return;
        interactionPromptCanvasGroup.alpha = show ? 1f : 0f;
    }

    private void PickUpWeapon()
    {
        hasWeapon = true;
        currentAmmo = maxAmmo;

        SetHighlight(false); // restore emission before the object is hidden

        if (weaponTransform != null)
        {
            weaponTransform.gameObject.SetActive(true);
        }

        if (weaponPickupObject != null)
        {
            weaponPickupObject.SetActive(false);
        }

        ShowInteractionPrompt(false);
        isLookingAtPickup = false;
    }

    private void HandleAimingAndSway()
    {
        // Apply aim/sway to the holder, NOT the animated weapon. If both this script and the
        // Animator write the same transform's localPosition, they fight and the weapon drifts
        // downward. aimHolder should be a parent of the animated weapon; the Animator then owns
        // the weapon's own localPosition and this owns the holder's. Fall back to weaponTransform
        // only if no holder is assigned (preserves old behaviour rather than doing nothing).
        Transform moveTarget = aimHolder != null ? aimHolder : weaponTransform;
        if (moveTarget == null) return;

        bool isAiming = Input.GetButton("Fire2") && !isReloading;

        if (debugAimLogging && isAiming != wasAimingLastFrame)
        {
            Debug.Log($"[Aim] {(isAiming ? "START" : "STOP")} aim. moveTarget.localPosition = {moveTarget.localPosition} | aimPosition={aimPosition} hipFirePosition={hipFirePosition}", this);
            wasAimingLastFrame = isAiming;
        }

        Vector3 baseTargetPosition = isAiming ? aimPosition : hipFirePosition;

        float currentSwayMultiplier = isAiming ? aimSwayMultiplier : 1f;
        float swayX = -Input.GetAxis("Mouse X") * swayAmount * currentSwayMultiplier;
        float swayY = -Input.GetAxis("Mouse Y") * swayAmount * currentSwayMultiplier;

        swayX = Mathf.Clamp(swayX, -maxSwayAmount, maxSwayAmount);
        swayY = Mathf.Clamp(swayY, -maxSwayAmount, maxSwayAmount);

        float currentDriftSpeed = isAiming ? aimDriftSpeed : idleDriftSpeed;
        float currentDriftAmount = isAiming ? aimDriftAmount : idleDriftAmount;

        float driftX = Mathf.Sin(Time.time * currentDriftSpeed) * currentDriftAmount;
        float driftY = Mathf.Cos(Time.time * currentDriftSpeed * 0.5f) * currentDriftAmount;

        Vector3 targetPosition = baseTargetPosition + new Vector3(swayX + driftX, swayY + driftY, 0f);

        moveTarget.localPosition = Vector3.Lerp(moveTarget.localPosition, targetPosition, Time.deltaTime * aimSpeed);
    }

    private void HandleShootingAndReloading()
    {
        if (weaponAnimator == null) return;

        if (Input.GetButtonDown("Fire1") && !isReloading)
        {
            if (currentAmmo > 0)
            {
                currentAmmo--;
                weaponAnimator.SetTrigger(HashFire);
                ShootFX();
                CastRay();
            }
            else
            {
                StartCoroutine(ReloadRoutine());
            }
        }

        if (Input.GetKeyDown(KeyCode.R) && currentAmmo < maxAmmo && !isReloading)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    private void CastRay()
    {
        Ray ray = new Ray(playerCamera.position, playerCamera.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, weaponRange))
        {
            EnemyAI target = hit.collider.GetComponentInParent<EnemyAI>();

            if (target != null)
            {
                target.Die();
            }
            else if (impactEffectPrefab != null)
            {
                GameObject impact = Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                Destroy(impact, 2f);
            }
        }
    }

    private void ShootFX()
    {
        if (fireSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(fireSound, fireVolume);
        }

        if (muzzleFlashPrefab != null && firePoint != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, firePoint.position, firePoint.rotation, firePoint);
            Destroy(flash, muzzleFlashLifetime);
        }
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        weaponAnimator.SetTrigger(HashReload);

        yield return new WaitForSeconds(reloadDuration);

        currentAmmo = maxAmmo;
        isReloading = false;
    }

    public void TakeHit()
    {
        if (isDead || isInvulnerable) return;

        PlayGettingShotSound();
        TriggerCameraKick();

        currentHealth--;

        if (currentHealth <= 0)
        {
            StopLowHealthRegen();
            StartCoroutine(DeathSequence());
        }
        else
        {
            EnterLowHealthState();
            StartCoroutine(HitCooldownRoutine());
        }
    }

    private IEnumerator HitCooldownRoutine()
    {
        isInvulnerable = true;
        yield return new WaitForSeconds(hitCooldownDuration);
        isInvulnerable = false;
    }

    public void RegisterRespawnPoint(Transform point)
    {
        currentRespawnPoint = point;
    }

    private void PlayGettingShotSound()
    {
        if (audioSource != null && gettingShotSound != null)
        {
            audioSource.PlayOneShot(gettingShotSound);
        }
    }

    private void TriggerCameraKick()
    {
        if (cameraKickCoroutine != null)
        {
            StopCoroutine(cameraKickCoroutine);
        }
        cameraKickCoroutine = StartCoroutine(CameraKickRoutine());
    }

    private IEnumerator CameraKickRoutine()
    {
        cameraKickOffset = hitCameraKickAmount;

        while (cameraKickOffset > 0.01f)
        {
            cameraKickOffset = Mathf.Lerp(cameraKickOffset, 0f, Time.deltaTime * hitCameraKickRecoverySpeed);
            yield return null;
        }

        cameraKickOffset = 0f;
        cameraKickCoroutine = null;
    }

    private void EnterLowHealthState()
    {
        if (lowHealthAudioSource != null && lowHealthSound != null)
        {
            if (lowHealthAudioSource.clip != lowHealthSound)
            {
                lowHealthAudioSource.clip = lowHealthSound;
            }
            lowHealthAudioSource.loop = true;

            if (!lowHealthAudioSource.isPlaying)
            {
                lowHealthAudioSource.Play();
            }
        }

        if (lowHealthVignetteCanvasGroup != null && lowHealthVignetteCoroutine == null)
        {
            lowHealthVignetteCoroutine = StartCoroutine(LowHealthVignetteRoutine());
        }

        if (lowHealthRegenCoroutine != null)
        {
            StopCoroutine(lowHealthRegenCoroutine);
        }
        lowHealthRegenCoroutine = StartCoroutine(LowHealthRegenRoutine());
    }

    private IEnumerator LowHealthVignetteRoutine()
    {
        while (true)
        {
            float pulse = (Mathf.Sin(Time.time * lowHealthVignettePulseSpeed) + 1f) * 0.5f;
            lowHealthVignetteCanvasGroup.alpha = Mathf.Lerp(lowHealthVignetteMinAlpha, lowHealthVignetteMaxAlpha, pulse);
            yield return null;
        }
    }

    private void StopLowHealthVignette()
    {
        if (lowHealthVignetteCoroutine != null)
        {
            StopCoroutine(lowHealthVignetteCoroutine);
            lowHealthVignetteCoroutine = null;
        }

        if (lowHealthVignetteCanvasGroup != null)
        {
            lowHealthVignetteCanvasGroup.alpha = 0f;
        }
    }

    private IEnumerator LowHealthRegenRoutine()
    {
        yield return new WaitForSeconds(lowHealthRegenDelay);

        currentHealth = maxHealth;
        StopLowHealthAudio();
        StopLowHealthVignette();
        lowHealthRegenCoroutine = null;
    }

    private void StopLowHealthRegen()
    {
        if (lowHealthRegenCoroutine != null)
        {
            StopCoroutine(lowHealthRegenCoroutine);
            lowHealthRegenCoroutine = null;
        }

        StopLowHealthAudio();
        StopLowHealthVignette();
    }

    private void StopLowHealthAudio()
    {
        if (lowHealthAudioSource != null && lowHealthAudioSource.isPlaying)
        {
            lowHealthAudioSource.Stop();
        }
    }

    private IEnumerator DeathSequence()
    {
        isDead = true;
        isReloading = false;
        deathCount++;

        ShowDeathScreen();

        // Out of respawns -> this is game over. Hold the death screen briefly so the death reads,
        // then hand off to the outcome manager's lose sequence (typewriter + menu). We do NOT
        // respawn or clear isDead - the player stays down and control never returns.
        if (deathCount > allowedRespawns)
        {
            if (outcomeManager == null) outcomeManager = FindObjectOfType<GameOutcomeManager>();

            yield return new WaitForSeconds(finalDeathHold);

            if (outcomeManager != null)
            {
                HideDeathScreen(); // the lose sequence draws its own black + text
                outcomeManager.TriggerLose();
            }
            else
            {
                Debug.LogWarning("[FirstPersonController] No GameOutcomeManager found - can't play lose sequence on final death. Player will just stay on the death screen.", this);
            }
            yield break;
        }

        float timer = respawnDelay;
        while (timer > 0f)
        {
            if (respawnTimerText != null)
            {
                respawnTimerText.text = $"Respawning in {Mathf.CeilToInt(timer)}...";
            }

            timer -= Time.deltaTime;
            yield return null;
        }

        RespawnPlayer();
        HideDeathScreen();

        isDead = false;

        if (hitInvulnerabilityDuration > 0f)
        {
            StartCoroutine(InvulnerabilityWindow());
        }
    }

    private IEnumerator InvulnerabilityWindow()
    {
        isInvulnerable = true;
        yield return new WaitForSeconds(hitInvulnerabilityDuration);
        isInvulnerable = false;
    }

    private void ShowDeathScreen()
    {
        if (deathScreenCanvasGroup == null) return;

        deathScreenCanvasGroup.alpha = 1f;
        deathScreenCanvasGroup.blocksRaycasts = true;
        deathScreenCanvasGroup.interactable = true;

        if (deathScreenText != null && deathMessages.Length > 0)
        {
            deathScreenText.text = GetNextDeathMessage();
        }

        if (audioLowPassFilter != null)
        {
            audioLowPassFilter.cutoffFrequency = muffledCutoffFrequency;
        }

        if (deathAudioSource != null && deathSoundClip != null)
        {
            deathAudioSource.clip = deathSoundClip;
            deathAudioSource.Play();
        }
    }

    private string GetNextDeathMessage()
    {
        if (deathMessageBag.Count == 0)
        {
            RefillDeathMessageBag();
        }

        int lastIndex = deathMessageBag.Count - 1;
        string message = deathMessageBag[lastIndex];
        deathMessageBag.RemoveAt(lastIndex);

        lastDeathMessage = message;
        return message;
    }

    private void RefillDeathMessageBag()
    {
        deathMessageBag.Clear();
        deathMessageBag.AddRange(deathMessages);

        for (int i = deathMessageBag.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (deathMessageBag[i], deathMessageBag[j]) = (deathMessageBag[j], deathMessageBag[i]);
        }

        if (deathMessages.Length > 1 && deathMessageBag[deathMessageBag.Count - 1] == lastDeathMessage)
        {
            (deathMessageBag[deathMessageBag.Count - 1], deathMessageBag[0]) =
                (deathMessageBag[0], deathMessageBag[deathMessageBag.Count - 1]);
        }
    }

    private void HideDeathScreen()
    {
        if (deathScreenCanvasGroup == null) return;

        deathScreenCanvasGroup.alpha = 0f;
        deathScreenCanvasGroup.blocksRaycasts = false;
        deathScreenCanvasGroup.interactable = false;

        if (audioLowPassFilter != null)
        {
            audioLowPassFilter.cutoffFrequency = normalCutoffFrequency;
        }

        if (deathAudioSource != null)
        {
            deathAudioSource.Stop();
        }
    }

    private void RespawnPlayer()
    {
        controller.enabled = false;

        transform.position = currentRespawnPoint.position;
        transform.rotation = currentRespawnPoint.rotation;

        velocity = Vector3.zero;
        currentAmmo = maxAmmo;
        currentHealth = maxHealth;
        cameraKickOffset = 0f;
        StopLowHealthRegen();

        controller.enabled = true;
    }
}