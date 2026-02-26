using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows Win/Lose panels and a restart hint.
/// Subscribes to GameStateManager in Start (not OnEnable) so Instance is ready.
/// </summary>
public class GameStateUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;
    [SerializeField] private GameObject hudPanel;

    [Header("Win Panel")]
    [SerializeField] private TextMeshProUGUI winMessageText;
    [SerializeField] private Button          winRestartButton;

    [Header("Lose Panel")]
    [SerializeField] private TextMeshProUGUI loseMessageText;
    [SerializeField] private Button          loseRestartButton;

    [Header("HUD")]
    [SerializeField] private TextMeshProUGUI restartHintText;

    [Header("Messages")]
    [SerializeField] private string winMessage  = "You Escaped!\nThe dungeon has been cleared.";
    [SerializeField] private string loseMessage = "You Died.\nThe dungeon claims another soul.";
    [SerializeField] private string restartHint = "Press R to restart";

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        // Wire buttons
        winRestartButton?.onClick.AddListener(OnRestartClicked);
        loseRestartButton?.onClick.AddListener(OnRestartClicked);

        // Set hint text
        if (restartHintText != null)
            restartHintText.text = restartHint;

        // Subscribe to GameStateManager — done in Start so Instance is ready
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameWon       += ShowWinScreen;
            GameStateManager.Instance.OnGameLost      += ShowLoseScreen;
            GameStateManager.Instance.OnGameRestarted += ShowPlayingUI;
        }
        else
        {
            Debug.LogWarning("[GameStateUI] GameStateManager.Instance is null in Start — " +
                             "make sure GameStateManager is in the scene and above GameStateUI " +
                             "in Script Execution Order.");
        }

        // Start hidden
        ShowPlayingUI();
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameWon       -= ShowWinScreen;
            GameStateManager.Instance.OnGameLost      -= ShowLoseScreen;
            GameStateManager.Instance.OnGameRestarted -= ShowPlayingUI;
        }
    }

    // ── Screens ────────────────────────────────────────────────────────────

    private void ShowPlayingUI()
    {
        SetPanel(winPanel,  false);
        SetPanel(losePanel, false);
        SetPanel(hudPanel,  true);

        if (restartHintText != null)
            restartHintText.gameObject.SetActive(true);
    }

    private void ShowWinScreen()
    {
        Debug.Log("[GameStateUI] Showing win screen.");
        SetPanel(winPanel,  true);
        SetPanel(losePanel, false);
        SetPanel(hudPanel,  false);

        if (winMessageText != null)
            winMessageText.text = winMessage;

        if (restartHintText != null)
            restartHintText.gameObject.SetActive(false);
    }

    private void ShowLoseScreen()
    {
        Debug.Log("[GameStateUI] Showing lose screen.");
        SetPanel(losePanel, true);
        SetPanel(winPanel,  false);
        SetPanel(hudPanel,  false);

        if (loseMessageText != null)
            loseMessageText.text = loseMessage;

        if (restartHintText != null)
            restartHintText.gameObject.SetActive(false);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void OnRestartClicked()
    {
        GameStateManager.Instance?.RestartGame();
    }

    private void SetPanel(GameObject panel, bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }
}