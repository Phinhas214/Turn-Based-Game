using System.Collections.Generic;
using UnityEngine;

public class LevelGrid : MonoBehaviour
{
    public static LevelGrid Instance { get; private set; }

    private List<RoomGrid> roomGrids = new List<RoomGrid>();
    private RoomGrid currentRoomGrid;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom room)
    {
        if (room?.roomGrid != null)
        {
            currentRoomGrid = room.roomGrid;
            Debug.Log($"[LevelGrid] Current room synced to: {room.roomInstance.name}");
        }
    }

    // ── Registration ───────────────────────────────────────────────────────

    public void RegisterRoomGrid(RoomGrid roomGrid)
    {
        if (!roomGrids.Contains(roomGrid))
            roomGrids.Add(roomGrid);
    }

    public void UnregisterRoomGrid(RoomGrid roomGrid)
    {
        roomGrids.Remove(roomGrid);
    }

    // ── ClearAllRoomGrids ──────────────────────────────────────────────────

    /// <summary>
    /// Clears all registered room grids and resets the current room reference.
    /// Called by LevelGenerator.ClearLevel() before regenerating so stale
    /// grid references don't carry over into the next run.
    /// </summary>
    public void ClearAllRoomGrids()
    {
        roomGrids.Clear();
        currentRoomGrid = null;
        Debug.Log("[LevelGrid] All room grids cleared.");
    }

    // ── Current room ───────────────────────────────────────────────────────

    public void SetCurrentRoomGrid(RoomGrid roomGrid)
    {
        currentRoomGrid = roomGrid;
        Debug.Log($"[LevelGrid] Current room manually set to: {roomGrid?.gameObject.name}");
    }

    public RoomGrid GetCurrentRoomGrid() => currentRoomGrid;
    public bool IsInitialized() => currentRoomGrid != null;

    // ── Coordinate helpers ─────────────────────────────────────────────────

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        if (currentRoomGrid == null)
        {
            Debug.LogWarning("[LevelGrid] GetGridPosition called but currentRoomGrid is null!");
            return new GridPosition(0, 0);
        }
        return currentRoomGrid.GetGridPosition(worldPosition);
    }

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return false;
        return currentRoomGrid.IsValidGridPosition(gridPosition);
    }

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return Vector3.zero;
        return currentRoomGrid.GetWorldPosition(gridPosition);
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return false;
        return currentRoomGrid.HasAnyUnitOnGridPosition(gridPosition);
    }

    public bool HasAnyEnemyOnGridPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return false;
        return currentRoomGrid.HasAnyEnemyOnGridPosition(gridPosition);
    }

    public RoomGrid GetRoomAtPosition(Vector3 worldPosition)
    {
        foreach (RoomGrid room in roomGrids)
            if (room != null && room.IsPositionInRoom(worldPosition))
                return room;
        return null;
    }

    public List<Unit> GetUnitListAtGridPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return new List<Unit>();
        TilemapRoomGrid tg = currentRoomGrid.GetTilemapRoomGrid();
        return tg != null ? tg.GetUnitsAtGridPosition(gridPosition) : new List<Unit>();
    }

    public List<EnemyUnit> GetEnemiesAtGridPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return new List<EnemyUnit>();
        TilemapRoomGrid tg = currentRoomGrid.GetTilemapRoomGrid();
        return tg != null ? tg.GetEnemiesAtGridPosition(gridPosition) : new List<EnemyUnit>();
    }
}