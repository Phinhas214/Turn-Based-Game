using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Networked End Turn button UI.
///
/// DIFFERENCES FROM ORIGINAL TurnSystemUI:
///   - Shows how many players have confirmed end-turn (e.g. "1/3 Ready")
///   - Only shows LOCAL player's UI — other players' UI is their own business
///   - End Turn button submits to MultiplayerTurnSystem instead of TurnSystem
///
/// SETUP:
///   Same panel structure as your existing TurnSystemUI.
///   Wire references in Inspector.
/// </summary>
public class MultiplayerTurnSystemUI : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────
    [Header("Core UI")]
    [SerializeField] private Button          endTurnButton;
    [SerializeField] private TextMeshProUGUI turnNumberText;
    [SerializeField] private TextMeshProUGUI readyCountText;    // e.g. "2 / 4 players ready"

    [Header("Visual States")]
    [SerializeField] private GameObject endTurnFlashOverlay;
    [SerializeField] private GameObject disabledClickFeedback;
    [SerializeField] private GameObject enemyTurnOverlay;      // shown during enemy phase

    [Header("Timings")]
    [SerializeField] private float flashInterval            = 0.3f;
    [SerializeField] private float disabledFeedbackDuration = 0.15f;

    // ── Private ───────────────────────────────────────────────────────────
    private PlayerStats playerStats;
    private Coroutine   flashRoutine;

    // Tracks how many players have ended their turn this round
    // (the server knows the real count; we approximate from a ClientRpc)
    private int playersReady   = 0;
    private int playersTotal   = 0;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;

        if (MultiplayerTurnSystem.Instance != null)
        {
            MultiplayerTurnSystem.Instance.OnTurnChanged      += HandleTurnChanged;
            MultiplayerTurnSystem.Instance.OnEnemyPhaseBegin  += HandleEnemyPhaseBegin;
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin  += HandlePlayerTurnBegin;
        }
    }

    private void OnDisable()
    {
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;

        if (MultiplayerTurnSystem.Instance != null)
        {
            MultiplayerTurnSystem.Instance.OnTurnChanged     -= HandleTurnChanged;
            MultiplayerTurnSystem.Instance.OnEnemyPhaseBegin -= HandleEnemyPhaseBegin;
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin -= HandlePlayerTurnBegin;
        }
    }

    private void Start()
    {
        endTurnButton?.onClick.AddListener(OnEndTurnClicked);

        SetOverlay(endTurnFlashOverlay,   false);
        SetOverlay(disabledClickFeedback, false);
        SetOverlay(enemyTurnOverlay,      false);

        UpdateTurnText();
        UpdateReadyCount(0, 1);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Level ready
    // ─────────────────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        Unit unit = FindFirstObjectByType<Unit>();
        if (unit != null)
            playerStats = unit.GetComponent<PlayerStats>();

        playersTotal = NetworkManager.Singleton?.ConnectedClientsIds.Count ?? 1;
        UpdateReadyCount(0, playersTotal);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (playerStats == null || MultiplayerTurnSystem.Instance == null) return;

        bool isPlayerTurn = MultiplayerTurnSystem.Instance.IsPlayerTurn;
        bool outOfStamina = playerStats.currentStamina == 0;

        if (!isPlayerTurn)
        {
            StopFlash();
            SetOverlay(endTurnFlashOverlay, true);
            return;
        }

        if (!outOfStamina)
        {
            StopFlash();
            SetOverlay(endTurnFlashOverlay, false);
            return;
        }

        // Player turn + no stamina → flash
        if (flashRoutine == null)
            flashRoutine = StartCoroutine(FlashRoutine());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Button
    // ─────────────────────────────────────────────────────────────────────

    private void OnEndTurnClicked()
    {
        if (MultiplayerTurnSystem.Instance == null) return;

        if (!MultiplayerTurnSystem.Instance.IsPlayerTurn)
        {
            TriggerDisabledClickFeedback();
            return;
        }

        MultiplayerTurnSystem.Instance.SubmitEndTurn();

        // Visual feedback: grey out button until enemy phase ends
        endTurnButton.interactable = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────────────

    private void HandleTurnChanged(object sender, EventArgs e)
    {
        UpdateTurnText();
    }

    private void HandleEnemyPhaseBegin()
    {
        StopFlash();
        SetOverlay(enemyTurnOverlay, true);
        endTurnButton.interactable = false;

        playersReady = 0;
        UpdateReadyCount(0, playersTotal);
    }

    private void HandlePlayerTurnBegin()
    {
        SetOverlay(enemyTurnOverlay, false);
        endTurnButton.interactable = true;
        playersTotal = NetworkManager.Singleton?.ConnectedClientsIds.Count ?? 1;
        UpdateReadyCount(0, playersTotal);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public — called by a NetworkBehaviour to sync ready count to all clients
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call this from a ClientRpc in MultiplayerTurnSystem when a player submits end-turn.
    /// </summary>
    public void UpdateReadyCount(int ready, int total)
    {
        playersReady = ready;
        playersTotal = total;

        if (readyCountText != null)
            readyCountText.text = total > 1 ? $"{ready} / {total} ready" : "";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateTurnText()
    {
        if (MultiplayerTurnSystem.Instance != null && turnNumberText != null)
            turnNumberText.text = "TURN " + MultiplayerTurnSystem.Instance.GetTrunNumber();
    }

    private IEnumerator FlashRoutine()
    {
        while (true)
        {
            SetOverlay(endTurnFlashOverlay, true);
            yield return new WaitForSeconds(flashInterval);
            SetOverlay(endTurnFlashOverlay, false);
            yield return new WaitForSeconds(flashInterval);
        }
    }

    private void StopFlash()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        SetOverlay(endTurnFlashOverlay, false);
    }

    private void TriggerDisabledClickFeedback()
    {
        if (!disabledClickFeedback) return;
        StopAllCoroutines();
        StartCoroutine(DisabledClickRoutine());
    }

    private IEnumerator DisabledClickRoutine()
    {
        SetOverlay(disabledClickFeedback, true);
        yield return new WaitForSeconds(disabledFeedbackDuration);
        SetOverlay(disabledClickFeedback, false);
    }

    private void SetOverlay(GameObject obj, bool active)
    {
        if (obj != null) obj.SetActive(active);
    }
}