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
        [SerializeField] private float cameraMoveSpeed = 2.0f;
        [SerializeField] private float cameraRotateSpeed = 2.0f;

        [Header("Camera Targets")]
        [Tooltip("Shot focusing on Goyo and soldiers firing behind barricades")]
        [SerializeField] private Transform barricadeFiringCamTarget;
        [Tooltip("Shot focusing on Goyo getting hit and falling down")]
        [SerializeField] private Transform goyoFallCamTarget;
        [Tooltip("Shot directed up at the sky during hit/fall transition")]
        [SerializeField] private Transform skyTiltCamTarget;
        [Tooltip("Wide static shot looking across the quiet, foggy pass")]
        [SerializeField] private Transform aftermathCamTarget;

        [Header("Actors & Animators")]
        [SerializeField] private Animator goyoAnimator;
        [SerializeField] private List<Animator> soldierAnimators = new List<Animator>();
        [SerializeField] private List<GameObject> soldierObjects = new List<GameObject>();

        [Header("UI Canvas Groups & Overlays")]
        [SerializeField] private CanvasGroup screenFadeCanvasGroup;
        [SerializeField] private Image screenFadeImage; // Assign scenefade Image component
        [SerializeField] private CanvasGroup historicalTextCanvasGroup;
        [SerializeField] private CanvasGroup levelCompleteCanvasGroup;

        [Header("UI Text Fields")]
        [SerializeField] private TextMeshProUGUI dateText;
        [SerializeField] private TextMeshProUGUI summaryText;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource battleAmbienceSource;
        [SerializeField] private AudioSource windAmbienceSource;
        [SerializeField] private AudioSource singleGunshotAudio;

        [Header("Scene Transition Settings")]
        [SerializeField] private string mainMenuSceneName = "main menu";

        private Transform currentCameraTarget;
        private bool isCutsceneActive = false;

        private void Awake()
        {
            // Reset UI states
            if (historicalTextCanvasGroup != null) historicalTextCanvasGroup.alpha = 0f;
            if (levelCompleteCanvasGroup != null) levelCompleteCanvasGroup.alpha = 0f;
            if (screenFadeCanvasGroup != null) screenFadeCanvasGroup.alpha = 0f;

            // Make sure fade image starts white for sky transition setup
            if (screenFadeImage != null) screenFadeImage.color = Color.white;
        }

        private void Start()
        {
            PlayEndingCutscene();
        }

        private void Update()
        {
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
                    Time.deltaTime * cameraRotateSpeed
                );
            }
        }

        public void PlayEndingCutscene()
        {
            if (isCutsceneActive) return;
            isCutsceneActive = true;
            StartCoroutine(RunNewEndingCutsceneSequence());
        }

        private IEnumerator RunNewEndingCutsceneSequence()
        {
            // STEP 1: GOYO AND SOLDIERS FIRING BEHIND BARRICADES
            SnapCamera(barricadeFiringCamTarget);

            if (goyoAnimator != null) goyoAnimator.SetTrigger("Trig_Fire");
            foreach (var solAnim in soldierAnimators)
            {
                if (solAnim != null) solAnim.SetTrigger("Trig_Fire");
            }
            yield return new WaitForSeconds(3.0f);

            // STEP 2: SOLDIERS DIE ONE BY ONE
            for (int i = 0; i < soldierAnimators.Count; i++)
            {
                if (soldierAnimators[i] != null)
                {
                    soldierAnimators[i].SetTrigger("Trig_Die");
                }
                yield return new WaitForSeconds(1.2f); // Stagger deaths
            }

            yield return new WaitForSeconds(1.0f);

            // STEP 3: GOYO GETS HIT AND FALLS DOWN
            SnapCamera(goyoFallCamTarget);
            if (singleGunshotAudio != null) singleGunshotAudio.Play();

            if (goyoAnimator != null) goyoAnimator.SetTrigger("Trig_Fall");
            yield return new WaitForSeconds(1.5f);

            // STEP 4: CAMERA TILTS TOWARD SKY AS SCREEN SMOOTHLY FADES TO SOLID WHITE
            if (skyTiltCamTarget != null)
            {
                // Smoothly transition camera rotation/position up toward sky target
                currentCameraTarget = skyTiltCamTarget;
            }

            if (battleAmbienceSource != null) battleAmbienceSource.Stop();

            if (screenFadeImage != null) screenFadeImage.color = Color.white;
            yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 1f, 1.8f));

            // STEP 5: SCREEN TRANSITIONS FROM WHITE TO STATIC WIDE SHOT OF QUIET FOGGY PASS
            SnapCamera(aftermathCamTarget);

            // Hide standing actors and enable fallen pose models if needed
            foreach (var obj in soldierObjects)
            {
                if (obj != null) obj.SetActive(false);
            }

            if (windAmbienceSource != null) windAmbienceSource.Play();
            yield return new WaitForSeconds(0.5f);

            // Fade from white to reveal quiet pass
            yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 1f, 0f, 2.5f));
            yield return new WaitForSeconds(3.0f);

            // STEP 6: DARK OVERLAY FADES IN & HISTORICAL TEXT TYPES OUT
            if (screenFadeImage != null) screenFadeImage.color = Color.black;
            yield return StartCoroutine(FadeCanvasGroup(screenFadeCanvasGroup, 0f, 0.75f, 1.2f));

            if (dateText != null) dateText.text = "December 2, 1899";
            if (summaryText != null) summaryText.text = "The rear guard fell, and President Aguinaldo escaped.";

            if (historicalTextCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(historicalTextCanvasGroup, 0f, 1f, 1.0f));

            yield return new WaitForSeconds(5.0f);

            if (historicalTextCanvasGroup != null)
                yield return StartCoroutine(FadeCanvasGroup(historicalTextCanvasGroup, 1f, 0f, 0.8f));

            // STEP 7: GAME-OVER / LEVEL COMPLETE UI PANEL FADES IN
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