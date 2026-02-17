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