using System;
using Unity.Netcode;
using UnityEngine;

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
    private Unit           cachedUnit;

    public event Action<GridPosition> OnGridPositionChanged;
    public bool IsLocalPlayer => IsOwner;
    public bool IsMoving { get; set; } = false;

    // ── FIX A: Static room cache — keeps UpdatePositionServerRpc off FindObjectsByType ──
    internal static System.Collections.Generic.Dictionary<string, RoomGrid> roomGridByName
        = new System.Collections.Generic.Dictionary<string, RoomGrid>();

    public static void RebuildRoomGridCache()
    {
        roomGridByName.Clear();
        foreach (RoomGrid rg in UnityEngine.Object.FindObjectsByType<RoomGrid>(FindObjectsSortMode.None))
        {
            if (rg != null && !string.IsNullOrEmpty(rg.gameObject.name))
                roomGridByName[rg.gameObject.name] = rg;
        }
        Debug.Log($"[NetworkedUnit] Room cache built: {roomGridByName.Count} rooms.");
    }

    private static RoomGrid FindRoomByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (roomGridByName.TryGetValue(name, out RoomGrid rg) && rg != null) return rg;
        RebuildRoomGridCache();
        roomGridByName.TryGetValue(name, out rg);
        return rg;
    }

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

        if (IsServer)
        {
            Unit unit = GetComponent<Unit>();
            if (unit != null)
                NetworkedEnemyManager.Instance?.RegisterPlayer(unit);

            if (roomGridByName.Count == 0)
                RebuildRoomGridCache();
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
                targetPos.x, roomOrigin.y, targetPos.z, roomGrid.gameObject.name);

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
                transform.position.x, transform.position.y, transform.position.z,
                currentRoomGrid?.gameObject.name ?? "");
    }

    [ClientRpc]
    public void InitialiseUnitOnClientClientRpc(int gridX, int gridZ,
        float worldX, float worldY, float worldZ,
        ClientRpcParams rpcParams = default)
    {
        // FIX HOST SPAWN: The host is simultaneously server AND client.
        // The server already placed the host's unit correctly in SpawnAllPlayers
        // via PlaceInRoom. If we let this RPC run on the host-as-client, it
        // re-resolves the room from world position (which can return a different
        // room if camera/scene state differs) and overwrites the correct placement.
        // Remote clients need this RPC to set their initial room; the host does not.
        if (IsServer) return;

        if (!IsOwner) return;

        GridPosition gridPos  = new GridPosition(gridX, gridZ);
        Vector3      worldPos = new Vector3(worldX, worldY, worldZ);

        RoomGrid roomGrid = null;

        if (LevelGrid.Instance != null)
            roomGrid = LevelGrid.Instance.GetRoomAtPosition(worldPos);

        if (roomGrid == null)
        {
            Unit unit = GetComponent<Unit>();
            roomGrid = unit?.GetCurrentRoomGrid();
        }

        if (roomGrid == null)
        {
            foreach (RoomGrid rg in FindObjectsByType<RoomGrid>(FindObjectsSortMode.None))
            {
                if (rg.IsValidGridPosition(gridPos))
                {
                    Vector3 rgOrigin = rg.GetWorldPosition(new GridPosition(0, 0));
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
        Unit unit = GetComponent<Unit>();
        unit?.PlaceInRoom(roomGrid, gridPos);
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

    [ServerRpc(RequireOwnership = false)]
    private void UpdatePositionServerRpc(int gx, int gz, float wx, float wy, float wz, string roomName = "")
    {
        netGridX.Value  = gx;
        netGridZ.Value  = gz;
        netWorldX.Value = wx;
        netWorldY.Value = wy;
        netWorldZ.Value = wz;

        if (!IsServer) return;

        // FIX A: use cache instead of FindObjectsByType
        RoomGrid serverRoom = FindRoomByName(roomName);
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
            if (currentRoomGrid != null && isInitialized)
                currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

            currentRoomGrid = serverRoom;
            serverRoom.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            isInitialized   = true;

            Debug.Log($"[NetworkedUnit] Server: client {OwnerClientId} in room '{serverRoom.gameObject.name}'");
        }
        else if (isInitialized && newGridPos != gridPosition)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());
            currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
        }
        else if (!isInitialized)
        {
            // FIX C: first-call init when both sides were null
            currentRoomGrid = serverRoom;
            serverRoom.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            isInitialized   = true;
        }

        gridPosition = newGridPos;

        Unit unitComp = GetComponent<Unit>();
        if (unitComp != null && unitComp.GetCurrentRoomGrid() != serverRoom)
            unitComp.PlaceInRoom(serverRoom, newGridPos);
        else if (unitComp != null && unitComp.GetGridPosition() != newGridPos)
            unitComp.PlaceInRoom(serverRoom, newGridPos);
    }

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

    private void TurnSystem_OnTurnChanged(object sender, EventArgs e)
    {
        if (!IsOwner) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;
        if (playerStats != null)
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
    }

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