using UnityEngine;

public class Unit : MonoBehaviour
{
    private GridPosition gridPosition;
    private MoveAction moveAction;
    private SpinAction spinAction;
    private BaseAction[] baseActionArray;
    private RoomGrid currentRoomGrid;
    private bool isInitialized = false;

    private void Awake()
    {
        moveAction = GetComponent<MoveAction>();
        spinAction = GetComponent<SpinAction>();
        baseActionArray = GetComponents<BaseAction>();
    }

    private void Update()
    {
        if (!isInitialized || currentRoomGrid == null) return;

        GridPosition newGridPosition = currentRoomGrid.GetGridPosition(transform.position);
        
        // Check if we moved out of bounds of current room
        if (!currentRoomGrid.IsValidGridPosition(newGridPosition))
        {
            // Try to find which room we're actually in now
            RoomGrid newRoom = LevelGrid.Instance?.GetRoomAtPosition(transform.position);
            
            if (newRoom != null && newRoom != currentRoomGrid)
            {
                Debug.Log($"Player moved from {currentRoomGrid.gameObject.name} to {newRoom.gameObject.name}");
                
                // Remove from old room
                currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
                
                // Switch to new room
                currentRoomGrid = newRoom;
                gridPosition = newRoom.GetGridPosition(transform.position);
                newRoom.AddUnitAtGridPosition(gridPosition, this);
                
                // Update RoomManager
                LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
                if (levelGen != null)
                {
                    // Find the PlacedRoom that matches this RoomGrid
                    var rooms = levelGen.GetAllRooms();
                    foreach (var room in rooms)
                    {
                        if (room.roomGrid == newRoom)
                        {
                            RoomManager.Instance?.SetCurrentRoom(room);
                            break;
                        }
                    }
                }
                
                return;
            }
        }
        
        // Normal update within same room
        if (newGridPosition != gridPosition && currentRoomGrid.IsValidGridPosition(newGridPosition))
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
            currentRoomGrid.AddUnitAtGridPosition(newGridPosition, this);
            gridPosition = newGridPosition;
        }
    }

    public MoveAction GetMoveAction()
    {
        return moveAction;
    }
    
    public SpinAction GetSpinAction()
    {
        return spinAction;
    }
    
    public BaseAction[] GetBaseActionArray()
    {
        return baseActionArray;
    }
    
    public RoomGrid GetCurrentRoomGrid()
    {
        return currentRoomGrid;
    }
    
    public bool IsInitialized()
    {
        return isInitialized;
    }
    
    public GridPosition GetGridPosition()
    {
        if (currentRoomGrid == null || !isInitialized)
        {
            return new GridPosition(0, 0);
        }
        return gridPosition;
    }

    public void SetCurrentRoomGrid(RoomGrid roomGrid)
    {
        if (currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
        }

        currentRoomGrid = roomGrid;
        
        if (currentRoomGrid != null)
        {
            gridPosition = currentRoomGrid.GetGridPosition(transform.position);
            currentRoomGrid.AddUnitAtGridPosition(gridPosition, this);
            isInitialized = true;
        }
        else
        {
            isInitialized = false;
        }
    }

    public void PlaceInRoom(RoomGrid roomGrid, GridPosition newGridPosition)
    {
        if (currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
        }

        currentRoomGrid = roomGrid;
        gridPosition = newGridPosition;

        transform.position = roomGrid.GetWorldPosition(newGridPosition);
        roomGrid.AddUnitAtGridPosition(newGridPosition, this);
        isInitialized = true;
    }
}