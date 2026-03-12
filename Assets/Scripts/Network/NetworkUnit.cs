using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked replacement for Unit.cs.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkedUnit : NetworkBehaviour
{
    private NetworkVariable<int> netGridX = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netGridZ = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> netWorldX = new NetworkVariable<float>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netWorldY = new NetworkVariable<float>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netWorldZ = new NetworkVariable<float>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GridPosition   gridPosition;
    private MoveAction     moveAction;
    private SpinAction     spinAction;
    private BaseAction[]   baseActionArray;
    private RoomGrid       currentRoomGrid;
    private bool           isInitialized = false;
    private PlayerStats    playerStats;

    public event Action<GridPosition> OnGridPositionChanged;

    public bool IsLocalPlayer => IsOwner;
    public bool IsMoving { get; set; } = false;

    private void Awake()
    {
        moveAction      = GetComponent<MoveAction>();
        spinAction      = GetComponent<SpinAction>();
        baseActionArray = GetComponents<BaseAction>();
        playerStats     = GetComponent<PlayerStats>();
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

        if (IsServer)
        {
            Unit unit = GetComponent<Unit>();
            if (unit != null)
                NetworkedEnemyManager.Instance?.UnregisterPlayer(unit);
        }
    }

    private void Update()
    {
        if (!IsOwner || !isInitialized || currentRoomGrid == null || IsMoving) return;

        GridPosition newGridPos = currentRoomGrid.GetGridPosition(transform.position);

        if (Time.frameCount % 120 == 0)
            Debug.Log($"[NetworkedUnit] Update: transform={transform.position} gridPos={gridPosition} newGridPos={newGridPos}");

        if (newGridPos != gridPosition && currentRoomGrid.IsValidGridPosition(newGridPos))
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());
            currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            gridPosition = newGridPos;

            UpdatePositionServerRpc(newGridPos.x, newGridPos.z,
                transform.position.x, transform.position.y, transform.position.z);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Grid placement
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

        Unit unitComp = GetComponent<Unit>();
        if (unitComp != null)
            unitComp.PlaceInRoom(roomGrid, newGridPosition);

        Vector3 roomOrigin = roomGrid.GetWorldPosition(new GridPosition(0, 0));
        if (IsOwner || IsServer)
            UpdatePositionServerRpc(newGridPosition.x, newGridPosition.z,
                targetPos.x, roomOrigin.y, targetPos.z);

        Debug.Log($"[NetworkedUnit] PlaceInRoom → grid {newGridPosition}, world {targetPos}");
    }

    public void SyncGridPositionAfterMove(GridPosition newGridPosition)
    {
        if (currentRoomGrid == null) return;

        currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());
        gridPosition = newGridPosition;
        currentRoomGrid.AddUnitAtGridPosition(gridPosition, GetUnitCompat());

        Unit unitComp = GetComponent<Unit>();
        if (unitComp != null)
            unitComp.PlaceInRoom(currentRoomGrid, newGridPosition);

        if (IsOwner || IsServer)
            UpdatePositionServerRpc(newGridPosition.x, newGridPosition.z,
                transform.position.x, transform.position.y, transform.position.z);
    }

    // ─────────────────────────────────────────────────────────────────────
    // InitialiseUnitOnClientClientRpc
    // Restored to the version that was working with Owner authority:
    // reads room from Unit.GetCurrentRoomGrid() which the server already
    // set via unit.PlaceInRoom() before spawning — no fragile world-pos lookup.
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    public void InitialiseUnitOnClientClientRpc(int gridX, int gridZ,
        float worldX, float worldY, float worldZ,
        ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        GridPosition gridPos  = new GridPosition(gridX, gridZ);
        Vector3      worldPos = new Vector3(worldX, worldY, worldZ);

        // The server called unit.PlaceInRoom() before spawning the player object,
        // so Unit.currentRoomGrid is already set correctly when this RPC arrives.
        // Read it directly rather than doing a world-position lookup which can
        // return the wrong room if the camera/LevelGrid state differs.
        Unit unit = GetComponent<Unit>();
        if (unit == null) return;

        RoomGrid roomGrid = unit.GetCurrentRoomGrid();

        // Fallback: if Unit somehow doesn't have a room yet, find it by world pos
        if (roomGrid == null && LevelGrid.Instance != null)
            roomGrid = LevelGrid.Instance.GetRoomAtPosition(worldPos);

        if (roomGrid == null)
        {
            Debug.LogWarning("[NetworkedUnit] InitialiseUnitOnClient: Unit has no room grid yet at " + worldPos);
            return;
        }

        // Sync NetworkedUnit to match Unit
        PlaceInRoom(roomGrid, gridPos);

        Debug.Log($"[NetworkedUnit] Client initialised at grid {gridPos} room={roomGrid.gameObject.name}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // UpdatePositionServerRpc
    // ─────────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void UpdatePositionServerRpc(int gx, int gz, float wx, float wy, float wz)
    {
        netGridX.Value  = gx;
        netGridZ.Value  = gz;
        netWorldX.Value = wx;
        netWorldY.Value = wy;
        netWorldZ.Value = wz;

        if (!IsServer) return;

        Vector3  worldPos   = new Vector3(wx, wy, wz);
        RoomGrid serverRoom = LevelGrid.Instance?.GetRoomAtPosition(worldPos);

        if (serverRoom == null)
        {
            Debug.LogWarning($"[NetworkedUnit] UpdatePositionServerRpc: no room found at {worldPos}");
            return;
        }

        GridPosition newGridPos = new GridPosition(gx, gz);

        if (serverRoom != currentRoomGrid)
        {
            // Room changed — update grid occupancy
            if (currentRoomGrid != null && isInitialized)
                currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

            currentRoomGrid = serverRoom;
            serverRoom.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            isInitialized   = true;

            Debug.Log($"[NetworkedUnit] Server: client {OwnerClientId} moved to room '{serverRoom.gameObject.name}' grid {newGridPos}");
        }
        else if (isInitialized && newGridPos != gridPosition)
        {
            // Same room, different cell
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());
            currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
        }
        else if (!isInitialized)
        {
            // FIX: First-call init — server never had currentRoomGrid set yet.
            // Without this branch, enemies calling GetCurrentRoomGrid() on the
            // server get null and never see this player in their room.
            currentRoomGrid = serverRoom;
            serverRoom.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            isInitialized   = true;
            Debug.Log($"[NetworkedUnit] Server: client {OwnerClientId} initialised in '{serverRoom.gameObject.name}' at {newGridPos}");
        }

        gridPosition = newGridPos;

        // Keep Unit component in sync on the server for AI targeting
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

        GridPosition oldGridPos = gridPosition;
        GridPosition newGridPos = new GridPosition(netGridX.Value, netGridZ.Value);

        if (LevelGrid.Instance != null)
        {
            Vector3  worldPos  = new Vector3(netWorldX.Value, netWorldY.Value, netWorldZ.Value);
            RoomGrid foundRoom = LevelGrid.Instance.GetRoomAtPosition(worldPos);
            if (foundRoom != null && foundRoom != currentRoomGrid)
            {
                if (currentRoomGrid != null && isInitialized)
                    currentRoomGrid.RemoveUnitAtGridPosition(oldGridPos, GetUnitCompat());

                currentRoomGrid = foundRoom;
                currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
                isInitialized = true;
            }
            else if (currentRoomGrid != null && isInitialized && newGridPos != oldGridPos)
            {
                currentRoomGrid.RemoveUnitAtGridPosition(oldGridPos, GetUnitCompat());
                currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            }
        }

        gridPosition = newGridPos;
        OnGridPositionChanged?.Invoke(gridPosition);
    }

    private void OnNetWorldPositionChanged(float oldVal, float newVal)
    {
        if (IsOwner) return;

        Vector3 syncedPos = new Vector3(netWorldX.Value, netWorldY.Value, netWorldZ.Value);

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
        if (!IsOwner) return;
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

    private Unit GetUnitCompat() => GetComponent<Unit>();

    // Stub kept for compatibility with NetworkMapGen.cs calls.
    // The cache system was removed — LevelGrid.GetRoomAtPosition is used directly instead.
    public static void RebuildRoomGridCache() { }
}