using System;
using UnityEngine;

public class TurnSystem : MonoBehaviour
{
    public static TurnSystem Instance { get; private set; }

    // Original event — unchanged, all existing listeners still work
    public event EventHandler OnTurnChanged;

    // New events for enemy phase
    public event Action OnPlayerTurnBegin;
    public event Action OnEnemyPhaseBegin;
    public event Action OnEnemyPhaseEnd;

    private int  turnNumber   = 1;
    private bool isPlayerTurn = true; // NEW — tracks whose turn it is

    // True while the player can act. False during the enemy phase.
    public bool IsPlayerTurn => isPlayerTurn;

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("There's more than one TurnSystem! " + transform + " - " + Instance);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Listen for EnemyManager to tell us when all enemy turns are done
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete += HandleEnemyTurnsComplete;
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete -= HandleEnemyTurnsComplete;
    }

    // Original NextTurn — same signature, same OnTurnChanged fire, now also kicks off enemy phase
    public void NextTurn()
    {
        if (!isPlayerTurn) return; // ignore if enemies are still going

        isPlayerTurn = false;
        turnNumber++;

        OnTurnChanged?.Invoke(this, EventArgs.Empty); // same as before — Unit stamina reset fires here

        BeginEnemyPhase();
    }

    // Original method preserved — typo and all
    public int GetTrunNumber()
    {
        return turnNumber;
    }

    // ── Private ───────────────────────────────────────────────────────────

    private void BeginEnemyPhase()
    {
        OnEnemyPhaseBegin?.Invoke();

        if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
        {
            EnemyManager.Instance.RunEnemyTurns();
        }
        else
        {
            // No enemies — immediately hand back to player
            HandleEnemyTurnsComplete();
        }
    }

    private void HandleEnemyTurnsComplete()
    {
        isPlayerTurn = true;

        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty); // fires again so stamina UI refreshes
        OnPlayerTurnBegin?.Invoke();

        Debug.Log($"[TurnSystem] Player turn {turnNumber} begins.");
    }
}