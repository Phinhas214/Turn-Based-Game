using System;
using UnityEngine;

public class Unit : MonoBehaviour
{
    private GridPosition gridPosition;
    private MoveAction moveAction;
    private SpinAction spinAction;
    private BaseAction[] baseActionArray;
    private RoomGrid currentRoomGrid;
    private bool isInitialized = false;
    private PlayerStats playerStats;

    private void Awake()
    {
        moveAction = GetComponent<MoveAction>();
        spinAction = GetComponent<SpinAction>();
        baseActionArray = GetComponents<BaseAction>();
        playerStats = GetComponent<PlayerStats>();
    }

    private void Start()
    {
        TurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;
    }

    private void Update()
    {
        if (!isInitialized || currentRoomGrid == null) return;

        GridPosition newGridPosition = currentRoomGrid.GetGridPosition(transform.position);
        
        if (!currentRoomGrid.IsValidGridPosition(newGridPosition))
        {
            RoomGrid newRoom = LevelGrid.Instance?.GetRoomAtPosition(transform.position);
            
            if (newRoom != null && newRoom != currentRoomGrid)
            {
                Debug.Log($"Player moved from {currentRoomGrid.gameObject.name} to {newRoom.gameObject.name}");
                
                currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
                currentRoomGrid = newRoom;
                gridPosition = newRoom.GetGridPosition(transform.position);
                newRoom.AddUnitAtGridPosition(gridPosition, this);
                
                LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
                if (levelGen != null)
                {
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
        
        if (newGridPosition != gridPosition && currentRoomGrid.IsValidGridPosition(newGridPosition))
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
            currentRoomGrid.AddUnitAtGridPosition(newGridPosition, this);
            gridPosition = newGridPosition;
        }
    }

    private void TurnSystem_OnTurnChanged(object sender, EventArgs e)
    {
        if (playerStats != null)
        {
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
        }
    }

    public MoveAction GetMoveAction() => moveAction;
    public SpinAction GetSpinAction() => spinAction;
    public BaseAction[] GetBaseActionArray() => baseActionArray;
    public RoomGrid GetCurrentRoomGrid() => currentRoomGrid;
    public bool IsInitialized() => isInitialized;

    public GridPosition GetGridPosition()
    {
        if (currentRoomGrid == null || !isInitialized)
            return new GridPosition(0, 0);
        return gridPosition;
    }

    public void SetCurrentRoomGrid(RoomGrid roomGrid)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

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
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = roomGrid;
        gridPosition = newGridPosition;

        transform.position = roomGrid.GetWorldPosition(newGridPosition);
        roomGrid.AddUnitAtGridPosition(newGridPosition, this);
        isInitialized = true;
    }
}