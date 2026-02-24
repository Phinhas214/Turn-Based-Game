using UnityEngine;

public class PauseOverlay : MonoBehaviour
{
    public Canvas pauseCanvas;
    public Canvas mainUICanvas;
    public Canvas settingsCanvas;

    bool isPaused = false;

    void Awake()
    {
        pauseCanvas.enabled = false;
        settingsCanvas.enabled = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (settingsCanvas.enabled)
            {
                CloseSettings();
            }
            else
            {
                TogglePause();
            }
        }
    }

    void TogglePause()
    {
        isPaused = !isPaused;

        pauseCanvas.enabled = isPaused;
        mainUICanvas.enabled = !isPaused;
        settingsCanvas.enabled = false;

        Time.timeScale = isPaused ? 0f : 1f;
    }

    // -------- UI BUTTON HOOKS --------

    public void Resume()
    {
        if (!isPaused) return;
        TogglePause();
    }

    public void OpenSettings()
    {
        if (!isPaused) return;

        pauseCanvas.enabled = false;
        settingsCanvas.enabled = true;
    }

    public void CloseSettings()
    {
        settingsCanvas.enabled = false;
        pauseCanvas.enabled = true;
    }
}