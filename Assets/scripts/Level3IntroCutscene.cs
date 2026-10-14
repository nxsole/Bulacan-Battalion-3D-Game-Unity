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
        public AudioClip voiceClip;
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

        [Header("1. American 1 Camera Targets")]
        [Tooltip("Wide/Medium target where American 1 shot starts")]
        [SerializeField] private Transform american1StartCamTarget;
        [Tooltip("Close-up target where camera zooms in while American 1 speaks")]
        [SerializeField] private Transform american1EndCamTarget;
        [SerializeField] private float american1ZoomDuration = 4.0f;

        [Header("2. American 2 Camera Target")]
        [Tooltip("Single target for American 2 (Camera smoothly pans here from American 1)")]
        [SerializeField] private Transform american2CamTarget;
        [SerializeField] private float american2PanDuration = 3.5f;

        [Header("3. Sky Battlefield Zoom-Out Camera Targets")]
        [SerializeField] private Transform skyBattlefieldStartCamTarget;
        [SerializeField] private Transform skyBattlefieldEndCamTarget;
        [SerializeField] private float skyZoomOutDuration = 12.0f;

        [Header("4. Goyo Reveal Zoom-In Camera Targets")]
        [SerializeField] private Transform goyoWideStartCamTarget;
        [SerializeField] private Transform goyoCloseUpEndCamTarget;
        [SerializeField] private float goyoZoomInDuration = 5.0f;

        [Header("Goyo Gameobject & Animator")]
        [SerializeField] private Animator goyoAnimator;
        [SerializeField] private string goyoAnimTriggerName = "TriggerReveal";

        [Header("Audio Settings")]
        [SerializeField] private AudioSource dialogueVoiceSource;
        [SerializeField] private AudioSource bgmSource; // <--- BGM Audio Source
        [SerializeField] private AudioClip backgroundMusic; // <--- BGM Clip
        [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.5f; // <--- BGM Volume Slider
        [SerializeField] private float bgmFadeOutDuration = 1.5f;

        [Header("Dialogue Timing")]
        [SerializeField] private float textTypingSpeed = 0.035f;

        [Header("Dialogue Sequence Data")]
        [SerializeField] private List<Level3DialogueLine> dialogueSequence = new List<Level3DialogueLine>();

        private Coroutine typingCoroutine;
        private Coroutine bgmFadeCoroutine;
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

            // Start playing BGM immediately when cutscene starts
            PlayBackgroundMusic();

            StartCoroutine(RunLevel3IntroFlow());
        }

        private IEnumerator RunLevel3IntroFlow()
        {
            // =========================================================================
            // PART 1: AMERICAN DIALOGUE SEQUENCE WITH AUDIO & SMOOTH PANNING
            // =========================================================================

            // --- STEP 1: American 1 Dialogue & Smooth Zoom-In ---
            SnapCamera(american1StartCamTarget);

            if (screenFadeCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 1.0f));

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

                PlayDialogueVoice(line1.voiceClip);

                if (typingCoroutine != null) StopCoroutine(typingCoroutine);
                typingCoroutine = StartCoroutine(TypeWriterText(line1.dialogueText));

                if (american1StartCamTarget != null && american1EndCamTarget != null)
                {
                    yield return StartCoroutine(LerpCamera(american1StartCamTarget, american1EndCamTarget, american1ZoomDuration));
                }

                if (dialogueVoiceSource != null && dialogueVoiceSource.isPlaying)
                {
                    yield return new WaitWhile(() => dialogueVoiceSource.isPlaying && !isSkipping);
                }

                yield return new WaitForSeconds(line1.delayAfterLine);
            }

            // --- STEP 2: American 2 Dialogue & Smooth Pan to American 2 ---
            if (dialogueSequence.Count > 1)
            {
                Level3DialogueLine line2 = dialogueSequence[1];

                if (speakerNameText != null)
                {
                    speakerNameText.text = line2.speakerName;
                    speakerNameText.color = line2.speakerColor;
                }

                PlayDialogueVoice(line2.voiceClip);

                if (typingCoroutine != null) StopCoroutine(typingCoroutine);
                typingCoroutine = StartCoroutine(TypeWriterText(line2.dialogueText));

                if (american1EndCamTarget != null && american2CamTarget != null)
                {
                    yield return StartCoroutine(LerpCamera(american1EndCamTarget, american2CamTarget, american2PanDuration));
                }
                else if (american2CamTarget != null)
                {
                    SnapCamera(american2CamTarget);
                }

                if (dialogueVoiceSource != null && dialogueVoiceSource.isPlaying)
                {
                    yield return new WaitWhile(() => dialogueVoiceSource.isPlaying && !isSkipping);
                }

                yield return new WaitForSeconds(line2.delayAfterLine);
            }

            // Hide Dialogue UI smoothly
            if (dialogueCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(dialogueCanvasGroup, 1f, 0f, 0.4f));


            // =========================================================================
            // PART 2: ULTRA-SMOOTH PARALLEL SKY BATTLEFIELD ZOOM & GOYO REVEAL
            // =========================================================================

            // --- STEP 3: Sky Battlefield Flyover ---
            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, 0.6f));

            if (skyBattlefieldStartCamTarget != null) SnapCamera(skyBattlefieldStartCamTarget);

            if (screenFadeCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 1.5f));

            if (skyBattlefieldStartCamTarget != null && skyBattlefieldEndCamTarget != null)
            {
                float skyFadeOutTime = Mathf.Max(0.5f, skyZoomOutDuration - 0.8f);
                StartCoroutine(TriggerLateFadeOut(skyFadeOutTime, 0.8f));

                yield return StartCoroutine(LerpCamera(skyBattlefieldStartCamTarget, skyBattlefieldEndCamTarget, skyZoomOutDuration));
            }


            // --- STEP 4: Goyo Zoom-In Reveal ---
            if (goyoWideStartCamTarget != null) SnapCamera(goyoWideStartCamTarget);

            if (goyoAnimator != null && !string.IsNullOrEmpty(goyoAnimTriggerName))
            {
                goyoAnimator.SetTrigger(goyoAnimTriggerName);
            }

            if (screenFadeCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 1.2f));

            if (goyoWideStartCamTarget != null && goyoCloseUpEndCamTarget != null)
            {
                yield return StartCoroutine(LerpCamera(goyoWideStartCamTarget, goyoCloseUpEndCamTarget, goyoZoomInDuration));
            }

            // Hold close-up hero shot on Goyo
            yield return new WaitForSeconds(1.5f);

            // Start fading out BGM and screen before scene transition
            FadeOutBGM(bgmFadeOutDuration);

            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, bgmFadeOutDuration));

            LoadGameplayScene();
        }

        private IEnumerator TriggerLateFadeOut(float delay, float fadeDuration)
        {
            yield return new WaitForSeconds(delay);
            if (screenFadeCanvasGroup != null && !isSkipping)
            {
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, screenFadeCanvasGroup.alpha, 1f, fadeDuration));
            }
        }

        public void SkipCutscene()
        {
            if (isSkipping) return;
            isSkipping = true;

            if (dialogueVoiceSource != null && dialogueVoiceSource.isPlaying)
            {
                dialogueVoiceSource.Stop();
            }

            FadeOutBGM(0.5f);

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

        private void PlayBackgroundMusic()
        {
            if (bgmSource == null || backgroundMusic == null) return;
            bgmSource.clip = backgroundMusic;
            bgmSource.loop = true;
            bgmSource.volume = bgmVolume;
            bgmSource.Play();
        }

        private void FadeOutBGM(float duration)
        {
            if (bgmSource == null || !bgmSource.isPlaying) return;
            if (bgmFadeCoroutine != null) StopCoroutine(bgmFadeCoroutine);
            bgmFadeCoroutine = StartCoroutine(FadeOutAudioSource(bgmSource, duration));
        }

        private IEnumerator FadeOutAudioSource(AudioSource source, float duration)
        {
            float startVol = source.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                source.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
                yield return null;
            }

            source.volume = 0f;
            source.Stop();
        }

        private void PlayDialogueVoice(AudioClip clip)
        {
            if (dialogueVoiceSource == null || clip == null) return;
            dialogueVoiceSource.Stop();
            dialogueVoiceSource.clip = clip;
            dialogueVoiceSource.Play();
        }

        private void SnapCamera(Transform target)
        {
            if (mainCamera == null || target == null) return;
            mainCamera.transform.position = target.position;
            mainCamera.transform.rotation = target.rotation;
        }

        private IEnumerator LerpCamera(Transform startTarget, Transform endTarget, float duration)
        {
            if (mainCamera == null || startTarget == null || endTarget == null) yield break;

            float elapsed = 0f;
            Vector3 startPos = startTarget.position;
            Quaternion startRot = startTarget.rotation;
            Vector3 endPos = endTarget.position;
            Quaternion endRot = endTarget.rotation;

            while (elapsed < duration && !isSkipping)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                mainCamera.transform.position = Vector3.Lerp(startPos, endPos, smoothT);
                mainCamera.transform.rotation = Quaternion.Slerp(startRot, endRot, smoothT);

                yield return null;
            }
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