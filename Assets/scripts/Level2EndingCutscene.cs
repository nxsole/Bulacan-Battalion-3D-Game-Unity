using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace TiradPass
{
    public class Level2EndingCutscene : MonoBehaviour
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

        [Header("UI Text Fields")]
        [SerializeField] private TextMeshProUGUI dateText;
        [SerializeField] private TextMeshProUGUI summaryText;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource battleAmbienceSource;
        [SerializeField] private AudioSource windAmbienceSource;

        [Header("Scene Transition Settings")]
        [SerializeField] private string mainMenuSceneName = "main menu";

        private Transform currentCameraTarget;
        private bool isCutsceneActive = false;

        private void Awake()
        {
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
                // Fade in from black asynchronously while camera pulls back
                StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 2.0f));
            }

            // Hold on the wide zoom-out view of the fallen defenders
            yield return new WaitForSeconds(3.5f);

            // --- 2. SMOOTH TILT UP TO SKY & HOLD (~4.5 seconds) ---
            if (skyTiltCamTarget != null) currentCameraTarget = skyTiltCamTarget;

            // Wait for sky tilt motion and lingering shot (~8s total camera sequence)
            yield return new WaitForSeconds(4.5f);

            // --- 3. FADE TO BLACK AFTER SKY TILT FINISHES ---
            if (battleAmbienceSource != null) battleAmbienceSource.Stop();
            if (windAmbienceSource != null) windAmbienceSource.Play();

            if (screenFadeCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, 2.0f));

            yield return new WaitForSeconds(0.5f);

            // --- 4. HISTORICAL TEXT CARD OVER BLACK OVERLAY ---
            if (dateText != null) dateText.text = "December 2, 1899";
            if (summaryText != null) summaryText.text = "The rear guard fell, and President Aguinaldo escaped.";

            if (historicalTextCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(historicalTextCanvasGroup, 0f, 1f, 1.0f));

            yield return new WaitForSeconds(5.0f);

            if (historicalTextCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(historicalTextCanvasGroup, 1f, 0f, 0.8f));

            // --- 5. LEVEL COMPLETE / GAME OVER UI ---
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (levelCompleteCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(levelCompleteCanvasGroup, 0f, 1f, 0.8f));

            isCutsceneActive = false;
        }

        public void ReturnToMainMenu()
        {
            if (!string.IsNullOrEmpty(mainMenuSceneName))
            {
                SceneManager.LoadScene(mainMenuSceneName);
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
    }
}