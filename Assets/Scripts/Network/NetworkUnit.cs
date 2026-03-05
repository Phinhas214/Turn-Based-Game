using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked replacement for Unit.cs.
///
/// KEY RULES:
///   - Only the OWNING client processes input and moves the unit.
///   - Position is synced by NetworkTransform (add that component alongside this one).
///   - Grid state (which cell the unit occupies) is synced via ServerRpc.
///
/// SETUP:
///   - Replace the Unit component on your player prefab with this script.
///   - Add a NetworkObject component (required by NGO).
///   - Add a NetworkTransform component (handles position sync automatically).
///   - Keep MoveAction, CombatAction, PlayerStats, HealthComponent on the same prefab.
///
/// MIGRATION:
///   - All external code that calls unit.GetGridPosition() / PlaceInRoom() still works.
///   - UnitActionSystem checks IsOwner before processing input (see NetworkedUnitActionSystem).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkedUnit : NetworkBehaviour
{
    // ── Grid state synced to all clients ──────────────────────────────────
    private NetworkVariable<int> netGridX = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netGridZ = new NetworkVariable<int>(
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
        // Subscribe to grid position changes so all clients track which cell each player is in
        netGridX.OnValueChanged += OnNetGridPositionChanged;
        netGridZ.OnValueChanged += OnNetGridPositionChanged;

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;

        // Only the local player's unit registers with UnitActionSystem
        if (IsOwner)
        {
            // Let UnitActionSystem know the local player's unit exists
            // (It auto-selects via OnLevelReady, but this is a safety fallback)
            Debug.Log($"[NetworkedUnit] Local player unit spawned (clientId={OwnerClientId}).");
        }
    }

    public override void OnNetworkDespawn()
    {
        netGridX.OnValueChanged -= OnNetGridPositionChanged;
        netGridZ.OnValueChanged -= OnNetGridPositionChanged;

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged -= TurnSystem_OnTurnChanged;
    }

    private void Update()
    {
        // Only the owning client tracks its own position against the grid
        if (!IsOwner || !isInitialized || currentRoomGrid == null) return;

        GridPosition newGridPos = currentRoomGrid.GetGridPosition(transform.position);
        if (newGridPos != gridPosition && currentRoomGrid.IsValidGridPosition(newGridPos))
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());
            currentRoomGrid.AddUnitAtGridPosition(newGridPos, GetUnitCompat());
            gridPosition = newGridPos;

            // Tell server about the new grid position
            UpdateGridPositionServerRpc(newGridPos.x, newGridPos.z);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Grid placement
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places the unit in a room at a specific grid position.
    /// Must be called on the owning client (or server for initial spawn).
    /// </summary>
    public void PlaceInRoom(RoomGrid roomGrid, GridPosition newGridPosition)
    {
        // Remove from old position
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, GetUnitCompat());

        currentRoomGrid = roomGrid;
        gridPosition    = newGridPosition;

        Vector3 targetPos = roomGrid.GetWorldPosition(newGridPosition);
        targetPos.y = transform.position.y;
        transform.position = targetPos;

        roomGrid.AddUnitAtGridPosition(newGridPosition, GetUnitCompat());
        isInitialized = true;

        // Sync grid position to server (which syncs to all other clients)
        if (IsOwner || IsServer)
            UpdateGridPositionServerRpc(newGridPosition.x, newGridPosition.z);

        Debug.Log($"[NetworkedUnit] Placed at grid {newGridPosition}, world {targetPos}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server RPCs
    // ─────────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void UpdateGridPositionServerRpc(int x, int z)
    {
        netGridX.Value = x;
        netGridZ.Value = z;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Network variable callbacks
    // ─────────────────────────────────────────────────────────────────────

    private void OnNetGridPositionChanged(int oldVal, int newVal)
    {
        // Non-owning clients update their local grid tracking
        if (!IsOwner)
        {
            gridPosition = new GridPosition(netGridX.Value, netGridZ.Value);
            OnGridPositionChanged?.Invoke(gridPosition);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Turn handling
    // ─────────────────────────────────────────────────────────────────────

    private void TurnSystem_OnTurnChanged(object sender, EventArgs e)
    {
        // Only owning client refills its own stamina
        if (!IsOwner) return;

        if (playerStats != null)
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Getters — same interface as old Unit.cs
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

    // ─────────────────────────────────────────────────────────────────────
    // Compatibility shim
    // Allows existing code that takes a Unit (not NetworkedUnit) to still work
    // by forwarding to the Unit component if present, or via casting.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Unit component on this GameObject for APIs that still expect Unit.
    /// If you've replaced Unit with NetworkedUnit entirely, remove this and update callers.
    /// </summary>
    private Unit GetUnitCompat()
    {
        return GetComponent<Unit>();
    }
}