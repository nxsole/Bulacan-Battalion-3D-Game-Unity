using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace TiradPass
{
    [System.Serializable]
    public struct Level2DialogueLine
    {
        public string speakerName;
        public Color speakerColor;
        [TextArea(2, 5)]
        public string dialogueText;
        public Transform cameraTarget;
        public AudioClip voiceClip;
        public float delayAfterLine;

        [Header("Custom Camera Settings")]
        public float customMoveSpeed;
        public float customRotateSpeed;
        [Tooltip("Check this if you want the camera to instantly jump to this target instead of moving/lerping to it")]
        public bool instantCut;
    }

    public class level2IntroCutscene : MonoBehaviour
    {
        [Header("Camera & UI References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private GameObject playerObject; // Drag your Player object / FirstPersonController here
        [SerializeField] private TextMeshProUGUI speakerNameText;
        [SerializeField] private TextMeshProUGUI dialogueContentText;
        [SerializeField] private CanvasGroup dialogueCanvasGroup;
        [SerializeField] private CanvasGroup screenFadeCanvasGroup;
        [SerializeField] private Button skipButton;

        [Header("Camera Movement Settings")]
        [SerializeField] private float cameraMoveSpeed = 2.0f;
        [SerializeField] private float cameraRotateSpeed = 2.0f;

        [Header("Opening Shot Targets (Zoom Out)")]
        [Tooltip("Starting close position for the opening shot")]
        [SerializeField] private Transform openingStartTarget;
        [Tooltip("End position for the zoom out effect toward the horizon")]
        [SerializeField] private Transform openingHorizonTarget;
        [SerializeField] private float openingZoomDuration = 3.0f;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource backgroundAmbienceSource;
        [SerializeField] private AudioSource dialogueVoiceSource;
        [SerializeField] private float audioFadeDuration = 1.5f;

        [Header("Dialogue Timing Settings")]
        [SerializeField] private float textTypingSpeed = 0.035f;

        [Header("Camera Targets")]
        [SerializeField] private Transform mateoCamTarget;        // Shot 2 & 4: Mateo
        [SerializeField] private Transform goyoCamTarget;         // Shot 2: General Goyo
        [SerializeField] private Transform sundaloCamTarget;      // Shot 3: Warning Soldier

        [Header("Dialogue Sequence")]
        [SerializeField] private List<Level2DialogueLine> dialogueSequence = new List<Level2DialogueLine>();

        private Transform currentCameraTarget;
        private float currentMoveSpeed;
        private float currentRotateSpeed;
        private Coroutine typingCoroutine;
        private bool isSkipping = false;
        private bool isCutsceneActive = false;

        // Speaker color presets
        private readonly Color colorMateo = new Color(0.95f, 0.8f, 0.4f);
        private readonly Color colorGoyo = new Color(0.95f, 0.75f, 0.3f);
        private readonly Color colorSundalo = new Color(0.85f, 0.85f, 0.85f);

        private void Awake()
        {
            if (skipButton != null)
                skipButton.onClick.AddListener(SkipCutscene);

            if (dialogueCanvasGroup != null)
                dialogueCanvasGroup.alpha = 0f;

            // Snap camera directly to opening start position
            if (mainCamera != null && openingStartTarget != null)
            {
                SnapCameraToTarget(openingStartTarget);
            }

            // Disable player control during cutscene
            if (playerObject != null)
            {
                playerObject.SetActive(false);
            }
        }

        private void Start()
        {
            PlayCutscene();
        }

        private void Update()
        {
            if (isCutsceneActive && mainCamera != null && currentCameraTarget != null)
            {
                mainCamera.transform.position = Vector3.Lerp(
                    mainCamera.transform.position,
                    currentCameraTarget.position,
                    Time.deltaTime * currentMoveSpeed
                );

                mainCamera.transform.rotation = Quaternion.Slerp(
                    mainCamera.transform.rotation,
                    currentCameraTarget.rotation,
                    Time.deltaTime * currentRotateSpeed
                );
            }
        }

        public void SnapCameraToTarget(Transform target)
        {
            if (mainCamera == null || target == null) return;
            mainCamera.transform.position = target.position;
            mainCamera.transform.rotation = target.rotation;
        }

        [ContextMenu("Auto-Fill Level 2 Dialogue Data")]
        public void AutoFillDialogueData()
        {
            dialogueSequence = new List<Level2DialogueLine>
            {
                // First line set with instantCut = true to jump straight to Mateo after the opening shot
                new Level2DialogueLine { speakerName = "MATEO", speakerColor = colorMateo, dialogueText = "Heneral, papalapit na ang mga Amerikano. Sa tingin ninyo, kaya natin silang pigilan?", cameraTarget = mateoCamTarget, delayAfterLine = 1.0f, instantCut = true },
                new Level2DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Maaaring mas marami sila sa atin, Mateo. Ngunit ang makitid na daanang ito ang ating kalamangan.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new Level2DialogueLine { speakerName = "MATEO", speakerColor = colorMateo, dialogueText = "Paano po kung makalusot sila?", cameraTarget = mateoCamTarget, delayAfterLine = 1.0f },
                new Level2DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Lalaban tayo hanggang sa huli. Bawat sandaling mapigilan natin sila ay pagkakataon.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new Level2DialogueLine { speakerName = "SUNDALO", speakerColor = colorSundalo, dialogueText = "Heneral! Paparating na ang mga Amerikano!", cameraTarget = sundaloCamTarget, delayAfterLine = 1.0f },
                new Level2DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Mga kawal! Sa inyong mga puwesto! Hindi natin hahayaang makalusot ang kalaban!", cameraTarget = goyoCamTarget, delayAfterLine = 1.5f }
            };

            Debug.Log("Level 2 Dialogue Sequence Auto-Filled Successfully!");
        }

        public void PlayCutscene()
        {
            if (isCutsceneActive) return;
            isCutsceneActive = true;
            StartCoroutine(RunLevel2CutsceneFlow());
        }

        private IEnumerator RunLevel2CutsceneFlow()
        {
            // 1. OPENING SHOT: Smoothly zoom out toward the horizon
            if (openingStartTarget != null && openingHorizonTarget != null)
            {
                SnapCameraToTarget(openingStartTarget);
                currentCameraTarget = openingHorizonTarget;
                currentMoveSpeed = 0.5f; // Slow cinematic lerp
                currentRotateSpeed = 0.5f;

                yield return new WaitForSeconds(openingZoomDuration);
            }

            // 2. DIALOGUE UI FADE IN
            if (dialogueCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(dialogueCanvasGroup, 0.0f, 1.0f, 0.8f));

            // 3. DIALOGUE SEQUENCE
            for (int i = 0; i < dialogueSequence.Count; i++)
            {
                if (isSkipping) yield break;

                Level2DialogueLine line = dialogueSequence[i];

                // If instantCut is enabled on this line, snap camera instantly before lerping
                if (line.instantCut && line.cameraTarget != null)
                {
                    SnapCameraToTarget(line.cameraTarget);
                }

                currentCameraTarget = line.cameraTarget;
                currentMoveSpeed = line.customMoveSpeed > 0 ? line.customMoveSpeed : cameraMoveSpeed;
                currentRotateSpeed = line.customRotateSpeed > 0 ? line.customRotateSpeed : cameraRotateSpeed;

                if (speakerNameText != null)
                {
                    speakerNameText.text = line.speakerName;
                    speakerNameText.color = line.speakerColor;
                }

                if (dialogueVoiceSource != null && line.voiceClip != null)
                {
                    dialogueVoiceSource.Stop();
                    dialogueVoiceSource.clip = line.voiceClip;
                    dialogueVoiceSource.Play();
                }

                if (typingCoroutine != null) StopCoroutine(typingCoroutine);
                yield return typingCoroutine = StartCoroutine(TypeWriterText(line.dialogueText));

                if (dialogueVoiceSource != null && dialogueVoiceSource.isPlaying)
                {
                    yield return new WaitWhile(() => dialogueVoiceSource.isPlaying && !isSkipping);
                }

                yield return new WaitForSeconds(line.delayAfterLine);
            }

            yield return EndCutsceneAndStartGameplay();
        }

        private IEnumerator TypeWriterText(string fullText)
        {
            if (dialogueContentText == null) yield break;

            dialogueContentText.text = "";
            foreach (char letter in fullText.ToCharArray())
            {
                dialogueContentText.text += letter;
                yield return new WaitForSeconds(textTypingSpeed);
            }
        }

        public void SkipCutscene()
        {
            if (isSkipping) return;
            isSkipping = true;
            StopAllCoroutines();
            if (dialogueVoiceSource != null) dialogueVoiceSource.Stop();
            StartCoroutine(EndCutsceneAndStartGameplay());
        }

        private IEnumerator EndCutsceneAndStartGameplay()
        {
            if (dialogueCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(dialogueCanvasGroup, dialogueCanvasGroup.alpha, 0.0f, 0.5f));

            // Instantly snap back to Mateo's view for gameplay handover
            if (mateoCamTarget != null)
            {
                SnapCameraToTarget(mateoCamTarget);
            }

            yield return new WaitForSeconds(0.3f);

            // Enable player for gameplay control
            if (playerObject != null)
            {
                playerObject.SetActive(true);
            }

            // Lock mouse cursor for FPS gameplay
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            isCutsceneActive = false;

            // Turn off cutscene GameObject
            gameObject.SetActive(false);
        }

        private IEnumerator FadeCanvasGroup(CanvasGroup cg, float startAlpha, float endAlpha, float duration)
        {
            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
                yield return null;
            }
            cg.alpha = endAlpha;
        }
    }
}