using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked replacement for TurnSystem.
///
/// TURN FLOW:
///   1. All living players click End Turn  (each calls SubmitEndTurnServerRpc)
///   2. Server collects confirmations      (one per connected client)
///   3. Once ALL living players confirmed  → server runs enemy phase
///   4. EnemyManager.RunEnemyTurns()       → enemies move/attack (server only)
///   5. EnemyManager fires OnEnemyTurnsComplete → server calls BeginPlayerTurnClientRpc
///   6. All clients enter player turn      → UI unlocks, stamina refills
///
/// IMPORTANT:
///   - Attach to the same GameObject as NetworkManager (or a persistent manager object)
///   - The old TurnSystem.cs can be REMOVED — this replaces it entirely
///   - UI buttons subscribe to OnPlayerTurnBegin / OnEnemyPhaseBegin just as before
/// </summary>
public class MultiplayerTurnSystem : NetworkBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────
    public static MultiplayerTurnSystem Instance { get; private set; }

    // ── Network state — readable by all clients ───────────────────────────
    private NetworkVariable<bool> isPlayerTurn = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<int> turnNumber = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Server-only state ─────────────────────────────────────────────────
    // Tracks which client IDs have submitted end-turn this round
    private HashSet<ulong> endTurnConfirmations = new HashSet<ulong>();

    // ── Events — subscribe from UI / gameplay systems ─────────────────────
    // These fire on ALL clients (not just server)
    public event Action           OnPlayerTurnBegin;
    public event Action           OnEnemyPhaseBegin;
    public event Action           OnEnemyPhaseEnd;

    // Compatibility shim — old code uses EventHandler
    public event EventHandler     OnTurnChanged;

    // ─────────────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────────────

    public bool IsPlayerTurn => isPlayerTurn.Value;
    public int  TurnNumber   => turnNumber.Value;

    // ── Compatibility with old code that calls TurnSystem.Instance ────────
    // If you have old scripts referencing TurnSystem.Instance, add a shim there
    // or do a find-replace. This class uses MultiplayerTurnSystem.Instance.

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes so all clients get events
        isPlayerTurn.OnValueChanged += OnIsPlayerTurnChanged;

        if (IsServer && EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete += HandleEnemyTurnsComplete;
    }

    public override void OnNetworkDespawn()
    {
        isPlayerTurn.OnValueChanged -= OnIsPlayerTurnChanged;

        if (IsServer && EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete -= HandleEnemyTurnsComplete;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Client → Server: Submit end turn
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the local player's UI when they click End Turn.
    /// Safe to call even if it's not the player's turn — server ignores it.
    /// </summary>
    public void SubmitEndTurn()
    {
        if (!IsPlayerTurn) return;
        SubmitEndTurnServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitEndTurnServerRpc(ulong clientId)
    {
        if (!isPlayerTurn.Value)
        {
            Debug.LogWarning($"[TurnSystem] Client {clientId} sent end-turn during enemy phase — ignored.");
            return;
        }

        endTurnConfirmations.Add(clientId);
        Debug.Log($"[TurnSystem] Client {clientId} confirmed end turn. " +
                  $"{endTurnConfirmations.Count}/{GetExpectedPlayerCount()} ready.");

        CheckAllPlayersReady();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server: Check if all players are ready
    // ─────────────────────────────────────────────────────────────────────

    private void CheckAllPlayersReady()
    {
        int expected = GetExpectedPlayerCount();
        if (endTurnConfirmations.Count < expected)
            return;

        Debug.Log("[TurnSystem] All players confirmed — beginning enemy phase.");

        endTurnConfirmations.Clear();
        isPlayerTurn.Value = false;
        turnNumber.Value++;

        BeginEnemyPhaseClientRpc();
        RunEnemyTurnsOnServer();
    }

    private int GetExpectedPlayerCount()
    {
        // Count connected clients that are alive (have a Unit with living HealthComponent)
        // For simplicity, use connected client count.
        // Advanced: filter by living players only.
        return NetworkManager.Singleton.ConnectedClientsIds.Count;
    }

    private void RunEnemyTurnsOnServer()
    {
        if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
            EnemyManager.Instance.RunEnemyTurns();
        else
            HandleEnemyTurnsComplete();
    }

    private void HandleEnemyTurnsComplete()
    {
        // Called on server only
        isPlayerTurn.Value = true;
        BeginPlayerTurnClientRpc();
    }

    // ─────────────────────────────────────────────────────────────────────
    // ClientRpcs — server → all clients
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void BeginEnemyPhaseClientRpc()
    {
        Debug.Log("[TurnSystem] Enemy phase begins.");
        OnEnemyPhaseBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
    }

    [ClientRpc]
    private void BeginPlayerTurnClientRpc()
    {
        Debug.Log($"[TurnSystem] Player turn {turnNumber.Value} begins.");
        OnEnemyPhaseEnd?.Invoke();
        OnPlayerTurnBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkVariable callback
    // ─────────────────────────────────────────────────────────────────────

    private void OnIsPlayerTurnChanged(bool oldVal, bool newVal)
    {
        // NetworkVariable fires on all clients when server changes the value.
        // The ClientRpcs above handle the event firing for turn transitions —
        // this callback is a safety net for late-joining clients.
        if (newVal)
            OnPlayerTurnBegin?.Invoke();
        else
            OnEnemyPhaseBegin?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // ForcePlayerTurn — called on room transition (server only sets the var)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Forces the turn back to player turn. Server only.</summary>
    public void ForcePlayerTurn()
    {
        if (!IsServer) return;

        endTurnConfirmations.Clear();
        isPlayerTurn.Value = true;
        BeginPlayerTurnClientRpc();
    }

    /// <summary>
    /// Called from PlayerStats when entering a new room.
    /// Sends a ServerRpc so the server can call ForcePlayerTurn.
    /// </summary>
    public void RequestForcePlayerTurn()
    {
        ForcePlayerTurnServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ForcePlayerTurnServerRpc()
    {
        ForcePlayerTurn();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Backward-compat getter
    // ─────────────────────────────────────────────────────────────────────

    public int GetTrunNumber() => turnNumber.Value; // keep original typo for compatibility
}