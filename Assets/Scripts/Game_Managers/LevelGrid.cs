using System.Collections.Generic;
using UnityEngine;

public class LevelGrid : MonoBehaviour
{
    public static LevelGrid Instance { get; private set; }

    // All registered room grids from the generated level
    private List<RoomGrid> allRoomGrids = new List<RoomGrid>();
    private RoomGrid currentRoom;
    private bool isInitialized = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        allRoomGrids = new List<RoomGrid>();
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;

        if (RoomManager.Instance != null)
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;

        if (RoomManager.Instance != null)
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
    }

    // Called by LevelGenerator as each room grid is created
    public void RegisterRoomGrid(RoomGrid roomGrid)
    {
        if (!allRoomGrids.Contains(roomGrid))
        {
            allRoomGrids.Add(roomGrid);
        }
    }

    private void OnLevelReady()
    {
        if (RoomManager.Instance != null)
        {
            currentRoom = RoomManager.Instance.GetCurrentRoomGrid();
        }

        isInitialized = true;
        Debug.Log($"LevelGrid ready with {allRoomGrids.Count} registered room grids");
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        if (newRoom?.roomGrid != null)
        {
            currentRoom = newRoom.roomGrid;
        }
    }

    public void AddUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        if (!isInitialized || currentRoom == null) return;
        currentRoom.AddUnitAtGridPosition(gridPosition, unit);
    }

    public List<Unit> GetUnitListAtGridPosition(GridPosition gridPosition)
    {
        if (!isInitialized || currentRoom == null) return new List<Unit>();
        return currentRoom.GetGridSystem().GetGridObject(gridPosition).GetUnitList();
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        if (!isInitialized || currentRoom == null) return;
        currentRoom.RemoveUnitAtGridPosition(gridPosition, unit);
    }

    public void UnitMovedGridPosition(Unit unit, GridPosition from, GridPosition to)
    {
        if (!isInitialized || currentRoom == null) return;
        currentRoom.RemoveUnitAtGridPosition(from, unit);
        currentRoom.AddUnitAtGridPosition(to, unit);
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        if (!isInitialized || currentRoom == null) return false;
        return currentRoom.HasAnyUnitOnGridPosition(gridPosition);
    }

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        if (!isInitialized || currentRoom == null) return new GridPosition(0, 0);
        return currentRoom.GetGridPosition(worldPosition);
    }

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        if (!isInitialized || currentRoom == null) return Vector3.zero;
        return currentRoom.GetWorldPosition(gridPosition);
    }

    public bool isValidGridPosition(GridPosition gridPosition)
    {
        if (!isInitialized || currentRoom == null) return false;
        return currentRoom.IsValidGridPosition(gridPosition);
    }

    public int GetWidth()
    {
        if (!isInitialized || currentRoom == null) return 10;
        return currentRoom.GetWidth();
    }

    public int GetHeight()
    {
        if (!isInitialized || currentRoom == null) return 10;
        return currentRoom.GetHeight();
    }

    public bool IsInitialized() => isInitialized;
    public RoomGrid GetCurrentRoom() => currentRoom;
    public List<RoomGrid> GetAllRoomGrids() => allRoomGrids;

    // Find which room a world position belongs to
    public RoomGrid GetRoomAtPosition(Vector3 worldPosition)
    {
        foreach (RoomGrid room in allRoomGrids)
        {
            if (room != null && room.IsPositionInRoom(worldPosition))
                return room;
        }
        return null;
    }
}