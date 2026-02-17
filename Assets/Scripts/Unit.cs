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

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += InitializeUnit;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= InitializeUnit;
    }

    private void InitializeUnit()
    {
        // Get current room from RoomManager
        if (RoomManager.Instance != null)
        {
            currentRoomGrid = RoomManager.Instance.GetCurrentRoomGrid();
        }

        // Fallback: try to find room at our world position
        if (currentRoomGrid == null)
        {
            LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
            if (levelGen != null)
            {
                currentRoomGrid = levelGen.GetRoomAtWorldPosition(transform.position);
            }
        }
        
        if (currentRoomGrid != null)
        {
            gridPosition = currentRoomGrid.GetGridPosition(transform.position);
            currentRoomGrid.AddUnitAtGridPosition(gridPosition, this);
            isInitialized = true;
        }
        else
        {
            Debug.LogError($"Unit {gameObject.name} at position {transform.position} could not find a room!");
        }
    }

    private void Update()
    {
        if (!isInitialized || currentRoomGrid == null) return;

        GridPosition newGridPosition = currentRoomGrid.GetGridPosition(transform.position);
        
        if (newGridPosition != gridPosition)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
            currentRoomGrid.AddUnitAtGridPosition(newGridPosition, this);
            gridPosition = newGridPosition;
        }
    }

    public MoveAction GetMoveAction()
    {
        if (moveAction == null)
        {
            Debug.LogError($"GetMoveAction() called but moveAction is NULL on {gameObject.name}!");
        }
        return moveAction;
    }
    
    public SpinAction GetSpinAction() => spinAction;
    
    public GridPosition GetGridPosition()
    {
        if (currentRoomGrid == null || !isInitialized)
        {
            Debug.LogError("Unit.GetGridPosition() called but unit is not initialized!");
            return new GridPosition(0, 0);
        }
        return gridPosition;
    }
    
    public RoomGrid GetCurrentRoomGrid() => currentRoomGrid;
    public BaseAction[] GetBaseActionArray() => baseActionArray;
    public bool IsInitialized() => isInitialized;

    public void SetCurrentRoomGrid(RoomGrid roomGrid)
    {
        // Remove from old grid
        if (currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
        }

        // Set new grid
        currentRoomGrid = roomGrid;
        
        // Add to new grid
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
        // Remove from old grid
        if (currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
        }

        // Set new grid and position
        currentRoomGrid = roomGrid;
        gridPosition = newGridPosition;

        // Update world position and register
        transform.position = roomGrid.GetWorldPosition(newGridPosition);
        roomGrid.AddUnitAtGridPosition(newGridPosition, this);
        isInitialized = true;
    }
}