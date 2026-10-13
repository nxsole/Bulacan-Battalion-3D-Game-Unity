using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

namespace TiradPass
{
    [System.Serializable]
    public struct DialogueLine
    {
        public string speakerName;
        public Color speakerColor;
        [TextArea(2, 5)]
        public string dialogueText;
        public Transform cameraTarget;
        public AudioClip voiceClip;
        public float delayAfterLine;

        [Header("Custom Camera Speed (0 = Use Default)")]
        public float customMoveSpeed;
        public float customRotateSpeed;
    }

    public class TiradPassIntroCutscene : MonoBehaviour
    {
        [Header("Camera & UI References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private CanvasGroup mainMenuCanvasGroup;
        [SerializeField] private TextMeshProUGUI speakerNameText;
        [SerializeField] private TextMeshProUGUI dialogueContentText;
        [SerializeField] private CanvasGroup dialogueCanvasGroup;
        [SerializeField] private CanvasGroup screenFadeCanvasGroup;
        [SerializeField] private Button skipButton;

        [Header("Camera Movement Settings")]
        [SerializeField] private float cameraMoveSpeed = 2.0f;
        [SerializeField] private float cameraRotateSpeed = 2.0f;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource mainMenuMusicSource;
        [SerializeField] private AudioSource campfireAudioSource;
        [SerializeField] private AudioSource nightAmbienceAudioSource;
        [SerializeField] private AudioSource dialogueVoiceSource;
        [SerializeField] private float audioFadeDuration = 1.5f;

        [Header("Dialogue Timing Settings")]
        [SerializeField] private float textTypingSpeed = 0.035f;

        [Header("Scene Transition")]
        [SerializeField] private string nextSceneName = "Level1_TrailToTiradPass";
        [SerializeField] private float fadeDuration = 1.5f;

        [Header("Camera Targets")]
        [SerializeField] private Transform mainMenuCamTarget;
        [SerializeField] private Transform goyoCamTarget;
        [SerializeField] private Transform sundalo1CamTarget;
        [SerializeField] private Transform sundalo2CamTarget;
        [SerializeField] private Transform wideShotCamTarget;

        [Header("Dialogue Sequence")]
        [SerializeField] private List<DialogueLine> dialogueSequence = new List<DialogueLine>();

        private Transform currentCameraTarget;
        private float currentMoveSpeed;
        private float currentRotateSpeed;
        private Coroutine typingCoroutine;
        private bool isSkipping = false;
        private bool isCutsceneActive = false;

        private readonly Color colorGoyo = new Color(0.95f, 0.75f, 0.3f);
        private readonly Color colorSundalo1 = new Color(0.85f, 0.85f, 0.85f);
        private readonly Color colorSundalo2 = new Color(0.7f, 0.85f, 0.95f);

        private void Awake()
        {
            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(false);
                skipButton.onClick.AddListener(SkipCutscene);
            }

            if (dialogueCanvasGroup != null)
                dialogueCanvasGroup.alpha = 0f;

            currentCameraTarget = mainMenuCamTarget != null ? mainMenuCamTarget : wideShotCamTarget;
            currentMoveSpeed = cameraMoveSpeed;
            currentRotateSpeed = cameraRotateSpeed;

            if (mainCamera != null && currentCameraTarget != null)
            {
                mainCamera.transform.position = currentCameraTarget.position;
                mainCamera.transform.rotation = currentCameraTarget.rotation;
            }

            if (campfireAudioSource != null) { campfireAudioSource.volume = 0f; campfireAudioSource.loop = true; }
            if (nightAmbienceAudioSource != null) { nightAmbienceAudioSource.volume = 0f; nightAmbienceAudioSource.loop = true; }
        }

        private void Update()
        {
            if (mainCamera != null && currentCameraTarget != null)
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

        [ContextMenu("Auto-Fill Dialogue Data")]
        public void AutoFillDialogueData()
        {
            dialogueSequence = new List<DialogueLine>
            {
                new DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Magpahinga muna kayo. Mahaba pa ang lalakbayin natin ngayong gabi.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "SUNDALO 1", speakerColor = colorSundalo1, dialogueText = "Goyo, sigurado ka bang ligtas ang daan?", cameraTarget = sundalo1CamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Hindi tayo makakasiguro. Pero kailangan nating magpatuloy.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "SUNDALO 2", speakerColor = colorSundalo2, dialogueText = "Naririnig ko pa rin ang mga putok. Hinahabol pa rin tayo ng mga Amerikano.", cameraTarget = sundalo2CamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "SUNDALO 1", speakerColor = colorSundalo1, dialogueText = "At marami na sa atin ang pagod at nanghihina.", cameraTarget = sundalo1CamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Kaya hindi tayo maaaring magtagal dito. Pagkatapos nating magpahinga, tutungo tayo sa trail at susundan ito hanggang dulo.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "SUNDALO 2", speakerColor = colorSundalo2, dialogueText = "At kung makita natin ang mga Amerikano?", cameraTarget = sundalo2CamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Huwag kayong magpapadala sa takot. Harapin natin sila at alisin ang anumang haharang sa atin.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "SUNDALO 1", speakerColor = colorSundalo1, dialogueText = "Kailangan lang nating makarating sa dulo ng trail.", cameraTarget = sundalo1CamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Tama. Manatili kayong magkakasama at huwag mawawala sa grupo.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "SUNDALO 2", speakerColor = colorSundalo2, dialogueText = "Kung ganoon, magpahinga na tayo habang may oras pa.", cameraTarget = sundalo2CamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Oo. Pagkatapos nito, aalis tayo. Maghanda kayo.", cameraTarget = goyoCamTarget, delayAfterLine = 1.0f },
                new DialogueLine { speakerName = "SUNDALO 1", speakerColor = colorSundalo1, dialogueText = "Naiintindihan namin, Goyo.", cameraTarget = sundalo1CamTarget, delayAfterLine = 1.0f },
                // Final line: customMoveSpeed set to 0.3f for a slow, cinematic zoom-out
                new DialogueLine { speakerName = "GOYO", speakerColor = colorGoyo, dialogueText = "Sa dilim man tayo maglakbay, makakarating tayo sa dulo. Walang maiiwan.", cameraTarget = wideShotCamTarget, delayAfterLine = 2.5f, customMoveSpeed = 0.3f, customRotateSpeed = 0.3f }
            };

            Debug.Log("Dialogue Sequence Auto-Filled Successfully!");
        }

        public void PlayCutscene()
        {
            if (isCutsceneActive) return;
            isCutsceneActive = true;
            if (skipButton != null)
                skipButton.gameObject.SetActive(true);
            StartCoroutine(RunIntroCutsceneFlow());
        }

        private IEnumerator RunIntroCutsceneFlow()
        {
            StartCoroutine(FadeOutAudio(mainMenuMusicSource, audioFadeDuration));
            StartCoroutine(FadeInAudio(campfireAudioSource, 1.0f, audioFadeDuration));
            StartCoroutine(FadeInAudio(nightAmbienceAudioSource, 0.8f, audioFadeDuration));

            if (mainMenuCanvasGroup != null)
            {
                mainMenuCanvasGroup.interactable = false;
                mainMenuCanvasGroup.blocksRaycasts = false;
                yield return StartCoroutine(FadeCanvasGroup(mainMenuCanvasGroup, 1.0f, 0.0f, 0.8f));
                mainMenuCanvasGroup.gameObject.SetActive(false);
            }

            if (dialogueCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(dialogueCanvasGroup, 0.0f, 1.0f, 0.8f));

            for (int i = 0; i < dialogueSequence.Count; i++)
            {
                if (isSkipping) yield break;

                DialogueLine line = dialogueSequence[i];

                currentCameraTarget = line.cameraTarget;

                // Apply custom speed if set, otherwise use default
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

            yield return EndCutsceneAndLoadNextScene();
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
            if (!isCutsceneActive || isSkipping) return;
            isSkipping = true;
            StopAllCoroutines();
            if (dialogueVoiceSource != null) dialogueVoiceSource.Stop();
            StartCoroutine(EndCutsceneAndLoadNextScene());
        }

        private IEnumerator EndCutsceneAndLoadNextScene()
        {
            if (skipButton != null)
                skipButton.gameObject.SetActive(false);

            StartCoroutine(FadeOutAudio(campfireAudioSource, fadeDuration));
            StartCoroutine(FadeOutAudio(nightAmbienceAudioSource, fadeDuration));

            if (dialogueCanvasGroup != null)
                StartCoroutine(FadeCanvasGroup(dialogueCanvasGroup, dialogueCanvasGroup.alpha, 0.0f, 0.8f));

            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, screenFadeCanvasGroup.alpha, 1.0f, fadeDuration));

            SceneManager.LoadScene(nextSceneName);
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

        private IEnumerator FadeInAudio(AudioSource audioSource, float targetVolume, float duration)
        {
            if (audioSource == null) yield break;
            if (!audioSource.isPlaying) audioSource.Play();

            float startVolume = audioSource.volume;
            float elapsed = 0.0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
                yield return null;
            }
            audioSource.volume = targetVolume;
        }

        private IEnumerator FadeOutAudio(AudioSource audioSource, float duration)
        {
            if (audioSource == null || !audioSource.isPlaying) yield break;

            float startVolume = audioSource.volume;
            float elapsed = 0.0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
                yield return null;
            }

            audioSource.volume = 0f;
            audioSource.Stop();
        }
    }
}
