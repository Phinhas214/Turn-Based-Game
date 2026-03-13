using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Shows Win/Lose panels and a restart hint.
/// Subscribes to GameStateManager in Start (not OnEnable) so Instance is ready.
/// </summary>
public class GameStateUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private List<GameObject> gameplayPanels;


    [Header("Win Panel")]
    [SerializeField] private TextMeshProUGUI winMessageText;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private Button          winRestartButton;

    [Header("Lose Panel")]
    [SerializeField] private TextMeshProUGUI loseMessageText;
    [SerializeField] private GameObject losePanel;
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
        foreach (var panel in gameplayPanels)
            panel.SetActive(true);

        winPanel.SetActive(false);
        losePanel.SetActive(false);
    }

    private void ShowWinScreen()
    {
        foreach (var panel in gameplayPanels)
            panel.SetActive(false);

        winPanel.SetActive(true);
    }

    private void ShowLoseScreen()
    {
        foreach (var panel in gameplayPanels)
            panel.SetActive(false);

        losePanel.SetActive(true);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void OnRestartClicked()
    {
        GameStateManager.Instance?.RestartGame();
    }

    
}