using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class TurnSystemUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Button endTurnBtn;
    [SerializeField] private TextMeshProUGUI turnNumberText;

    [Header("End Turn Flash")]
    [SerializeField] private GameObject endTurnFlashOverlay;
    [SerializeField] private float flashInterval = 0.3f;

    private PlayerStats playerStats;
    private Coroutine flashRoutine;

    // ─────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────

    void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
    }

    void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= TurnSystem_OnTurnChanged;
    }

    private void Start()
    {
        // Button click
        endTurnBtn.onClick.AddListener(() =>
        {
            TurnSystem.Instance.NextTurn();
        });

        // Turn text
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;
            UpdateTurnText();
        }

        // Safety: make sure flash starts off
        if (endTurnFlashOverlay)
            endTurnFlashOverlay.SetActive(false);
    }

    // ─────────────────────────────────────────
    // Player discovery (CRITICAL)
    // ─────────────────────────────────────────

    private void OnLevelReady()
    {
        Unit unit = FindFirstObjectByType<Unit>();
        if (unit != null)
        {
            playerStats = unit.GetComponent<PlayerStats>();
            Debug.Log("[TurnSystemUI] PlayerStats acquired via OnLevelReady.");
        }
        else
        {
            Debug.LogWarning("[TurnSystemUI] No Unit found after level ready!");
        }
    }

    // ─────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────

    void Update()
    {
        if (playerStats == null || TurnSystem.Instance == null)
            return;

        // Debug — this SHOULD print once stamina hits 0
        Debug.Log(
            $"[TurnSystemUI] Stamina: {playerStats.currentStamina}, " +
            $"IsPlayerTurn: {TurnSystem.Instance.IsPlayerTurn}"
        );

        bool shouldFlash =
            TurnSystem.Instance.IsPlayerTurn &&
            playerStats.currentStamina == 0;

        if (shouldFlash && flashRoutine == null)
        {
            flashRoutine = StartCoroutine(FlashEndTurn());
        }
        else if (!shouldFlash && flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
            endTurnFlashOverlay.SetActive(false);
        }
    }

    // ─────────────────────────────────────────
    // Flash logic
    // ─────────────────────────────────────────

    private System.Collections.IEnumerator FlashEndTurn()
    {
        while (true)
        {
            endTurnFlashOverlay.SetActive(true);
            yield return null; // guarantee one rendered frame
            yield return new WaitForSeconds(flashInterval);

            endTurnFlashOverlay.SetActive(false);
            yield return new WaitForSeconds(flashInterval);
        }
    }

    // ─────────────────────────────────────────
    // Turn text
    // ─────────────────────────────────────────

    private void TurnSystem_OnTurnChanged(object sender, EventArgs e)
    {
        UpdateTurnText();
    }

    private void UpdateTurnText()
    {
        if (TurnSystem.Instance != null)
            turnNumberText.text = "TURN " + TurnSystem.Instance.GetTrunNumber();
    }
}