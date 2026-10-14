using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace TiradPass
{
    [System.Serializable]
    public struct Level3DialogueLine
    {
        public string speakerName;
        public Color speakerColor;
        [TextArea(2, 5)]
        public string dialogueText;
        public Transform cameraTarget;
        public float delayAfterLine;
    }

    public class Level3IntroCutscene : MonoBehaviour
    {
        [Header("Camera & UI References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private TextMeshProUGUI speakerNameText;
        [SerializeField] private TextMeshProUGUI dialogueContentText;
        [SerializeField] private CanvasGroup dialogueCanvasGroup;
        [SerializeField] private CanvasGroup screenFadeCanvasGroup;
        [SerializeField] private Button skipButton;

        [Header("Next Scene Settings")]
        [SerializeField] private string gameplaySceneName = "Level3_Gameplay";

        [Header("1. American 1 Zoom-In Camera Targets")]
        [Tooltip("Wide/Medium target where American 1 shot starts")]
        [SerializeField] private Transform american1StartCamTarget;
        [Tooltip("Close-up target where camera zooms in while American 1 speaks")]
        [SerializeField] private Transform american1EndCamTarget;

        [Header("2. American 2 Camera Target")]
        [SerializeField] private Transform american2CamTarget;

        [Header("3. Sky Battlefield Zoom-Out Camera Targets")]
        [Tooltip("Start of sky shot facing down towards the battlefield")]
        [SerializeField] private Transform skyBattlefieldStartCamTarget;
        [Tooltip("Wide target high in the sky looking down over the whole pass")]
        [SerializeField] private Transform skyBattlefieldEndCamTarget;
        [SerializeField] private float skyZoomOutDuration = 3.5f;

        [Header("4. Goyo Reveal Zoom-In Camera Targets")]
        [Tooltip("Wide shot of Goyo on the ridge before zooming in")]
        [SerializeField] private Transform goyoWideStartCamTarget;
        [Tooltip("Tight heroic close-up on Goyo on the ridge")]
        [SerializeField] private Transform goyoCloseUpEndCamTarget;
        [SerializeField] private float goyoZoomInDuration = 3.0f;

        [Header("Goyo Gameobject & Animator")]
        [SerializeField] private Animator goyoAnimator;
        [SerializeField] private string goyoAnimTriggerName = "TriggerReveal";

        [Header("Dialogue Timing")]
        [SerializeField] private float textTypingSpeed = 0.035f;

        [Header("Dialogue Sequence Data")]
        [SerializeField] private List<Level3DialogueLine> dialogueSequence = new List<Level3DialogueLine>();

        private Coroutine typingCoroutine;
        private bool isSkipping = false;
        private bool isCutsceneActive = false;

        private void Awake()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (skipButton != null)
            {
                skipButton.onClick.RemoveAllListeners();
                skipButton.onClick.AddListener(SkipCutscene);
            }

            if (dialogueCanvasGroup != null) dialogueCanvasGroup.alpha = 0f;
            if (screenFadeCanvasGroup != null) screenFadeCanvasGroup.alpha = 1f;

            if (mainCamera != null && american1StartCamTarget != null)
            {
                SnapCamera(american1StartCamTarget);
            }
        }

        private void Start()
        {
            PlayCutscene();
        }

        private void Update()
        {
            if (!isCutsceneActive || isSkipping) return;

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
            {
                SkipCutscene();
            }
        }

        public void PlayCutscene()
        {
            if (isCutsceneActive) return;
            isCutsceneActive = true;
            StartCoroutine(RunLevel3IntroFlow());
        }

        private IEnumerator RunLevel3IntroFlow()
        {
            // =========================================================================
            // PART 1: AMERICAN DIALOGUE SEQUENCE FIRST
            // =========================================================================

            // --- STEP 1: Fade In & American 1 Dialogue + Zoom-In ---
            SnapCamera(american1StartCamTarget);

            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 1.0f));

            if (dialogueCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(dialogueCanvasGroup, 0f, 1f, 0.5f));

            if (dialogueSequence.Count > 0)
            {
                Level3DialogueLine line1 = dialogueSequence[0];
                if (speakerNameText != null)
                {
                    speakerNameText.text = line1.speakerName;
                    speakerNameText.color = line1.speakerColor;
                }

                if (typingCoroutine != null) StopCoroutine(typingCoroutine);
                typingCoroutine = StartCoroutine(TypeWriterText(line1.dialogueText));

                // Smooth Zoom-In on American 1 while he speaks
                if (american1StartCamTarget != null && american1EndCamTarget != null)
                {
                    float zoomDuration = 3.5f;
                    float elapsed = 0f;
                    while (elapsed < zoomDuration && !isSkipping)
                    {
                        elapsed += Time.deltaTime;
                        float smoothT = Mathf.SmoothStep(0f, 1f, elapsed / zoomDuration);
                        mainCamera.transform.position = Vector3.Lerp(american1StartCamTarget.position, american1EndCamTarget.position, smoothT);
                        mainCamera.transform.rotation = Quaternion.Slerp(american1StartCamTarget.rotation, american1EndCamTarget.rotation, smoothT);
                        yield return null;
                    }
                }

                yield return new WaitForSeconds(line1.delayAfterLine);
            }

            // --- STEP 2: American 2 Dialogue ---
            if (dialogueSequence.Count > 1)
            {
                Level3DialogueLine line2 = dialogueSequence[1];
                if (line2.cameraTarget != null) SnapCamera(line2.cameraTarget);

                if (speakerNameText != null)
                {
                    speakerNameText.text = line2.speakerName;
                    speakerNameText.color = line2.speakerColor;
                }

                if (typingCoroutine != null) StopCoroutine(typingCoroutine);
                yield return typingCoroutine = StartCoroutine(TypeWriterText(line2.dialogueText));

                yield return new WaitForSeconds(line2.delayAfterLine);
            }

            // Hide Dialogue UI before sky & goyo transitions
            if (dialogueCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(dialogueCanvasGroup, 1f, 0f, 0.4f));

            // =========================================================================
            // PART 2: SKY BATTLEFIELD & GOYO REVEAL SEQUENCE
            // =========================================================================

            // --- STEP 3: Sky Battlefield Zoom-Out Shot ---
            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, 0.4f));

            if (skyBattlefieldStartCamTarget != null) SnapCamera(skyBattlefieldStartCamTarget);

            if (screenFadeCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 0.5f));

            // Perform Sky Zoom-Out over the pass
            if (skyBattlefieldStartCamTarget != null && skyBattlefieldEndCamTarget != null)
            {
                float elapsed = 0f;
                while (elapsed < skyZoomOutDuration && !isSkipping)
                {
                    elapsed += Time.deltaTime;
                    float smoothT = Mathf.SmoothStep(0f, 1f, elapsed / skyZoomOutDuration);
                    mainCamera.transform.position = Vector3.Lerp(skyBattlefieldStartCamTarget.position, skyBattlefieldEndCamTarget.position, smoothT);
                    mainCamera.transform.rotation = Quaternion.Slerp(skyBattlefieldStartCamTarget.rotation, skyBattlefieldEndCamTarget.rotation, smoothT);
                    yield return null;
                }
            }

            // --- STEP 4: Fade to Black & Goyo Zoom-In Reveal ---
            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, 0.5f));

            if (goyoWideStartCamTarget != null) SnapCamera(goyoWideStartCamTarget);

            // Trigger Goyo Animation
            if (goyoAnimator != null && !string.IsNullOrEmpty(goyoAnimTriggerName))
            {
                goyoAnimator.SetTrigger(goyoAnimTriggerName);
            }

            if (screenFadeCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 0.5f));

            // Smooth Zoom-In onto Goyo
            if (goyoWideStartCamTarget != null && goyoCloseUpEndCamTarget != null)
            {
                float elapsed = 0f;
                while (elapsed < goyoZoomInDuration && !isSkipping)
                {
                    elapsed += Time.deltaTime;
                    float smoothT = Mathf.SmoothStep(0f, 1f, elapsed / goyoZoomInDuration);
                    mainCamera.transform.position = Vector3.Lerp(goyoWideStartCamTarget.position, goyoCloseUpEndCamTarget.position, smoothT);
                    mainCamera.transform.rotation = Quaternion.Slerp(goyoWideStartCamTarget.rotation, goyoCloseUpEndCamTarget.rotation, smoothT);
                    yield return null;
                }
            }

            // Hold close-up hero shot on Goyo before starting gameplay
            yield return new WaitForSeconds(1.5f);

            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, 0.8f));

            LoadGameplayScene();
        }

        public void SkipCutscene()
        {
            if (isSkipping) return;
            isSkipping = true;

            StopAllCoroutines();
            StartCoroutine(RunSkipSequence());
        }

        private IEnumerator RunSkipSequence()
        {
            if (dialogueCanvasGroup != null) dialogueCanvasGroup.alpha = 0f;

            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, screenFadeCanvasGroup.alpha, 1f, 0.5f));

            LoadGameplayScene();
        }

        private void LoadGameplayScene()
        {
            isCutsceneActive = false;
            if (!string.IsNullOrEmpty(gameplaySceneName))
            {
                SceneManager.LoadScene(gameplaySceneName);
            }
            else
            {
                Debug.LogError("Gameplay scene name is missing in Inspector!");
            }
        }

        private void SnapCamera(Transform target)
        {
            if (mainCamera == null || target == null) return;
            mainCamera.transform.position = target.position;
            mainCamera.transform.rotation = target.rotation;
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

        private IEnumerator FadeCanvasGroup(CanvasGroup cg, float start, float end, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(start, end, elapsed / duration);
                yield return null;
            }
            cg.alpha = end;
        }
    }
}