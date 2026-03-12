using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-room independent turn system.
///
/// DESIGN:
///   Each room runs its own combat loop independently. Players in Room A and
///   players in Room B never block each other — they submit end turn, run enemies,
///   and restore stamina completely separately.
///
/// TURN FLOW PER ROOM:
///   1. All living players in the room submit End Turn.
///   2. Server locks input for players in that room (enemy phase begins).
///   3. Enemies in that room take their turns (server only).
///   4. Enemy phase ends — stamina restored, input unlocked for that room only.
///   5. Players can act again immediately. Other rooms are unaffected throughout.
///
/// COMBAT LOCK:
///   - Players cannot leave a room that has enemies (RoomNavigationUI checks this).
///   - While the enemy phase is running for your room, your input is locked.
///     This is done via a per-client NetworkVariable<bool> (isInEnemyPhase[clientId]).
///     Because NGO doesn't support per-client NetworkVariables natively, we use
///     a targeted ClientRpc that only fires to players in the relevant room.
///
/// READY COUNT UI:
///   - MultiplayerTurnSystemUI.UpdateReadyCount(ready, total) receives counts for
///     YOUR room only, not the entire game.
///
/// NOTE: The global isPlayerTurn NetworkVariable is kept for compatibility with
///   MultiplayerTurnSystemUI but is always true. Per-room enemy phase state is
///   tracked in the local bool 'localIsInEnemyPhase' set by targeted ClientRpcs.
/// </summary>
public class MultiplayerTurnSystem : NetworkBehaviour
{
    public static MultiplayerTurnSystem Instance { get; private set; }

    // ── Kept for SP/UI compatibility — stays true in MP (rooms manage themselves) ──
    private NetworkVariable<bool> isPlayerTurn = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<int> turnNumber = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Per-room server state ─────────────────────────────────────────────
    private class RoomCombatState
    {
        public HashSet<ulong> submitted      = new HashSet<ulong>();
        public bool           enemyPhaseRunning = false;
    }
    private Dictionary<RoomGrid, RoomCombatState> roomStates = new Dictionary<RoomGrid, RoomCombatState>();

    // ── Per-client local state (client-side only) ─────────────────────────
    // Set by targeted ClientRpc from server when this client's room enters/exits enemy phase.
    private bool localIsInEnemyPhase = false;

    // ── Events ────────────────────────────────────────────────────────────
    public event Action       OnPlayerTurnBegin;
    public event Action       OnEnemyPhaseBegin;
    public event Action       OnEnemyPhaseEnd;
    public event EventHandler OnTurnChanged;

    // ── Properties ────────────────────────────────────────────────────────
    // In room-based MP, IsPlayerTurn reflects THIS CLIENT's room state.
    // When the enemy phase is running for your room, this returns false.
    public bool IsPlayerTurn => !localIsInEnemyPhase;
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
        isPlayerTurn.OnValueChanged          -= OnIsPlayerTurnChanged;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReadySubscribeEnemyManager;

        if (NetworkedEnemyManager.Instance != null)
            NetworkedEnemyManager.Instance.OnEnemyTurnsComplete -= HandleGlobalEnemyTurnsComplete;
    }

    private void OnLevelReadySubscribeEnemyManager()
    {
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReadySubscribeEnemyManager;
        TrySubscribeToEnemyManager();
    }

    private bool TrySubscribeToEnemyManager()
    {
        // We no longer use the global OnEnemyTurnsComplete — room turns are self-contained.
        // We keep this subscription only as a safety fallback for SP compatibility.
        if (NetworkedEnemyManager.Instance != null)
        {
            NetworkedEnemyManager.Instance.OnEnemyTurnsComplete += HandleGlobalEnemyTurnsComplete;
            return true;
        }
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyTurnsComplete += HandleGlobalEnemyTurnsComplete;
            return true;
        }
        return false;
    }

    // SP-only fallback — in MP rooms handle themselves
    private void HandleGlobalEnemyTurnsComplete()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;
        isPlayerTurn.Value = true;
        BeginPlayerTurnClientRpc(new ClientRpcParams());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Submit end turn — per-room logic
    // ─────────────────────────────────────────────────────────────────────

    public void SubmitEndTurn()
    {
        if (localIsInEnemyPhase) return;
        SubmitEndTurnServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    /// <summary>
    /// Called server-side (e.g. from ReviveAction) to submit end-turn on behalf of
    /// a specific client. This avoids the SubmitEndTurn() → ServerRpc path which
    /// would use the server's clientId (0) instead of the actual player's id.
    /// </summary>
    public void SubmitEndTurnForClient(ulong clientId)
    {
        if (!IsServer) return;
        ProcessSubmitEndTurn(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitEndTurnServerRpc(ulong clientId) => ProcessSubmitEndTurn(clientId);

    // Shared logic used by both the ServerRpc and the direct server-side call
    private void ProcessSubmitEndTurn(ulong clientId)
    {
        RoomGrid room = GetPlayerRoom(clientId);
        if (room == null)
        {
            Debug.LogWarning($"[TurnSystem] Client {clientId} submitted but has no room.");
            return;
        }

        RoomCombatState state = GetOrCreateRoomState(room);
        if (state.enemyPhaseRunning) return;
        if (state.submitted.Contains(clientId)) return;

        state.submitted.Add(clientId);
        SetPlayerReadyIndicatorClientRpc(clientId, true);

        CheckRoomReady(room, state);
    }

    // Auto-submit dead players so they never block a room's turn from advancing.
    // Called whenever someone submits or a player dies.
    private void CheckRoomReady(RoomGrid room, RoomCombatState state)
    {
        if (state.enemyPhaseRunning) return;

        List<ulong> roomPlayers = GetLivingPlayersInRoom(room);

        // Dead players auto-submit so the room is never stuck waiting for a corpse
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (!IsClientAlive(client.ClientId) && GetPlayerRoom(client.ClientId) == room)
                state.submitted.Add(client.ClientId);
        }

        int ready = 0;
        foreach (ulong id in roomPlayers)
            if (state.submitted.Contains(id)) ready++;

        BroadcastRoomReadyCountToRoom(room, ready, roomPlayers.Count);
        Debug.Log($"[TurnSystem] Room {room.gameObject.name}: {ready}/{roomPlayers.Count} ready.");

        // If no living players in room, nothing to run
        if (roomPlayers.Count == 0) return;

        bool allReady = roomPlayers.TrueForAll(id => state.submitted.Contains(id));
        if (allReady)
            StartCoroutine(RunRoomTurn(room, state, roomPlayers));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-room turn coroutine (server only)
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator RunRoomTurn(RoomGrid room, RoomCombatState state, List<ulong> roomPlayers)
    {
        state.enemyPhaseRunning = true;
        state.submitted.Clear();
        turnNumber.Value++;

        // Clear ready indicators
        foreach (ulong id in roomPlayers)
            SetPlayerReadyIndicatorClientRpc(id, false);

        // Lock input for players in this room
        SetRoomEnemyPhaseClientRpc(true, BuildTargetParams(roomPlayers));
        Debug.Log($"[TurnSystem] Room {room.gameObject.name} — enemy phase begin.");

        // Run enemies in this room only
        List<NetworkedEnemyUnit> roomEnemies = NetworkedEnemyManager.Instance?.GetEnemiesInRoom(room)
                                               ?? new List<NetworkedEnemyUnit>();

        if (roomEnemies.Count > 0)
        {
            bool done = false;
            NetworkedEnemyManager.Instance.RunEnemyTurnsInRoom(room, () => done = true);
            yield return new WaitUntil(() => done);
        }

        // Enemy phase over — restore stamina and unlock input for this room's players
        foreach (ulong id in roomPlayers)
            RestoreStaminaClientRpc(id);

        SetRoomEnemyPhaseClientRpc(false, BuildTargetParams(roomPlayers));

        state.enemyPhaseRunning = false;

        Debug.Log($"[TurnSystem] Room {room.gameObject.name} — player turn resumed.");

        // Broadcast turn changed to room players so UI updates
        NotifyTurnChangedClientRpc(BuildTargetParams(roomPlayers));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers — server only
    // ─────────────────────────────────────────────────────────────────────

    private RoomCombatState GetOrCreateRoomState(RoomGrid room)
    {
        if (!roomStates.TryGetValue(room, out RoomCombatState state))
        {
            state = new RoomCombatState();
            roomStates[room] = state;
        }
        return state;
    }

    private RoomGrid GetPlayerRoom(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return null;

        NetworkedUnit netUnit = client.PlayerObject?.GetComponent<NetworkedUnit>();
        if (netUnit == null) return null;

        RoomGrid room = netUnit.GetCurrentRoomGrid();

        // Fallback: if NetworkedUnit doesn't have a room yet, try the Unit component.
        // This can happen briefly after spawn before InitialiseUnitOnClient fires.
        if (room == null)
        {
            Unit unit = client.PlayerObject.GetComponent<Unit>();
            room = unit?.GetCurrentRoomGrid();
        }

        if (room == null)
            Debug.LogWarning($"[TurnSystem] GetPlayerRoom: client {clientId} has no room on server yet.");

        return room;
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

    private int GetLivingPlayerCount()
    {
        int count = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            if (IsClientAlive(client.ClientId)) count++;
        return Mathf.Max(1, count);
    }

    private ClientRpcParams BuildTargetParams(List<ulong> clientIds)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = clientIds.ToArray() }
        };
    }

    private void BroadcastRoomReadyCountToRoom(RoomGrid room, int ready, int total)
    {
        List<ulong> roomPlayers = GetLivingPlayersInRoom(room);
        UpdateReadyCountClientRpc(ready, total, BuildTargetParams(roomPlayers));
    }

    // ─────────────────────────────────────────────────────────────────────
    // ClientRpcs — targeted to specific rooms only
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Locks or unlocks player input for clients whose room is in enemy phase.
    /// Only sent to players in the room that just submitted.
    /// </summary>
    [ClientRpc]
    private void SetRoomEnemyPhaseClientRpc(bool inEnemyPhase, ClientRpcParams rpcParams = default)
    {
        localIsInEnemyPhase = inEnemyPhase;

        if (inEnemyPhase)
        {
            OnEnemyPhaseBegin?.Invoke();
            OnTurnChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            OnEnemyPhaseEnd?.Invoke();
            OnPlayerTurnBegin?.Invoke();
            OnTurnChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [ClientRpc]
    private void UpdateReadyCountClientRpc(int ready, int total, ClientRpcParams rpcParams = default)
    {
        MultiplayerTurnSystemUI.Instance?.UpdateReadyCount(ready, total);
    }

    [ClientRpc]
    private void NotifyTurnChangedClientRpc(ClientRpcParams rpcParams = default)
    {
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
    }

    [ClientRpc]
    private void RestoreStaminaClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        foreach (var unit in FindObjectsByType<NetworkedUnit>(FindObjectsSortMode.None))
        {
            if (!unit.IsOwner) continue;
            var stats = unit.GetComponent<PlayerStats>();
            stats?.SetCurrentStaminaPoints(stats.GetMaxStaminaPoints());
            return;
        }
    }

    [ClientRpc]
    private void SetPlayerReadyIndicatorClientRpc(ulong clientId, bool ready)
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId != clientId) continue;
            if (client.PlayerObject == null) return;
            client.PlayerObject.GetComponent<PlayerReadyIndicator>()?.SetReady(ready);
            return;
        }
    }

    // Broadcast to all — kept for SP + generic turn UI updates
    [ClientRpc]
    private void BeginPlayerTurnClientRpc(ClientRpcParams rpcParams = default)
    {
        localIsInEnemyPhase = false;
        OnEnemyPhaseEnd?.Invoke();
        OnPlayerTurnBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkVariable callback — kept for SP compatibility
    // ─────────────────────────────────────────────────────────────────────

    private void OnIsPlayerTurnChanged(bool oldVal, bool newVal)
    {
        // In MP this variable stays true — per-room phase handled by SetRoomEnemyPhaseClientRpc.
        // In SP (no network) this still drives the turn system normally.
        if (!NetworkManager.Singleton.IsListening)
        {
            if (newVal) OnPlayerTurnBegin?.Invoke();
            else        OnEnemyPhaseBegin?.Invoke();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Force player turn — called on room navigation / reset
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a player moves to a new room to clear any stale turn state
    /// for the room they left, and ensure they start fresh in the new room.
    /// </summary>
    public void NotifyPlayerChangedRoom(ulong clientId, RoomGrid oldRoom, RoomGrid newRoom)
    {
        if (!IsServer) return;

        // Remove from old room's submitted set so it doesn't block that room's turn
        if (oldRoom != null && roomStates.TryGetValue(oldRoom, out var oldState))
        {
            oldState.submitted.Remove(clientId);
            // If the old room now has all remaining players submitted, run its turn
            List<ulong> remaining = GetLivingPlayersInRoom(oldRoom);
            if (remaining.Count > 0)
            {
                bool allDone = true;
                foreach (ulong id in remaining)
                    if (!oldState.submitted.Contains(id)) { allDone = false; break; }
                if (allDone && !oldState.enemyPhaseRunning)
                    StartCoroutine(RunRoomTurn(oldRoom, oldState, remaining));
            }
        }
    }

    public void ForcePlayerTurn()
    {
        if (!IsServer) return;
        roomStates.Clear();
        isPlayerTurn.Value = true;
        BeginPlayerTurnClientRpc(new ClientRpcParams());
    }

    public void RequestForcePlayerTurn() => ForcePlayerTurnServerRpc();

    [ServerRpc(RequireOwnership = false)]
    private void ForcePlayerTurnServerRpc() => ForcePlayerTurn();

    /// <summary>
    /// Called by RoomNavigationUI when the local player moves to a new room.
    /// Routes to the server to update the per-room turn state.
    /// We can't pass RoomGrid over RPC directly so we pass world positions
    /// and look up the rooms server-side.
    /// </summary>
    public void RequestNotifyRoomChange(ulong clientId, RoomGrid oldRoom, RoomGrid newRoom)
    {
        if (oldRoom == null) return;
        Vector3 oldPos = oldRoom.GetWorldPosition(new GridPosition(0, 0));
        Vector3 newPos = newRoom != null ? newRoom.GetWorldPosition(new GridPosition(0, 0)) : Vector3.zero;
        NotifyRoomChangeServerRpc(clientId,
            oldPos.x, oldPos.y, oldPos.z,
            newPos.x, newPos.y, newPos.z);
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyRoomChangeServerRpc(ulong clientId,
        float oldX, float oldY, float oldZ,
        float newX, float newY, float newZ)
    {
        if (LevelGrid.Instance == null) return;
        RoomGrid oldRoom = LevelGrid.Instance.GetRoomAtPosition(new Vector3(oldX, oldY, oldZ));
        RoomGrid newRoom = LevelGrid.Instance.GetRoomAtPosition(new Vector3(newX, newY, newZ));
        NotifyPlayerChangedRoom(clientId, oldRoom, newRoom);
    }

    public int GetTrunNumber() => turnNumber.Value;
}