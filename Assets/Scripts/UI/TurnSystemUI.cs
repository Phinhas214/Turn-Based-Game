using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
public class TurnSystemUI : MonoBehaviour
{
    // ─────────────────────────────────────────
    // References
    // ─────────────────────────────────────────

    [Header("Core UI")]
    [SerializeField] private Button endTurnButton;
    [SerializeField] private TextMeshProUGUI turnNumberText;

    [Header("Visual States")]
    [Tooltip("Overlay used for flashing (out of stamina) AND solid display (enemy turn).")]
    [SerializeField] private GameObject endTurnFlashOverlay;

    [Tooltip("Shown briefly when clicking End Turn during enemy turn.")]
    [SerializeField] private GameObject disabledClickFeedback;

    [Header("Timings")]
    [SerializeField] private float flashInterval = 0.3f;
    [SerializeField] private float disabledFeedbackDuration = 0.15f;

    // ─────────────────────────────────────────
    // Runtime
    // ─────────────────────────────────────────

    private PlayerStats playerStats;
    private Coroutine flashRoutine;

    // ─────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += HandleTurnChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= HandleTurnChanged;
    }

    private void Start()
    {
        // Button click handling
        endTurnButton.onClick.AddListener(OnEndTurnClicked);

        // Initial visual safety
        if (endTurnFlashOverlay)
            endTurnFlashOverlay.SetActive(false);

        if (disabledClickFeedback)
            disabledClickFeedback.SetActive(false);

        UpdateTurnText();
    }

    // ─────────────────────────────────────────
    // Player discovery
    // ─────────────────────────────────────────

    private void OnLevelReady()
    {
        Unit unit = FindFirstObjectByType<Unit>();
        if (unit == null)
        {
            Debug.LogWarning("[TurnSystemUI] No Unit found on level ready.");
            return;
        }

        playerStats = unit.GetComponent<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogWarning("[TurnSystemUI] Unit has no PlayerStats.");
            return;
        }

        Debug.Log("[TurnSystemUI] PlayerStats acquired.");
    }

    // ─────────────────────────────────────────
    // Update loop (state-driven)
    // ─────────────────────────────────────────

    private void Update()
    {
        if (playerStats == null || TurnSystem.Instance == null)
            return;

        bool isPlayerTurn = TurnSystem.Instance.IsPlayerTurn;
        bool outOfStamina = playerStats.currentStamina == 0;

        // ── ENEMY TURN ─────────────────────────
        if (!isPlayerTurn)
        {
            StopFlash();

            // Hold overlay ON (solid state)
            if (endTurnFlashOverlay)
                endTurnFlashOverlay.SetActive(true);

            return;
        }

        // ── PLAYER TURN ────────────────────────

        // Player has stamina → normal state
        if (!outOfStamina)
        {
            StopFlash();
            if (endTurnFlashOverlay)
                endTurnFlashOverlay.SetActive(false);

            return;
        }

        // Player turn + no stamina → flashing
        if (flashRoutine == null)
            flashRoutine = StartCoroutine(FlashRoutine());
    }

    // ─────────────────────────────────────────
    // Button logic
    // ─────────────────────────────────────────

    private void OnEndTurnClicked()
    {
        if (TurnSystem.Instance == null)
            return;

        // Enemy turn → deny + feedback
        if (!TurnSystem.Instance.IsPlayerTurn)
        {
            TriggerDisabledClickFeedback();
            return;
        }

        // Player turn → end turn always allowed
        TurnSystem.Instance.NextTurn();
    }

    // ─────────────────────────────────────────
    // Flash logic
    // ─────────────────────────────────────────

    private IEnumerator FlashRoutine()
    {
        while (true)
        {
            endTurnFlashOverlay.SetActive(true);
            yield return new WaitForSeconds(flashInterval);

            endTurnFlashOverlay.SetActive(false);
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

    // ─────────────────────────────────────────
    // Disabled click feedback
    // ─────────────────────────────────────────

    private void TriggerDisabledClickFeedback()
    {
        if (!disabledClickFeedback)
            return;

        StopAllCoroutines();
        StartCoroutine(DisabledClickRoutine());
    }

    private IEnumerator DisabledClickRoutine()
    {
        disabledClickFeedback.SetActive(true);
        yield return new WaitForSeconds(disabledFeedbackDuration);
        disabledClickFeedback.SetActive(false);
    }

    // ─────────────────────────────────────────
    // Turn text
    // ─────────────────────────────────────────

    private void HandleTurnChanged(object sender, EventArgs e)
    {
        UpdateTurnText();
    }

    private void UpdateTurnText()
    {
        if (TurnSystem.Instance != null && turnNumberText != null)
            turnNumberText.text = "TURN " + TurnSystem.Instance.GetTrunNumber();
    }
}