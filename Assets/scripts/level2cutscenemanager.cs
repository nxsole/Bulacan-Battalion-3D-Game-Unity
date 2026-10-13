using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TiradPass
{
    public class level2cutscenemanager : MonoBehaviour
    {
        [Header("Scene Transition Settings")]
        [Tooltip("Exact name of your Level 2 Ending Cutscene scene in Build Settings")]
        [SerializeField] private string endingCutsceneSceneName = "Level 2 Ending Cutscene";
        [SerializeField] private CanvasGroup screenFadeCanvasGroup;
        [SerializeField] private float fadeOutDuration = 1.0f;

        private bool isLevelCompleted = false;

        // Call this function when all enemies are defeated or the level trigger is reached
        public void OnLevelCompleted()
        {
            if (isLevelCompleted) return;
            isLevelCompleted = true;

            StartCoroutine(TransitionToEndingCutscene());
        }

        private IEnumerator TransitionToEndingCutscene()
        {
            // Optional: Fade screen to black before scene load
            if (screenFadeCanvasGroup != null)
            {
                float elapsed = 0f;
                while (elapsed < fadeOutDuration)
                {
                    elapsed += Time.deltaTime;
                    screenFadeCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeOutDuration);
                    yield return null;
                }
                screenFadeCanvasGroup.alpha = 1f;
            }

            yield return new WaitForSeconds(0.2f);

            // Load the ending cutscene
            if (!string.IsNullOrEmpty(endingCutsceneSceneName))
            {
                SceneManager.LoadScene(endingCutsceneSceneName);
            }
            else
            {
                Debug.LogError("Ending cutscene scene name is empty in the Inspector!");
            }
        }
    }
}