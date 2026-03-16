using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles the lose panel UI only.
/// Win/next-level UI is handled by EndRoomUI.
/// </summary>
public class GameStateUI : MonoBehaviour
{
    [Header("Gameplay Panels (hidden on lose)")]
    [SerializeField] private List<GameObject> gameplayPanels;

    [Header("Lose Panel")]
    [SerializeField] private GameObject      losePanel;
    [SerializeField] private TextMeshProUGUI loseMessageText;
    [SerializeField] private Button          loseRestartButton;

    [Header("Messages")]
    [SerializeField] private string loseMessage = "You Died.\nThe dungeon claims another soul.";

    private void Start()
    {
        loseRestartButton?.onClick.AddListener(OnRestartClicked);

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameLost      += ShowLoseScreen;
            GameStateManager.Instance.OnGameRestarted += ShowPlayingUI;
        }
        else
        {
            Debug.LogWarning("[GameStateUI] GameStateManager.Instance is null in Start.");
        }

        ShowPlayingUI();
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance == null) return;
        GameStateManager.Instance.OnGameLost      -= ShowLoseScreen;
        GameStateManager.Instance.OnGameRestarted -= ShowPlayingUI;
    }

    private void ShowPlayingUI()
    {
        foreach (var p in gameplayPanels)
            if (p != null) p.SetActive(true);

        if (losePanel != null) losePanel.SetActive(false);
    }

    private void ShowLoseScreen()
    {
        foreach (var p in gameplayPanels)
            if (p != null) p.SetActive(false);

        if (losePanel != null) losePanel.SetActive(true);
        if (loseMessageText != null) loseMessageText.text = loseMessage;
    }

    private void OnRestartClicked()
    {
        GameStateManager.Instance?.RestartGame(true);
    }
}