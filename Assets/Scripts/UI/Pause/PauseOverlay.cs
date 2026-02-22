using UnityEngine;

public class PauseOverlay : MonoBehaviour
{
    public Canvas pauseCanvas;
    public Canvas mainUICanvas;

    bool isPaused = false;

    void Awake()
    {
        pauseCanvas.enabled = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePause();
        }
    }

    void TogglePause()
    {
        isPaused = !isPaused;

        pauseCanvas.enabled = isPaused;
        mainUICanvas.enabled = !isPaused;

        Time.timeScale = isPaused ? 0f : 1f;
    }
}
