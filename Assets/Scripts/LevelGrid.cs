using System.Collections.Generic;
using UnityEngine;

public class LevelGrid : MonoBehaviour
{
    public static LevelGrid Instance { get; private set; }

    [SerializeField] private Transform gridDebugObjectPrefab;
    private GridSystem gridSystem;

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("There's more than one LevelGrid! " + transform + " - " + Instance);
            Destroy(gameObject);
            return;
        } 
        Instance = this;

        // Create a dummy grid system for backward compatibility
        // But DON'T create debug objects - rooms will handle their own
        gridSystem = new GridSystem(10, 10, 2f);
        // gridSystem.CreateDebugObjects(gridDebugObjectPrefab); // COMMENTED OUT
    }

    // These methods now just work with current room
    public void AddUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            currentRoom.AddUnitAtGridPosition(gridPosition, unit);
        }
    }

    public List<Unit> GetUnitListAtGridPosition(GridPosition gridPosition)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            GridObject gridObject = currentRoom.GetGridSystem().GetGridObject(gridPosition);
            return gridObject.GetUnitList();
        }
        return new List<Unit>();
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            currentRoom.RemoveUnitAtGridPosition(gridPosition, unit);
        }
    }

    public void UnitMovedGridPosition(Unit unit, GridPosition fromGridPosition, GridPosition toGridPosition)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            RemoveUnitAtGridPosition(fromGridPosition, unit);
            AddUnitAtGridPosition(toGridPosition, unit);
        }
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            return currentRoom.HasAnyUnitOnGridPosition(gridPosition);
        }
        return false;
    }

    // Pass Through functions 
    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            return currentRoom.GetGridPosition(worldPosition);
        }
        return new GridPosition(0, 0);
    }

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            return currentRoom.GetWorldPosition(gridPosition);
        }
        return Vector3.zero;
    }

    public bool isValidGridPosition(GridPosition gridPosition)
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            return currentRoom.IsValidGridPosition(gridPosition);
        }
        return false;
    }

    public int GetWidth()
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            return currentRoom.GetWidth();
        }
        return gridSystem.GetWidth(); // Fallback
    }

    public int GetHeight()
    {
        RoomGrid currentRoom = RoomManager.Instance?.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            return currentRoom.GetHeight();
        }
        return gridSystem.GetHeight(); // Fallback
    }
}