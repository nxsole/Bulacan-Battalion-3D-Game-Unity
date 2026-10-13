using UnityEngine;

namespace TiradPass
{
    public class PersistentBGM : MonoBehaviour
    {
        private static PersistentBGM instance;

        [SerializeField] private AudioSource audioSource;

        private void Awake()
        {
            // Singleton pattern to prevent duplicate music players on scene loads
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject); // Keeps this GameObject alive when changing scenes

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource != null && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        public void StopMusic()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
            }
        }

        public void FadeOutAndDestroy(float fadeDuration)
        {
            StartCoroutine(FadeOutRoutine(fadeDuration));
        }

        private System.Collections.IEnumerator FadeOutRoutine(float fadeDuration)
        {
            if (audioSource != null)
            {
                float startVol = audioSource.volume;
                float elapsed = 0f;

                while (elapsed < fadeDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    audioSource.volume = Mathf.Lerp(startVol, 0f, elapsed / fadeDuration);
                    yield return null;
                }

                audioSource.Stop();
            }

            Destroy(gameObject);
        }
    }
}