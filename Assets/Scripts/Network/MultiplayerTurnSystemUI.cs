using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Multiplayer End Turn UI — modelled directly on TurnSystemUI (single-player).
///
/// FIXES vs previous version:
///   - Subscriptions to MultiplayerTurnSystem are deferred to OnLevelReady,
///     which fires AFTER the NetworkBehaviour has spawned. OnEnable was too early.
///   - Update loop mirrors TurnSystemUI exactly:
///       enemy turn  → overlay ON solid (locked)
///       player turn + stamina → overlay OFF
///       player turn + no stamina → flash
///   - Button click is simple: if player turn → SubmitEndTurn(). No auto-submit
///     coroutine trickery — the flash is just a visual hint, player still clicks.
///   - hasSubmittedThisTurn prevents double-submit, resets on new player turn.
/// </summary>
public class MultiplayerTurnSystemUI : MonoBehaviour
{
    public static MultiplayerTurnSystemUI Instance { get; private set; }

    [Header("Core UI")]
    [SerializeField] private Button          endTurnButton;
    [SerializeField] private TextMeshProUGUI turnNumberText;
    [SerializeField] private TextMeshProUGUI readyCountText;

    [Header("Visual States")]
    [Tooltip("Same overlay used in TurnSystemUI — flashes when out of stamina, solid during enemy turn.")]
    [SerializeField] private GameObject endTurnFlashOverlay;
    [Tooltip("Shown briefly when clicking End Turn during enemy turn.")]
    [SerializeField] private GameObject disabledClickFeedback;

    [Header("Timings")]
    [SerializeField] private float flashInterval            = 0.3f;
    [SerializeField] private float disabledFeedbackDuration = 0.15f;

    private PlayerStats localPlayerStats;
    private bool        hasSubmittedThisTurn = false;
    private Coroutine   flashRoutine;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake() => Instance = this;

    private void Start()
    {
        endTurnButton?.onClick.AddListener(OnEndTurnClicked);
        SetOverlay(endTurnFlashOverlay,   false);
        SetOverlay(disabledClickFeedback, false);
        UpdateTurnText();
    }

    private void OnEnable()
    {
        // Subscribe to level ready so we can find our unit + subscribe to turn system
        LevelGenerator.OnLevelReady          += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady          -= OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
        UnsubscribeTurnSystem();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Level ready — NOW safe to subscribe to turn system and find unit
    // ─────────────────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        SubscribeTurnSystem();
        StartCoroutine(FindLocalPlayerStats());
    }

    private void SubscribeTurnSystem()
    {
        // Always try MP first, then SP fallback
        if (MultiplayerTurnSystem.Instance != null)
        {
            MultiplayerTurnSystem.Instance.OnTurnChanged     += HandleTurnChanged;
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin += HandlePlayerTurnBegin;
            MultiplayerTurnSystem.Instance.OnEnemyPhaseBegin += HandleEnemyPhaseBegin;
        }
        else if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged += HandleTurnChanged;
        }
    }

    private void UnsubscribeTurnSystem()
    {
        if (MultiplayerTurnSystem.Instance != null)
        {
            MultiplayerTurnSystem.Instance.OnTurnChanged     -= HandleTurnChanged;
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin -= HandlePlayerTurnBegin;
            MultiplayerTurnSystem.Instance.OnEnemyPhaseBegin -= HandleEnemyPhaseBegin;
        }
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= HandleTurnChanged;
    }

    private IEnumerator FindLocalPlayerStats()
    {
        float elapsed = 0f;
        while (elapsed < 10f)
        {
            foreach (var unit in FindObjectsByType<NetworkedUnit>(FindObjectsSortMode.None))
            {
                if (!unit.IsOwner) continue;
                localPlayerStats = unit.GetComponent<PlayerStats>();
                yield break;
            }
            // SP fallback
            var spUnit = FindFirstObjectByType<Unit>();
            if (spUnit != null && spUnit.GetComponent<Unity.Netcode.NetworkObject>() == null)
            {
                localPlayerStats = spUnit.GetComponent<PlayerStats>();
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Update — mirrors TurnSystemUI exactly
    // ─────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (localPlayerStats == null) return;

        bool isPlayerTurn  = IsPlayerTurnNow();
        bool outOfStamina  = localPlayerStats.currentStamina == 0;
        bool hasSubmitted  = hasSubmittedThisTurn;

        // Enemy turn → overlay solid ON, stop any flash
        if (!isPlayerTurn)
        {
            StopFlash();
            SetOverlay(endTurnFlashOverlay, true);
            return;
        }

        // Already submitted this turn → keep button greyed, no flash
        if (hasSubmitted)
        {
            StopFlash();
            SetOverlay(endTurnFlashOverlay, false);
            return;
        }

        // Player turn + has stamina → normal
        if (!outOfStamina)
        {
            StopFlash();
            SetOverlay(endTurnFlashOverlay, false);
            return;
        }

        // Player turn + no stamina → flash to prompt end turn (same as SP)
        if (flashRoutine == null)
            flashRoutine = StartCoroutine(FlashRoutine());
    }

    private bool IsPlayerTurnNow()
    {
        if (MultiplayerTurnSystem.Instance != null) return MultiplayerTurnSystem.Instance.IsPlayerTurn;
        if (TurnSystem.Instance != null)            return TurnSystem.Instance.IsPlayerTurn;
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Button — same logic as TurnSystemUI
    // ─────────────────────────────────────────────────────────────────────

    private void OnEndTurnClicked()
    {
        // If turn system not found yet, try subscribing now (handles late spawn)
        if (MultiplayerTurnSystem.Instance == null && TurnSystem.Instance == null)
        {
            Debug.LogWarning("[TurnSystemUI] No turn system found — retrying subscription.");
            SubscribeTurnSystem();
        }

        if (!IsPlayerTurnNow())
        {
            TriggerDisabledClickFeedback();
            return;
        }

        if (hasSubmittedThisTurn)
        {
            Debug.Log("[TurnSystemUI] Already submitted this turn.");
            return;
        }

        hasSubmittedThisTurn = true;
        if (endTurnButton != null) endTurnButton.interactable = false;

        if (MultiplayerTurnSystem.Instance != null)
        {
            Debug.Log("[TurnSystemUI] Submitting end turn to MultiplayerTurnSystem.");
            MultiplayerTurnSystem.Instance.SubmitEndTurn();
        }
        else if (TurnSystem.Instance != null)
        {
            Debug.Log("[TurnSystemUI] Calling TurnSystem.NextTurn.");
            TurnSystem.Instance.NextTurn();
        }
        else
        {
            Debug.LogError("[TurnSystemUI] No turn system instance found! Check MultiplayerManagers has MultiplayerTurnSystem and NetworkObject.");
            hasSubmittedThisTurn = false; // reset so player can try again
            if (endTurnButton != null) endTurnButton.interactable = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Turn events
    // ─────────────────────────────────────────────────────────────────────

    private void HandleTurnChanged(object sender, EventArgs e) => UpdateTurnText();

    private void HandlePlayerTurnBegin()
    {
        hasSubmittedThisTurn = false;
        if (endTurnButton != null) endTurnButton.interactable = true;
        UpdateTurnText();
    }

    private void HandleEnemyPhaseBegin()
    {
        StopFlash();
        if (endTurnButton != null) endTurnButton.interactable = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Called from MultiplayerTurnSystem.BroadcastReadyCountClientRpc
    // ─────────────────────────────────────────────────────────────────────

    public void UpdateReadyCount(int ready, int total)
    {
        if (readyCountText != null)
            readyCountText.text = total > 1 ? $"{ready} / {total} ready" : "";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Flash — identical to TurnSystemUI
    // ─────────────────────────────────────────────────────────────────────

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
    }

    private void UpdateTurnText()
    {
        if (turnNumberText == null) return;
        if (MultiplayerTurnSystem.Instance != null)
            turnNumberText.text = "TURN " + MultiplayerTurnSystem.Instance.GetTrunNumber();
        else if (TurnSystem.Instance != null)
            turnNumberText.text = "TURN " + TurnSystem.Instance.GetTrunNumber();
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