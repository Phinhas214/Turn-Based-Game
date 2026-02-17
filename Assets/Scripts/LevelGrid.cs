using System.Collections.Generic;
using UnityEngine;

public class LevelGrid : MonoBehaviour
{
    public static LevelGrid Instance { get; private set; }

    [SerializeField] private Transform gridDebugObjectPrefab;
    
    private Dictionary<RoomGrid, GridSystem> roomGridSystems;
    private RoomGrid currentRoom;
    private bool isInitialized = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("There's more than one LevelGrid! " + transform + " - " + Instance);
            Destroy(gameObject);
            return;
        } 
        Instance = this;
        
        roomGridSystems = new Dictionary<RoomGrid, GridSystem>();
        
        Debug.Log("LevelGrid initialized - waiting for level generation");
    }

    private void OnEnable()
    {
        // Subscribe to level generation completion
        LevelGenerator.OnLevelReady += InitializeFromGeneratedLevel;
        
        // Subscribe to room changes
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
        }
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= InitializeFromGeneratedLevel;
        
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
        }
    }

    private void InitializeFromGeneratedLevel()
    {
        Debug.Log("=== LevelGrid: Receiving Generated Level ===");
        
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null)
        {
            Debug.LogError("LevelGrid: No LevelGenerator found!");
            return;
        }

        List<LevelGenerator.PlacedRoom> rooms = levelGen.GetAllRooms();
        if (rooms == null || rooms.Count == 0)
        {
            Debug.LogError("LevelGrid: No rooms to initialize!");
            return;
        }

        // Store reference to each room's grid system
        roomGridSystems.Clear();
        foreach (var room in rooms)
        {
            if (room.roomGrid != null)
            {
                roomGridSystems[room.roomGrid] = room.roomGrid.GetGridSystem();
                Debug.Log($"LevelGrid registered: {room.roomInstance.name}");
            }
        }

        // Set current room
        if (RoomManager.Instance != null)
        {
            currentRoom = RoomManager.Instance.GetCurrentRoomGrid();
        }

        isInitialized = true;
        Debug.Log($"✓ LevelGrid initialized with {roomGridSystems.Count} rooms");
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        if (newRoom != null && newRoom.roomGrid != null)
        {
            currentRoom = newRoom.roomGrid;
            Debug.Log($"LevelGrid: Current room changed to {newRoom.roomInstance.name}");
        }
    }

    // ===== PUBLIC API - All methods work with current room =====

    public void AddUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        if (!isInitialized || currentRoom == null)
        {
            Debug.LogWarning("LevelGrid: Not initialized or no current room!");
            return;
        }
        
        currentRoom.AddUnitAtGridPosition(gridPosition, unit);
    }

    public List<Unit> GetUnitListAtGridPosition(GridPosition gridPosition)
    {
        if (!isInitialized || currentRoom == null)
        {
            return new List<Unit>();
        }
        
        GridObject gridObject = currentRoom.GetGridSystem().GetGridObject(gridPosition);
        return gridObject.GetUnitList();
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        if (!isInitialized || currentRoom == null) return;
        
        currentRoom.RemoveUnitAtGridPosition(gridPosition, unit);
    }

    public void UnitMovedGridPosition(Unit unit, GridPosition fromGridPosition, GridPosition toGridPosition)
    {
        if (!isInitialized || currentRoom == null) return;
        
        RemoveUnitAtGridPosition(fromGridPosition, unit);
        AddUnitAtGridPosition(toGridPosition, unit);
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        if (!isInitialized || currentRoom == null) return false;
        
        return currentRoom.HasAnyUnitOnGridPosition(gridPosition);
    }

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        if (!isInitialized || currentRoom == null)
        {
            Debug.LogWarning("LevelGrid.GetGridPosition: Not initialized!");
            return new GridPosition(0, 0);
        }
        
        return currentRoom.GetGridPosition(worldPosition);
    }

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        if (!isInitialized || currentRoom == null)
        {
            Debug.LogWarning("LevelGrid.GetWorldPosition: Not initialized!");
            return Vector3.zero;
        }
        
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

    // ===== HELPER METHODS =====

    public RoomGrid GetCurrentRoom()
    {
        return currentRoom;
    }

    public bool IsInitialized()
    {
        return isInitialized;
    }

    public GridSystem GetCurrentGridSystem()
    {
        if (!isInitialized || currentRoom == null) return null;
        
        return currentRoom.GetGridSystem();
    }

    // Get room at specific world position (useful for transitions)
    public RoomGrid GetRoomAtPosition(Vector3 worldPosition)
    {
        foreach (var roomGrid in roomGridSystems.Keys)
        {
            if (roomGrid.IsPositionInRoom(worldPosition))
            {
                return roomGrid;
            }
        }
        return null;
    }
}
