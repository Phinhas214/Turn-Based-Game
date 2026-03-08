using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Networked End Turn button UI.
///
/// TURN FLOW:
///   - Each player clicks "End Turn" OR runs out of stamina → auto-submits after a brief flash.
///   - Shows "X / Y ready" so players know who's still going.
///   - Locks the button during enemy phase.
///   - When ALL living players have submitted, the server runs enemies then starts the next player turn.
///
/// SETUP:
///   Wire all references in Inspector. Add to your HUD canvas.
///   Works in both SP (TurnSystem) and MP (MultiplayerTurnSystem).
/// </summary>
public class MultiplayerTurnSystemUI : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────
    public static MultiplayerTurnSystemUI Instance { get; private set; }

    // ── References ────────────────────────────────────────────────────────
    [Header("Core UI")]
    [SerializeField] private Button          endTurnButton;
    [SerializeField] private TextMeshProUGUI turnNumberText;
    [SerializeField] private TextMeshProUGUI readyCountText;    // e.g. "2 / 4 ready"

    [Header("Visual States")]
    [SerializeField] private GameObject endTurnFlashOverlay;
    [SerializeField] private GameObject disabledClickFeedback;
    [SerializeField] private GameObject enemyTurnOverlay;       // shown during enemy phase

    [Header("Timings")]
    [SerializeField] private float flashInterval            = 0.3f;
    [SerializeField] private float disabledFeedbackDuration = 0.15f;

    // ── Private ───────────────────────────────────────────────────────────
    private PlayerStats localPlayerStats;
    private bool        hasSubmittedThisTurn = false;
    private Coroutine   flashRoutine;

    private int playersReady = 0;
    private int playersTotal = 1;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
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

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady          += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
        SubscribeToTurnSystem();
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady          -= OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
        UnsubscribeFromTurnSystem();
    }

    private void SubscribeToTurnSystem()
    {
        if (MultiplayerTurnSystem.Instance != null)
        {
            MultiplayerTurnSystem.Instance.OnTurnChanged     += HandleTurnChanged;
            MultiplayerTurnSystem.Instance.OnEnemyPhaseBegin += HandleEnemyPhaseBegin;
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin += HandlePlayerTurnBegin;
        }
        else if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged += HandleTurnChanged;
        }
    }

    private void UnsubscribeFromTurnSystem()
    {
        if (MultiplayerTurnSystem.Instance != null)
        {
            MultiplayerTurnSystem.Instance.OnTurnChanged     -= HandleTurnChanged;
            MultiplayerTurnSystem.Instance.OnEnemyPhaseBegin -= HandleEnemyPhaseBegin;
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin -= HandlePlayerTurnBegin;
        }
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= HandleTurnChanged;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Level ready — find LOCAL player's stats
    // ─────────────────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        // Re-subscribe in case MultiplayerTurnSystem spawned after OnEnable
        SubscribeToTurnSystem();
        StartCoroutine(WaitForLocalPlayerStats());
    }

    private IEnumerator WaitForLocalPlayerStats()
    {
        float timeout = 10f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            Unit localUnit = FindLocalUnit();
            if (localUnit != null)
            {
                localPlayerStats = localUnit.GetComponent<PlayerStats>();
                playersTotal     = NetworkManager.Singleton?.ConnectedClientsIds.Count ?? 1;
                UpdateReadyCount(0, playersTotal);
                UpdateTurnText();
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("[MultiplayerTurnSystemUI] Timed out finding local player stats.");
    }

    private Unit FindLocalUnit()
    {
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = unit.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                if (netObj.IsOwner) return unit;
            }
            else
            {
                return unit; // single-player: no network object
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Update — auto end-turn when stamina hits 0
    // ─────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsPlayerTurnNow())
        {
            StopFlash();
            return;
        }

        if (localPlayerStats == null || hasSubmittedThisTurn) return;

        bool outOfStamina = localPlayerStats.currentStamina <= 0;

        if (outOfStamina)
        {
            if (flashRoutine == null)
                flashRoutine = StartCoroutine(FlashThenAutoSubmit());
        }
        else
        {
            StopFlash();
        }
    }

    private bool IsPlayerTurnNow()
    {
        if (MultiplayerTurnSystem.Instance != null)
            return MultiplayerTurnSystem.Instance.IsPlayerTurn;
        if (TurnSystem.Instance != null)
            return TurnSystem.Instance.IsPlayerTurn;
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Button click
    // ─────────────────────────────────────────────────────────────────────

    private void OnEndTurnClicked()
    {
        if (!IsPlayerTurnNow())
        {
            TriggerDisabledClickFeedback();
            return;
        }
        SubmitEndTurn();
    }

    private void SubmitEndTurn()
    {
        if (hasSubmittedThisTurn) return;
        hasSubmittedThisTurn = true;

        endTurnButton.interactable = false;
        StopFlash();

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.SubmitEndTurn();
        else if (TurnSystem.Instance != null)
            TurnSystem.Instance.NextTurn();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Turn system events
    // ─────────────────────────────────────────────────────────────────────

    private void HandleTurnChanged(object sender, EventArgs e) => UpdateTurnText();

    private void HandleEnemyPhaseBegin()
    {
        StopFlash();
        SetOverlay(enemyTurnOverlay, true);
        endTurnButton.interactable = false;
    }

    private void HandlePlayerTurnBegin()
    {
        hasSubmittedThisTurn = false;
        SetOverlay(enemyTurnOverlay, false);
        endTurnButton.interactable = true;
        playersTotal = NetworkManager.Singleton?.ConnectedClientsIds.Count ?? 1;
        UpdateReadyCount(0, playersTotal);
        UpdateTurnText();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public — called from MultiplayerTurnSystem.BroadcastReadyCountClientRpc
    // ─────────────────────────────────────────────────────────────────────

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
        if (turnNumberText == null) return;
        if (MultiplayerTurnSystem.Instance != null)
            turnNumberText.text = "TURN " + MultiplayerTurnSystem.Instance.GetTrunNumber();
        else if (TurnSystem.Instance != null)
            turnNumberText.text = "TURN " + TurnSystem.Instance.GetTrunNumber();
    }

    /// <summary>
    /// Flashes the end-turn button a few times to warn the player,
    /// then automatically submits end-turn.
    /// </summary>
    private IEnumerator FlashThenAutoSubmit()
    {
        for (int i = 0; i < 3; i++)
        {
            SetOverlay(endTurnFlashOverlay, true);
            yield return new WaitForSeconds(flashInterval);
            SetOverlay(endTurnFlashOverlay, false);
            yield return new WaitForSeconds(flashInterval);
        }

        flashRoutine = null;

        if (IsPlayerTurnNow() && !hasSubmittedThisTurn)
            SubmitEndTurn();
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