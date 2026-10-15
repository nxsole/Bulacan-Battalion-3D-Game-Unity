using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    [Header("UI Reference")]
    public GameObject pauseMenuPanel;

    [Header("Player Control Reference")]
    public MonoBehaviour playerController;

    [Header("Audio Settings")]
    [Tooltip("Drag your persistent music AudioSource here")]
    public AudioSource backgroundMusic;

    [Tooltip("Drag your secondary scene music AudioSource here")]
    public AudioSource music;

    [Tooltip("Drag your level music AudioSource here")]
    public AudioSource levelMusic;

    [Header("Scene Settings")]
    public string mainMenuSceneName = "main menu";

    private bool isPaused = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }
    }

    public void PauseGame()
    {
        pauseMenuPanel.SetActive(true);
        Time.timeScale = 0f; // Freeze game speed

        // Pause persistent background music if active
        if (backgroundMusic != null && backgroundMusic.isPlaying)
        {
            backgroundMusic.Pause();
        }

        // Pause secondary scene music if active
        if (music != null && music.isPlaying)
        {
            music.Pause();
        }

        // Pause level music if active
        if (levelMusic != null && levelMusic.isPlaying)
        {
            levelMusic.Pause();
        }

        // Unlock and show cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Disable player look/movement script
        if (playerController != null)
            playerController.enabled = false;

        isPaused = true;
    }

    public void ResumeGame()
    {
        pauseMenuPanel.SetActive(false);
        Time.timeScale = 1f; // Restore normal speed

        // Resume persistent background music
        if (backgroundMusic != null)
        {
            backgroundMusic.UnPause();
        }

        // Resume secondary scene music
        if (music != null)
        {
            music.UnPause();
        }

        // Resume level music
        if (levelMusic != null)
        {
            levelMusic.UnPause();
        }

        // Hide and lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Re-enable player look/movement script
        if (playerController != null)
            playerController.enabled = true;

        isPaused = false;
    }

    public void QuitToMainMenu()
    {
        Time.timeScale = 1f;

        if (backgroundMusic != null)
        {
            backgroundMusic.Stop();
        }

        if (music != null)
        {
            music.Stop();
        }

        if (levelMusic != null)
        {
            levelMusic.Stop();
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }
}