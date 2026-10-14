using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace TiradPass
{
    public class Level3EndingCutscene : MonoBehaviour
    {
        [Header("Camera References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private float cameraMoveSpeed = 0.8f;

        [Header("Camera Targets")]
        [Tooltip("Start shot close to barricade looking over the fallen defenders")]
        [SerializeField] private Transform soldierStartCamTarget;
        [Tooltip("Distant target for the smooth zoom-out movement")]
        [SerializeField] private Transform zoomOutCamTarget;
        [Tooltip("Target looking directly up toward the sky")]
        [SerializeField] private Transform skyTiltCamTarget;

        [Header("UI Canvas Groups & Overlays")]
        [SerializeField] private CanvasGroup screenFadeCanvasGroup;
        [SerializeField] private Image screenFadeImage;
        [SerializeField] private CanvasGroup historicalTextCanvasGroup;
        [SerializeField] private CanvasGroup levelCompleteCanvasGroup;
        [SerializeField] private Button skipButton;

        [Header("UI Text Fields")]
        [SerializeField] private TextMeshProUGUI dateText;
        [SerializeField] private TextMeshProUGUI summaryText;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource battleAmbienceSource;
        [SerializeField] private AudioSource windAmbienceSource;

        [Header("Scene Transition Settings")]
        [SerializeField] private string mainMenuSceneName = "main menu";
        [Tooltip("Time to hold on Level Complete UI before transition")]
        [SerializeField] private float levelCompleteHoldDuration = 3.0f;
        [Tooltip("Duration of final audio fade-out")]
        [SerializeField] private float finalAudioFadeDuration = 1.5f;

        private Transform currentCameraTarget;
        private bool isCutsceneActive = false;
        private bool isSkipping = false;

        private void Awake()
        {
            // Unlock mouse cursor for UI interaction
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (skipButton != null)
            {
                skipButton.onClick.RemoveAllListeners();
                skipButton.onClick.AddListener(SkipEndingCutscene);
            }

            if (historicalTextCanvasGroup != null) historicalTextCanvasGroup.alpha = 0f;
            if (levelCompleteCanvasGroup != null) levelCompleteCanvasGroup.alpha = 0f;

            // Set fade overlay color strictly to Black
            if (screenFadeImage != null) screenFadeImage.color = Color.black;

            // Start full black at Awake for initial fade-in
            if (screenFadeCanvasGroup != null) screenFadeCanvasGroup.alpha = 1f;
        }

        private void Start()
        {
            PlayEndingCutscene();
        }

        private void Update()
        {
            if (isSkipping) return;

            // Unlock cursor if captured during sequence
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // Keyboard shortcut support: Space, Escape, or Enter to skip
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
            {
                SkipEndingCutscene();
            }

            // Smooth continuous interpolation between camera targets
            if (isCutsceneActive && mainCamera != null && currentCameraTarget != null)
            {
                mainCamera.transform.position = Vector3.Lerp(
                    mainCamera.transform.position,
                    currentCameraTarget.position,
                    Time.deltaTime * cameraMoveSpeed
                );

                mainCamera.transform.rotation = Quaternion.Slerp(
                    mainCamera.transform.rotation,
                    currentCameraTarget.rotation,
                    Time.deltaTime * cameraMoveSpeed
                );
            }
        }

        public void PlayEndingCutscene()
        {
            if (isCutsceneActive) return;
            isCutsceneActive = true;
            StartCoroutine(RunStaticDefenderEndingSequence());
        }

        private IEnumerator RunStaticDefenderEndingSequence()
        {
            // Snap camera to initial close shot over the barricade
            SnapCamera(soldierStartCamTarget);

            // --- 1. ZOOM OUT & FADE IN SIMULTANEOUSLY (~3.5 seconds) ---
            if (zoomOutCamTarget != null) currentCameraTarget = zoomOutCamTarget;

            if (screenFadeCanvasGroup != null)
            {
                StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 2.0f));
            }

            // Hold on the wide zoom-out view of the fallen defenders
            yield return new WaitForSeconds(3.5f);

            // --- 2. SMOOTH TILT UP TO SKY & HOLD (~4.5 seconds) ---
            if (skyTiltCamTarget != null) currentCameraTarget = skyTiltCamTarget;

            yield return new WaitForSeconds(4.5f);

            // --- 3. FADE TO BLACK AFTER SKY TILT FINISHES ---
            if (windAmbienceSource != null && !windAmbienceSource.isPlaying)
                windAmbienceSource.Play();

            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, 2.0f));

            yield return new WaitForSeconds(0.5f);

            // --- 4. HISTORICAL TEXT CARD OVER BLACK OVERLAY ---
           
            if (summaryText != null) summaryText.text = "Despite holding the high ground, General Gregorio del Pilar and his men faced heavily armed American soldiers. The stage was set for a desperate stand at Tirad Pass, where courage would meet impossible odds.";

            if (historicalTextCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(historicalTextCanvasGroup, 0f, 1f, 1.0f));

            yield return new WaitForSeconds(5.0f);

            if (historicalTextCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(historicalTextCanvasGroup, 1f, 0f, 0.8f));

            // --- 5. LEVEL COMPLETE / GAME OVER UI ---
            if (levelCompleteCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(levelCompleteCanvasGroup, 0f, 1f, 0.8f));

            isCutsceneActive = false;

            // --- 6. HOLD LEVEL COMPLETE FOR 3 SECONDS ---
            yield return new WaitForSeconds(levelCompleteHoldDuration);

            // --- 7. FADE AUDIO & PERSISTENT BGM OUT ---
            yield return StartCoroutine(FadeAllAudioOut(finalAudioFadeDuration));

            // --- 8. LOAD MAIN MENU ---
            ReturnToMainMenu();
        }

        public void SkipEndingCutscene()
        {
            if (isSkipping) return;
            isSkipping = true;

            StopAllCoroutines();

            // Fade persistent music out quickly on skip
            PersistentBGM bgm = FindObjectOfType<PersistentBGM>();
            if (bgm != null)
            {
                bgm.FadeOutAndDestroy(0.5f);
            }

            // Stop local ambience sources
            if (battleAmbienceSource != null) battleAmbienceSource.Stop();
            if (windAmbienceSource != null) windAmbienceSource.Stop();

            ReturnToMainMenu();
        }

        public void ReturnToMainMenu()
        {
            // Ensure persistent BGM gets cleaned up if transitioning directly
            PersistentBGM bgm = FindObjectOfType<PersistentBGM>();
            if (bgm != null)
            {
                bgm.FadeOutAndDestroy(finalAudioFadeDuration);
            }

            if (!string.IsNullOrEmpty(mainMenuSceneName))
            {
                SceneManager.LoadScene(mainMenuSceneName);
            }
            else
            {
                Debug.LogError("Main menu scene name is missing in Inspector!");
            }
        }

        private void SnapCamera(Transform target)
        {
            if (mainCamera == null || target == null) return;
            currentCameraTarget = target;
            mainCamera.transform.position = target.position;
            mainCamera.transform.rotation = target.rotation;
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

        private IEnumerator FadeAllAudioOut(float duration)
        {
            // 1. Fade local scene audio sources
            float elapsed = 0.0f;
            float startVolBattle = battleAmbienceSource != null ? battleAmbienceSource.volume : 0f;
            float startVolWind = windAmbienceSource != null ? windAmbienceSource.volume : 0f;

            // 2. Fade persistent background music from gameplay
            PersistentBGM bgm = FindObjectOfType<PersistentBGM>();
            if (bgm != null)
            {
                bgm.FadeOutAndDestroy(duration);
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                if (battleAmbienceSource != null)
                    battleAmbienceSource.volume = Mathf.Lerp(startVolBattle, 0f, t);
                if (windAmbienceSource != null)
                    windAmbienceSource.volume = Mathf.Lerp(startVolWind, 0f, t);

                yield return null;
            }

            if (battleAmbienceSource != null) battleAmbienceSource.Stop();
            if (windAmbienceSource != null) windAmbienceSource.Stop();
        }
    }
}