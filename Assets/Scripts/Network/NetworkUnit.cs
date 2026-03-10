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
    }

    // Set to true by MoveAction while a coroutine move is in progress.
    // Prevents the Update loop from fighting with MoveAction's occupancy management.
    public bool IsMoving { get; set; } = false;

    private void Update()
    {
        // Only the owning client tracks its own grid position changes.
        // Skip while MoveAction is running — it manages occupancy directly.
        if (!IsOwner || !isInitialized || currentRoomGrid == null || IsMoving) return;

        GridPosition newGridPos = currentRoomGrid.GetGridPosition(transform.position);
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
    // Grid placement — called by server on spawn, and by owning client on
    // room navigation
    // ─────────────────────────────────────────────────────────────────────

    public void PlaceInRoom(RoomGrid roomGrid, GridPosition newGridPosition)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

        currentRoomGrid = roomGrid;
        gridPosition    = newGridPosition;

        Vector3 targetPos   = roomGrid.GetWorldPosition(newGridPosition);
        targetPos.y         = transform.position.y;
        transform.position  = targetPos;

        roomGrid.AddUnitAtGridPosition(newGridPosition, GetUnitCompat());
        isInitialized = true;

        // Sync both grid position AND world position to all other clients
        if (IsOwner || IsServer)
            UpdatePositionServerRpc(newGridPosition.x, newGridPosition.z,
                targetPos.x, targetPos.y, targetPos.z);

        Debug.Log($"[NetworkedUnit] PlaceInRoom → grid {newGridPosition}, world {targetPos}");
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

        if (IsOwner || IsServer)
            UpdatePositionServerRpc(newGridPosition.x, newGridPosition.z,
                transform.position.x, transform.position.y, transform.position.z);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server RPC — single call updates both grid vars and world pos vars
    // ─────────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void UpdatePositionServerRpc(int gx, int gz, float wx, float wy, float wz)
    {
        netGridX.Value  = gx;
        netGridZ.Value  = gz;
        netWorldX.Value = wx;
        netWorldY.Value = wy;
        netWorldZ.Value = wz;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Network variable callbacks
    // ─────────────────────────────────────────────────────────────────────

    private void OnNetGridPositionChanged(int oldVal, int newVal)
    {
        if (IsOwner) return;

        gridPosition = new GridPosition(netGridX.Value, netGridZ.Value);

        // Also update currentRoomGrid — find the room that contains this world pos
        // so grid queries work correctly on observer clients too
        if (LevelGrid.Instance != null)
        {
            Vector3 worldPos = new Vector3(netWorldX.Value, netWorldY.Value, netWorldZ.Value);
            RoomGrid foundRoom = LevelGrid.Instance.GetRoomAtPosition(worldPos);
            if (foundRoom != null && foundRoom != currentRoomGrid)
            {
                if (currentRoomGrid != null && isInitialized)
                    currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

                currentRoomGrid = foundRoom;
                currentRoomGrid.AddUnitAtGridPosition(gridPosition, GetUnitCompat());
                isInitialized = true;
            }
        }

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

    private Unit GetUnitCompat() => GetComponent<Unit>();
}