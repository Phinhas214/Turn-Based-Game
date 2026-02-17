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
        
        Debug.Log($"Unit placed in room at grid {newGridPosition}, initialized: {isInitialized}");
    }
}