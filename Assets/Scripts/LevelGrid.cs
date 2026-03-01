using System.Collections.Generic;
using UnityEngine;

public class LevelGrid : MonoBehaviour
{
    public static LevelGrid Instance { get; private set; }

    [SerializeField] private List<RoomGrid> roomGrids = new List<RoomGrid>();
    private RoomGrid currentRoomGrid;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void RegisterRoomGrid(RoomGrid roomGrid)
    {
        if (!roomGrids.Contains(roomGrid))
        {
            roomGrids.Add(roomGrid);
            Debug.Log($"[LevelGrid] Registered room grid: {roomGrid.gameObject.name}");
        }
    }

    public void UnregisterRoomGrid(RoomGrid roomGrid)
    {
        if (roomGrids.Contains(roomGrid))
        {
            roomGrids.Remove(roomGrid);
            Debug.Log($"[LevelGrid] Unregistered room grid: {roomGrid.gameObject.name}");
        }
    }

    public RoomGrid GetRoomAtPosition(Vector3 worldPosition)
    {
        foreach (RoomGrid room in roomGrids)
        {
            if (room != null && room.IsPositionInRoom(worldPosition))
                return room;
        }
        return null;
    }

    public RoomGrid GetCurrentRoomGrid()
    {
        return currentRoomGrid;
    }

    // ✅ ADDED: IsInitialized method (needed by UnitActionSystem)
    public bool IsInitialized()
    {
        return currentRoomGrid != null;
    }

    public void SetCurrentRoomGrid(RoomGrid roomGrid)
    {
        currentRoomGrid = roomGrid;
        Debug.Log($"[LevelGrid] Set current room to: {roomGrid?.gameObject.name}");
    }

    public List<Unit> GetUnitListAtGridPosition(GridPosition gridPosition)
    {
        List<Unit> unitList = new List<Unit>();
        
        if (currentRoomGrid == null)
        {
            Debug.LogError("[LevelGrid] No current room grid set!");
            return unitList;
        }

        // ✅ FIXED: Use TilemapRoomGrid instead of GridSystem
        TilemapRoomGrid tilemapGrid = currentRoomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null)
        {
            Debug.LogError("[LevelGrid] Current room has no tilemap grid!");
            return unitList;
        }

        return tilemapGrid.GetUnitsAtGridPosition(gridPosition);
    }

    public List<EnemyUnit> GetEnemiesAtGridPosition(GridPosition gridPosition)
    {
        List<EnemyUnit> enemyList = new List<EnemyUnit>();
        
        if (currentRoomGrid == null)
        {
            Debug.LogError("[LevelGrid] No current room grid set!");
            return enemyList;
        }

        // ✅ FIXED: Use TilemapRoomGrid instead of GridSystem
        TilemapRoomGrid tilemapGrid = currentRoomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null)
        {
            Debug.LogError("[LevelGrid] Current room has no tilemap grid!");
            return enemyList;
        }

        return tilemapGrid.GetEnemiesAtGridPosition(gridPosition);
    }

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return false;
        return currentRoomGrid.IsValidGridPosition(gridPosition);
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

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        if (currentRoomGrid == null) return Vector3.zero;
        return currentRoomGrid.GetWorldPosition(gridPosition);
    }

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        if (currentRoomGrid == null) return new GridPosition(0, 0);
        return currentRoomGrid.GetGridPosition(worldPosition);
    }
}