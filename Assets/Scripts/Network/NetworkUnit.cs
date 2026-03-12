using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked replacement for Unit.cs.
///
/// KEY RULES:
///   - Only the OWNING client processes input and moves the unit.
///   - Position is synced by NetworkTransform (add that component alongside this one).
///   - Grid state (which cell the unit occupies) is synced via NetworkVariables.
///   - World position is ALSO synced via NetworkVariables so non-owners can
///     teleport the transform when room navigation happens.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkedUnit : NetworkBehaviour
{
    // ── Grid state — synced to all clients ────────────────────────────────
    private NetworkVariable<int> netGridX = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netGridZ = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── World position — synced so room transitions teleport correctly ────
    // NetworkTransform handles smooth movement WITHIN a room.
    // But when PlaceInRoom is called (room navigation / initial spawn) we
    // need a hard teleport on non-owning clients, so we sync world pos too.
    private NetworkVariable<float> netWorldX = new NetworkVariable<float>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netWorldY = new NetworkVariable<float>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netWorldZ = new NetworkVariable<float>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Private runtime ───────────────────────────────────────────────────
    private GridPosition   gridPosition;
    private MoveAction     moveAction;
    private SpinAction     spinAction;
    private BaseAction[]   baseActionArray;
    private RoomGrid       currentRoomGrid;
    private bool           isInitialized = false;
    private PlayerStats    playerStats;
    private Unit           cachedUnit;  // cached to avoid GetComponent every Update/grid call

    // ── Events ────────────────────────────────────────────────────────────
    public event Action<GridPosition> OnGridPositionChanged;

    // ── Properties ────────────────────────────────────────────────────────
    public bool IsLocalPlayer => IsOwner;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        moveAction      = GetComponent<MoveAction>();
        spinAction      = GetComponent<SpinAction>();
        baseActionArray = GetComponents<BaseAction>();
        playerStats     = GetComponent<PlayerStats>();
        cachedUnit      = GetComponent<Unit>();
    }

    public override void OnNetworkSpawn()
    {
        netGridX.OnValueChanged  += OnNetGridPositionChanged;
        netGridZ.OnValueChanged  += OnNetGridPositionChanged;
        netWorldX.OnValueChanged += OnNetWorldPositionChanged;
        netWorldY.OnValueChanged += OnNetWorldPositionChanged;
        netWorldZ.OnValueChanged += OnNetWorldPositionChanged;

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;

        if (IsOwner)
            Debug.Log($"[NetworkedUnit] Local player unit spawned (clientId={OwnerClientId}).");

        // Register with EnemyManager on server so it can find this player
        // without expensive FindObjectsByType calls every enemy turn
        if (IsServer)
        {
            Unit unit = GetComponent<Unit>();
            if (unit != null)
                NetworkedEnemyManager.Instance?.RegisterPlayer(unit);
        }
    }

    public override void OnNetworkDespawn()
    {
        netGridX.OnValueChanged  -= OnNetGridPositionChanged;
        netGridZ.OnValueChanged  -= OnNetGridPositionChanged;
        netWorldX.OnValueChanged -= OnNetWorldPositionChanged;
        netWorldY.OnValueChanged -= OnNetWorldPositionChanged;
        netWorldZ.OnValueChanged -= OnNetWorldPositionChanged;

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged -= TurnSystem_OnTurnChanged;

        // Unregister from enemy manager
        if (IsServer)
        {
            Unit unit = GetComponent<Unit>();
            if (unit != null)
                NetworkedEnemyManager.Instance?.UnregisterPlayer(unit);
        }
    }

    // IsMoving is still set by MoveAction to suppress any stale callbacks during movement.
    // The Update()-based position tracking has been removed — it was sending an RPC
    // every interpolation frame, flooding the server. SyncGridPositionAfterMove in
    // MoveAction sends exactly one RPC when the move is committed, which is sufficient.
    public bool IsMoving { get; set; } = false;

    // ─────────────────────────────────────────────────────────────────────
    // Grid placement — called by server on spawn, and by owning client on
    // room navigation
    // ─────────────────────────────────────────────────────────────────────

    public void PlaceInRoom(RoomGrid roomGrid, GridPosition newGridPosition)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

        currentRoomGrid = roomGrid;
        gridPosition    = newGridPosition;

        Vector3 targetPos  = roomGrid.GetWorldPosition(newGridPosition);
        targetPos.y        = transform.position.y;
        transform.position = targetPos;

        roomGrid.AddUnitAtGridPosition(newGridPosition, GetUnitCompat());
        isInitialized = true;

        // CRITICAL: Keep Unit component in sync — Unit.gridPosition and
        // Unit.currentRoomGrid are what MoveAction and TilemapGridVisual read.
        Unit unitComp = GetComponent<Unit>();
        if (unitComp != null)
            unitComp.PlaceInRoom(roomGrid, newGridPosition);

        // Send the room's ORIGIN world position (Y=0 plane) to UpdatePositionServerRpc,
        // NOT the player's current Y. LevelGrid.GetRoomAtPosition checks XZ bounds;
        // sending the player's Y is fine visually but the server room-lookup needs
        // a position that reliably falls inside the room's registered bounds.
        Vector3 roomOrigin = roomGrid.GetWorldPosition(new GridPosition(0, 0));
        if (IsOwner || IsServer)
            UpdatePositionServerRpc(newGridPosition.x, newGridPosition.z,
                targetPos.x, roomOrigin.y, targetPos.z, roomGrid.gameObject.name);

        Debug.Log($"[NetworkedUnit] PlaceInRoom → grid {newGridPosition}, world {targetPos} | Unit.roomGrid after={(unitComp?.GetCurrentRoomGrid()?.gameObject.name ?? "NULL")} Unit.gridPos after={(unitComp?.GetGridPosition().ToString() ?? "NULL")}");
    }

    /// <summary>
    /// Called by MoveAction after completing a move to sync the final grid position
    /// without teleporting the transform (transform is already at the correct position).
    /// </summary>
    public void SyncGridPositionAfterMove(GridPosition newGridPosition)
    {
        if (currentRoomGrid == null) return;

        currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());
        gridPosition = newGridPosition;
        currentRoomGrid.AddUnitAtGridPosition(gridPosition, GetUnitCompat());

        // Directly call Unit.PlaceInRoom with the final position.
        // This is the only safe way — SetCurrentRoomGrid snapshots transform.position
        // which may not be at the destination yet when called mid-coroutine.
        Unit unitComp = GetComponent<Unit>();
        if (unitComp != null)
        {
            unitComp.PlaceInRoom(currentRoomGrid, newGridPosition);
            Debug.Log($"[NetworkedUnit] SyncGridPositionAfterMove: synced Unit to grid {newGridPosition} unit.gridPos now={unitComp.GetGridPosition()}");
        }

        if (IsOwner || IsServer)
            UpdatePositionServerRpc(newGridPosition.x, newGridPosition.z,
                transform.position.x, transform.position.y, transform.position.z,
                currentRoomGrid?.gameObject.name ?? "");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Called by server after spawning — tells the OWNING client to initialise
    // their local Unit component with the correct room and grid position.
    // Without this Unit.currentRoomGrid is null on the client so Unit.Update()
    // never tracks position and gridPosition stays at (0,0) forever.
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    public void InitialiseUnitOnClientClientRpc(int gridX, int gridZ,
        float worldX, float worldY, float worldZ,
        ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        GridPosition gridPos  = new GridPosition(gridX, gridZ);
        Vector3      worldPos = new Vector3(worldX, worldY, worldZ);

        // Find the room by world position via LevelGrid — this is the most reliable
        // path on the client because Unit.PlaceInRoom was called on the SERVER only,
        // so Unit.currentRoomGrid is null here on the client.
        RoomGrid roomGrid = null;

        if (LevelGrid.Instance != null)
            roomGrid = LevelGrid.Instance.GetRoomAtPosition(worldPos);

        // Fallback: check Unit component in case it somehow got set
        if (roomGrid == null)
        {
            Unit unit = GetComponent<Unit>();
            roomGrid = unit?.GetCurrentRoomGrid();
        }

        // Last resort: scan all RoomGrids in the scene
        if (roomGrid == null)
        {
            foreach (RoomGrid rg in FindObjectsByType<RoomGrid>(FindObjectsSortMode.None))
            {
                if (rg.IsValidGridPosition(gridPos))
                {
                    Vector3 rgOrigin = rg.GetWorldPosition(new GridPosition(0, 0));
                    // Check if worldPos is roughly within this room
                    if (Mathf.Abs(rgOrigin.x - worldPos.x) < rg.GetWidth()  * 2f &&
                        Mathf.Abs(rgOrigin.z - worldPos.z) < rg.GetHeight() * 2f)
                    {
                        roomGrid = rg;
                        break;
                    }
                }
            }
        }

        if (roomGrid == null)
        {
            Debug.LogWarning($"[NetworkedUnit] InitialiseUnitOnClient: could not resolve room at world {worldPos}. Retrying...");
            StartCoroutine(RetryInitialiseUnitOnClient(gridX, gridZ, worldX, worldY, worldZ));
            return;
        }

        ApplyClientInitialisation(roomGrid, gridPos);
    }

    private void ApplyClientInitialisation(RoomGrid roomGrid, GridPosition gridPos)
    {
        // Set both NetworkedUnit AND Unit so both components have the room
        // Unit.currentRoomGrid is what MoveAction, TilemapGridVisual, and
        // RoomNavigationUI's fallback path read — it must be set on the client.
        Unit unit = GetComponent<Unit>();
        unit?.PlaceInRoom(roomGrid, gridPos);

        // PlaceInRoom on NetworkedUnit sets currentRoomGrid and fires UpdatePositionServerRpc
        PlaceInRoom(roomGrid, gridPos);

        Debug.Log($"[NetworkedUnit] Client initialised: room={roomGrid.gameObject.name} grid={gridPos}");
    }

    private System.Collections.IEnumerator RetryInitialiseUnitOnClient(
        int gridX, int gridZ, float worldX, float worldY, float worldZ)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            yield return new WaitForSeconds(0.3f);

            Vector3      worldPos = new Vector3(worldX, worldY, worldZ);
            GridPosition gridPos  = new GridPosition(gridX, gridZ);
            RoomGrid     roomGrid = LevelGrid.Instance?.GetRoomAtPosition(worldPos);

            if (roomGrid == null)
            {
                foreach (RoomGrid rg in FindObjectsByType<RoomGrid>(FindObjectsSortMode.None))
                {
                    Vector3 rgOrigin = rg.GetWorldPosition(new GridPosition(0, 0));
                    if (Mathf.Abs(rgOrigin.x - worldPos.x) < rg.GetWidth()  * 2f &&
                        Mathf.Abs(rgOrigin.z - worldPos.z) < rg.GetHeight() * 2f)
                    {
                        roomGrid = rg; break;
                    }
                }
            }

            if (roomGrid != null)
            {
                ApplyClientInitialisation(roomGrid, gridPos);
                Debug.Log($"[NetworkedUnit] Retry {attempt+1} succeeded: room={roomGrid.gameObject.name} grid={gridPos}");
                yield break;
            }
        }

        Debug.LogError("[NetworkedUnit] InitialiseUnitOnClient: failed to resolve room after 10 retries.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server RPC — single call updates both grid vars and world pos vars
    // ─────────────────────────────────────────────────────────────────────

    // Called by PlaceInRoom whenever the owner (or server) moves to a new room.
    // We pass the room name explicitly — this is far more reliable than trying
    // to resolve LevelGrid.GetRoomAtPosition on the server, which can fail if
    // LevelGrid hasn't registered the room yet or if Y-coordinate bounds are off.
    // Room names are set during generation as e.g. "NormalRoom_(1,0)" and are
    // identical on server and all clients (deterministic from seed).
    [ServerRpc(RequireOwnership = false)]
    private void UpdatePositionServerRpc(int gx, int gz, float wx, float wy, float wz, string roomName = "")
    {
        netGridX.Value  = gx;
        netGridZ.Value  = gz;
        netWorldX.Value = wx;
        netWorldY.Value = wy;
        netWorldZ.Value = wz;

        if (!IsServer) return;

        // Find the room by name — same on server and all clients
        RoomGrid serverRoom = null;
        if (!string.IsNullOrEmpty(roomName))
        {
            foreach (RoomGrid rg in FindObjectsByType<RoomGrid>(FindObjectsSortMode.None))
            {
                if (rg.gameObject.name == roomName) { serverRoom = rg; break; }
            }
        }

        // Fallback to world-position lookup if name lookup failed
        if (serverRoom == null)
            serverRoom = LevelGrid.Instance?.GetRoomAtPosition(new Vector3(wx, wy, wz));

        if (serverRoom == null)
        {
            Debug.LogWarning($"[NetworkedUnit] Server: could not resolve room '{roomName}' for client {OwnerClientId}");
            return;
        }

        GridPosition newGridPos = new GridPosition(gx, gz);

        if (serverRoom != currentRoomGrid)
        {
            // Player changed rooms — update room tracking and grid occupancy
            if (currentRoomGrid != null && isInitialized)
                currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

            currentRoomGrid = serverRoom;
            serverRoom.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            isInitialized   = true;

            Debug.Log($"[NetworkedUnit] Server: client {OwnerClientId} changed room to '{serverRoom.gameObject.name}'");
        }
        else if (isInitialized && newGridPos != gridPosition)
        {
            // Player moved WITHIN the same room — update grid occupancy
            // CRITICAL: this branch was missing. gridPosition was never updated for
            // intra-room moves, so enemies always saw the player at their spawn tile.
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());
            currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
        }

        // Always update the local gridPosition field — both room changes AND
        // intra-room moves must land here or GetGridPosition() returns stale data.
        gridPosition = newGridPos;

        // Keep Unit component in sync on the server
        Unit unitComp = GetComponent<Unit>();
        if (unitComp != null && unitComp.GetCurrentRoomGrid() != serverRoom)
            unitComp.PlaceInRoom(serverRoom, newGridPos);
        else if (unitComp != null && unitComp.GetGridPosition() != newGridPos)
            unitComp.PlaceInRoom(serverRoom, newGridPos);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Network variable callbacks
    // ─────────────────────────────────────────────────────────────────────

    private void OnNetGridPositionChanged(int oldVal, int newVal)
    {
        if (IsOwner) return;

        // Capture old position BEFORE updating the field, so room removal uses the
        // correct old tile (not the new one we're about to assign).
        GridPosition oldGridPos = gridPosition;
        GridPosition newGridPos = new GridPosition(netGridX.Value, netGridZ.Value);

        // Also update currentRoomGrid if the player has crossed rooms.
        // Use world position variables for room lookup — reliable and independent of
        // grid position, which may not yet be set on this client.
        if (LevelGrid.Instance != null)
        {
            Vector3  worldPos  = new Vector3(netWorldX.Value, netWorldY.Value, netWorldZ.Value);
            RoomGrid foundRoom = LevelGrid.Instance.GetRoomAtPosition(worldPos);
            if (foundRoom != null && foundRoom != currentRoomGrid)
            {
                // Remove from old room using the old position
                if (currentRoomGrid != null && isInitialized)
                    currentRoomGrid.RemoveUnitAtGridPosition(oldGridPos, GetUnitCompat());

                currentRoomGrid = foundRoom;
                currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
                isInitialized = true;
            }
            else if (currentRoomGrid != null && isInitialized && newGridPos != oldGridPos)
            {
                // Same room, different tile — update occupancy
                currentRoomGrid.RemoveUnitAtGridPosition(oldGridPos, GetUnitCompat());
                currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            }
        }

        gridPosition = newGridPos;
        OnGridPositionChanged?.Invoke(gridPosition);
    }

    private void OnNetWorldPositionChanged(float oldVal, float newVal)
    {
        // Non-owning clients: teleport the transform to the synced world position.
        // NetworkTransform handles smooth movement within a room, but hard
        // teleports (room transitions, initial spawn) need this direct assignment.
        if (IsOwner) return;

        Vector3 syncedPos = new Vector3(netWorldX.Value, netWorldY.Value, netWorldZ.Value);

        // Only snap if the distance is significant (i.e. a room transition, not noise)
        if (Vector3.Distance(transform.position, syncedPos) > 0.1f)
        {
            transform.position = syncedPos;
            Debug.Log($"[NetworkedUnit] Observer teleported to {syncedPos}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Turn handling
    // ─────────────────────────────────────────────────────────────────────

    private void TurnSystem_OnTurnChanged(object sender, EventArgs e)
    {
        // In multiplayer, stamina is restored by MultiplayerTurnSystem.RestoreStaminaClientRpc
        // which fires per-room the moment all players in that room submit end-turn.
        if (!IsOwner) return;

        // Only restore stamina here in single-player (not connected to NGO)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;

        if (playerStats != null)
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Getters
    // ─────────────────────────────────────────────────────────────────────

    public MoveAction   GetMoveAction()      => moveAction;
    public SpinAction   GetSpinAction()      => spinAction;
    public BaseAction[] GetBaseActionArray() => baseActionArray;
    public RoomGrid     GetCurrentRoomGrid() => currentRoomGrid;
    public bool         IsInitialized()      => isInitialized;

    public GridPosition GetGridPosition()
    {
        if (currentRoomGrid == null || !isInitialized)
            return new GridPosition(netGridX.Value, netGridZ.Value);
        return gridPosition;
    }

    public void SetCurrentRoomGrid(RoomGrid roomGrid)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

        currentRoomGrid = roomGrid;

        if (currentRoomGrid != null)
        {
            gridPosition = currentRoomGrid.GetGridPosition(transform.position);
            currentRoomGrid.AddUnitAtGridPosition(gridPosition, GetUnitCompat());
            isInitialized = true;
        }
        else
        {
            isInitialized = false;
        }
    }

    private Unit GetUnitCompat() => cachedUnit;
}