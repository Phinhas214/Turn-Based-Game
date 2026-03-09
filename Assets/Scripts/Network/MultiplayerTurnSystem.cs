using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked turn system with ROOM-AWARE end-turn logic.
///
/// TURN FLOW:
///   1. Player clicks End Turn (or runs out of stamina → auto-submitted by UI).
///
///   2. SERVER checks: are there other living players in the same room?
///      A) SOLO room  → that player's turn ends immediately; stamina restores for them alone.
///      B) SHARED room → wait for ALL players in that room to submit. Once all ready:
///                       → stamina restores for everyone in that room.
///
///   3. Once EVERY living player across ALL rooms has submitted:
///      → Server runs enemy phase (enemies chase/attack).
///      → After enemies are done, server starts the next player turn for everyone.
///
/// STAMINA:
///   - Stamina is restored via RestoreStaminaClientRpc targeted at the player's client.
///   - This fires as soon as the player's room-group has all submitted (not waiting for
///     the full enemy phase), so players in a solo room get stamina back right away
///     while players in a shared room wait for their roommates.
///
/// SETUP:
///   - Attach to MultiplayerManagers GameObject (with NetworkObject).
///   - Old TurnSystem.cs can remain for single-player mode — this only runs in MP.
/// </summary>
public class MultiplayerTurnSystem : NetworkBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────
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

    // ── Server-only state ─────────────────────────────────────────────────
    // Which clients have submitted end-turn this round
    private HashSet<ulong> endTurnConfirmations = new HashSet<ulong>();

    // ── Events — fire on ALL clients ──────────────────────────────────────
    public event Action        OnPlayerTurnBegin;
    public event Action        OnEnemyPhaseBegin;
    public event Action        OnEnemyPhaseEnd;
    public event EventHandler  OnTurnChanged;   // backward-compat

    // ── Properties ────────────────────────────────────────────────────────
    public bool IsPlayerTurn => isPlayerTurn.Value;
    public int  TurnNumber   => turnNumber.Value;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
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
    // Client → Server: Submit end turn
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Called by MultiplayerTurnSystemUI when the player clicks End Turn (or auto-submits).</summary>
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

        if (endTurnConfirmations.Contains(clientId))
        {
            Debug.LogWarning($"[TurnSystem] Client {clientId} already submitted end-turn this round.");
            return;
        }

        endTurnConfirmations.Add(clientId);
        Debug.Log($"[TurnSystem] Client {clientId} confirmed end turn. " +
                  $"{endTurnConfirmations.Count}/{GetLivingPlayerCount()} total ready.");

        // Restore stamina for the room-group this player belongs to,
        // IF all players in that room have now submitted.
        TryRestoreStaminaForRoom(clientId);

        // Check if ALL players across all rooms are done.
        CheckAllPlayersReady();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Room-aware stamina restore
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds all living players in the same room as clientId.
    /// If they have ALL submitted end-turn, restore stamina for each of them.
    /// </summary>
    private void TryRestoreStaminaForRoom(ulong submittingClientId)
    {
        RoomGrid submitterRoom = GetPlayerRoom(submittingClientId);

        // Gather everyone in the same room
        List<ulong> roommates = GetLivingPlayersInRoom(submitterRoom);

        // Check if all roommates have submitted
        foreach (ulong id in roommates)
        {
            if (!endTurnConfirmations.Contains(id))
                return; // someone in this room hasn't submitted yet
        }

        // All roommates are done — restore stamina for each of them now
        Debug.Log($"[TurnSystem] All players in room resolved — restoring stamina for {roommates.Count} player(s).");
        foreach (ulong id in roommates)
        {
            RestoreStaminaClientRpc(id);
        }
    }

    /// <summary>Returns the RoomGrid the given client's player unit is currently in. Null if unknown.</summary>
    private RoomGrid GetPlayerRoom(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return null;

        if (client.PlayerObject == null) return null;

        var unit = client.PlayerObject.GetComponent<NetworkedUnit>();
        return unit?.GetCurrentRoomGrid();
    }

    /// <summary>
    /// Returns all living clients whose player unit is in the given room.
    /// If room is null (unit not yet placed), treats that player as in their own solo room.
    /// </summary>
    private List<ulong> GetLivingPlayersInRoom(RoomGrid room)
    {
        var result = new List<ulong>();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (!IsClientAlive(client.ClientId)) continue;

            RoomGrid theirRoom = GetPlayerRoom(client.ClientId);

            // Same room reference, or both null (unplaced players grouped together)
            if (theirRoom == room)
                result.Add(client.ClientId);
        }

        return result;
    }

    private bool IsClientAlive(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return false;

        if (client.PlayerObject == null) return true; // not spawned yet — count as alive

        var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
        return health == null || !health.IsDead;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Global phase transition
    // ─────────────────────────────────────────────────────────────────────

    private void CheckAllPlayersReady()
    {
        int living = GetLivingPlayerCount();

        // Broadcast ready count for UI
        BroadcastReadyCountClientRpc(endTurnConfirmations.Count, living);

        if (endTurnConfirmations.Count < living)
            return;

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
        {
            if (IsClientAlive(client.ClientId))
                count++;
        }
        return Mathf.Max(1, count);
    }

    private void RunEnemyTurnsOnServer()
    {
        if (NetworkedEnemyManager.Instance != null && NetworkedEnemyManager.Instance.GetEnemyCount() > 0)
            NetworkedEnemyManager.Instance.RunEnemyTurns();
        else if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
            EnemyManager.Instance.RunEnemyTurns();
        else
            HandleEnemyTurnsComplete(); // no enemies — skip straight back to player turn
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

    [ClientRpc]
    private void BroadcastReadyCountClientRpc(int ready, int total)
    {
        MultiplayerTurnSystemUI.Instance?.UpdateReadyCount(ready, total);
    }

    /// <summary>
    /// Restores stamina for ONE specific client. Only that client executes the restore.
    /// Sent immediately when that player's room-group all finish, before enemy phase.
    /// </summary>
    [ClientRpc]
    private void RestoreStaminaClientRpc(ulong targetClientId)
    {
        // Only the targeted client should restore their own stamina
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        // Find this client's local unit
        foreach (var unit in FindObjectsByType<NetworkedUnit>(FindObjectsSortMode.None))
        {
            if (!unit.IsOwner) continue;

            var stats = unit.GetComponent<PlayerStats>();
            if (stats != null)
            {
                stats.SetCurrentStaminaPoints(stats.GetMaxStaminaPoints());
                Debug.Log($"[TurnSystem] Stamina restored for client {targetClientId}.");
            }
            return;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkVariable callback
    // ─────────────────────────────────────────────────────────────────────

    private void OnIsPlayerTurnChanged(bool oldVal, bool newVal)
    {
        // Safety net for late-joining clients
        if (newVal) OnPlayerTurnBegin?.Invoke();
        else        OnEnemyPhaseBegin?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Force player turn (room transition)
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

    // ─────────────────────────────────────────────────────────────────────
    // Backward-compat
    // ─────────────────────────────────────────────────────────────────────

    public int GetTrunNumber() => turnNumber.Value;
}