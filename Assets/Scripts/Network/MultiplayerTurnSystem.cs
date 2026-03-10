using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Room-aware multiplayer turn system.
///
/// TURN FLOW PER ROOM:
///   - Players sharing a room must ALL submit before their room's turn ends.
///   - Players alone in a room end their turn immediately.
///   - Stamina is restored the moment all players in a room submit.
///   - A "Ready" indicator appears above each player when they submit.
///
/// GLOBAL ENEMY PHASE:
///   - Enemy phase starts once EVERY living player (across all rooms) has submitted.
///   - Enemies run on server, then player turn begins for everyone.
/// </summary>
public class MultiplayerTurnSystem : NetworkBehaviour
{
    public static MultiplayerTurnSystem Instance { get; private set; }

    // ── Network state ─────────────────────────────────────────────────────
    private NetworkVariable<bool> isPlayerTurn = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<int> turnNumber = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Server-only ───────────────────────────────────────────────────────
    private HashSet<ulong> endTurnConfirmations = new HashSet<ulong>();

    // ── Events ────────────────────────────────────────────────────────────
    public event Action       OnPlayerTurnBegin;
    public event Action       OnEnemyPhaseBegin;
    public event Action       OnEnemyPhaseEnd;
    public event EventHandler OnTurnChanged;

    // ── Properties ────────────────────────────────────────────────────────
    public bool IsPlayerTurn => isPlayerTurn.Value;
    public int  TurnNumber   => turnNumber.Value;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        isPlayerTurn.OnValueChanged += OnIsPlayerTurnChanged;
        if (IsServer)
        {
            if (!TrySubscribeToEnemyManager())
                NetworkedLevelGenerator.OnLevelReady += OnLevelReadySubscribeEnemyManager;
        }
    }

    public override void OnNetworkDespawn()
    {
        isPlayerTurn.OnValueChanged -= OnIsPlayerTurnChanged;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReadySubscribeEnemyManager;
        if (NetworkedEnemyManager.Instance != null)
            NetworkedEnemyManager.Instance.OnEnemyTurnsComplete -= HandleEnemyTurnsComplete;
        else if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete -= HandleEnemyTurnsComplete;
    }

    private void OnLevelReadySubscribeEnemyManager()
    {
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReadySubscribeEnemyManager;
        TrySubscribeToEnemyManager();
    }

    private bool TrySubscribeToEnemyManager()
    {
        if (NetworkedEnemyManager.Instance != null)
        {
            NetworkedEnemyManager.Instance.OnEnemyTurnsComplete += HandleEnemyTurnsComplete;
            return true;
        }
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyTurnsComplete += HandleEnemyTurnsComplete;
            return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Submit end turn
    // ─────────────────────────────────────────────────────────────────────

    public void SubmitEndTurn()
    {
        if (!IsPlayerTurn) return;
        SubmitEndTurnServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitEndTurnServerRpc(ulong clientId)
    {
        if (!isPlayerTurn.Value || endTurnConfirmations.Contains(clientId)) return;

        endTurnConfirmations.Add(clientId);
        Debug.Log($"[TurnSystem] Client {clientId} submitted end-turn. {endTurnConfirmations.Count}/{GetLivingPlayerCount()} total.");

        // Show ready indicator above this player on all clients
        SetPlayerReadyIndicatorClientRpc(clientId, true);

        // Restore stamina for this player's room if everyone in it is ready
        TryRestoreStaminaForRoom(clientId);

        // Check if ALL players are done
        CheckAllPlayersReady();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Room-aware stamina restore
    // ─────────────────────────────────────────────────────────────────────

    private void TryRestoreStaminaForRoom(ulong submittingClientId)
    {
        RoomGrid submitterRoom = GetPlayerRoom(submittingClientId);
        List<ulong> roommates  = GetLivingPlayersInRoom(submitterRoom);

        foreach (ulong id in roommates)
            if (!endTurnConfirmations.Contains(id)) return;

        // All roommates done — restore stamina for each
        Debug.Log($"[TurnSystem] Room resolved — restoring stamina for {roommates.Count} player(s).");
        foreach (ulong id in roommates)
            RestoreStaminaClientRpc(id);
    }

    private RoomGrid GetPlayerRoom(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return null;
        return client.PlayerObject?.GetComponent<NetworkedUnit>()?.GetCurrentRoomGrid();
    }

    private List<ulong> GetLivingPlayersInRoom(RoomGrid room)
    {
        var result = new List<ulong>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (!IsClientAlive(client.ClientId)) continue;
            if (GetPlayerRoom(client.ClientId) == room)
                result.Add(client.ClientId);
        }
        return result;
    }

    private bool IsClientAlive(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return false;
        if (client.PlayerObject == null) return true;
        var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
        return health == null || !health.IsDead;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Global phase transition
    // ─────────────────────────────────────────────────────────────────────

    private void CheckAllPlayersReady()
    {
        int living = GetLivingPlayerCount();
        BroadcastReadyCountClientRpc(endTurnConfirmations.Count, living);

        if (endTurnConfirmations.Count < living) return;

        Debug.Log("[TurnSystem] All players confirmed — beginning enemy phase.");
        endTurnConfirmations.Clear();
        isPlayerTurn.Value = false;
        turnNumber.Value++;

        BeginEnemyPhaseClientRpc();
        RunEnemyTurnsOnServer();
    }

    private int GetLivingPlayerCount()
    {
        int count = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            if (IsClientAlive(client.ClientId)) count++;
        return Mathf.Max(1, count);
    }

    private void RunEnemyTurnsOnServer()
    {
        if (NetworkedEnemyManager.Instance != null && NetworkedEnemyManager.Instance.GetEnemyCount() > 0)
            NetworkedEnemyManager.Instance.RunEnemyTurns();
        else if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
            EnemyManager.Instance.RunEnemyTurns();
        else
            HandleEnemyTurnsComplete();
    }

    private void HandleEnemyTurnsComplete()
    {
        isPlayerTurn.Value = true;
        BeginPlayerTurnClientRpc();
    }

    // ─────────────────────────────────────────────────────────────────────
    // ClientRpcs
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void BeginEnemyPhaseClientRpc()
    {
        OnEnemyPhaseBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
    }

    [ClientRpc]
    private void BeginPlayerTurnClientRpc()
    {
        OnEnemyPhaseEnd?.Invoke();
        OnPlayerTurnBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
    }

    [ClientRpc]
    private void BroadcastReadyCountClientRpc(int ready, int total)
    {
        MultiplayerTurnSystemUI.Instance?.UpdateReadyCount(ready, total);
    }

    [ClientRpc]
    private void RestoreStaminaClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        foreach (var unit in FindObjectsByType<NetworkedUnit>(FindObjectsSortMode.None))
        {
            if (!unit.IsOwner) continue;
            var stats = unit.GetComponent<PlayerStats>();
            if (stats != null)
                stats.SetCurrentStaminaPoints(stats.GetMaxStaminaPoints());
            return;
        }
    }

    /// <summary>Shows/hides the Ready indicator above a specific player on ALL clients.</summary>
    [ClientRpc]
    private void SetPlayerReadyIndicatorClientRpc(ulong clientId, bool ready)
    {
        // Find the NetworkObject owned by clientId and set its indicator
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId != clientId) continue;
            if (client.PlayerObject == null) return;
            var indicator = client.PlayerObject.GetComponent<PlayerReadyIndicator>();
            indicator?.SetReady(ready);
            return;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkVariable callback
    // ─────────────────────────────────────────────────────────────────────

    private void OnIsPlayerTurnChanged(bool oldVal, bool newVal)
    {
        if (newVal) OnPlayerTurnBegin?.Invoke();
        else        OnEnemyPhaseBegin?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Force player turn (room transition / reset)
    // ─────────────────────────────────────────────────────────────────────

    public void ForcePlayerTurn()
    {
        if (!IsServer) return;
        endTurnConfirmations.Clear();
        isPlayerTurn.Value = true;
        BeginPlayerTurnClientRpc();
    }

    public void RequestForcePlayerTurn() => ForcePlayerTurnServerRpc();

    [ServerRpc(RequireOwnership = false)]
    private void ForcePlayerTurnServerRpc() => ForcePlayerTurn();

    public int GetTrunNumber() => turnNumber.Value;
}