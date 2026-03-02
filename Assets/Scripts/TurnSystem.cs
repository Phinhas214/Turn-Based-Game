using System;
using UnityEngine;

public class TurnSystem : MonoBehaviour
{
    public static TurnSystem Instance { get; private set; }

    // Original event — unchanged
    public event EventHandler OnTurnChanged;

    // Phase events
    public event Action OnPlayerTurnBegin;
    public event Action OnEnemyPhaseBegin;
    public event Action OnEnemyPhaseEnd;

    private int turnNumber = 1;
    private bool isPlayerTurn = true;

    public bool IsPlayerTurn => isPlayerTurn;

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("There's more than one TurnSystem!");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete += HandleEnemyTurnsComplete;
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete -= HandleEnemyTurnsComplete;
    }

    // ─────────────────────────────────────────
    // Normal turn flow
    // ─────────────────────────────────────────

    public void NextTurn()
    {
        if (!isPlayerTurn) return;

        isPlayerTurn = false;
        turnNumber++;

        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        BeginEnemyPhase();
    }

    public int GetTrunNumber()
    {
        return turnNumber;
    }

    private void BeginEnemyPhase()
    {
        Debug.Log("[TurnSystem] Enemy phase begins");

        OnEnemyPhaseBegin?.Invoke();

        if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
            EnemyManager.Instance.RunEnemyTurns();
        else
            HandleEnemyTurnsComplete();
    }

    private void HandleEnemyTurnsComplete()
    {
        isPlayerTurn = true;

        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        OnPlayerTurnBegin?.Invoke();

        Debug.Log($"[TurnSystem] Player turn {turnNumber} begins.");
    }

    // ─────────────────────────────────────────
    // NEW — forced recovery (room entry, cutscenes, etc.)
    // ─────────────────────────────────────────

    public void ForcePlayerTurn()
    {
        isPlayerTurn = true;

        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        OnPlayerTurnBegin?.Invoke();

        Debug.Log("[TurnSystem] Forced player turn.");
    }
}