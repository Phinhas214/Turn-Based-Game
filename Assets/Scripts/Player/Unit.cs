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
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;
        }
    }

    private void Update()
    {
        if (!isInitialized || currentRoomGrid == null) return;

        GridPosition newGridPosition = currentRoomGrid.GetGridPosition(transform.position);
        
        if (!currentRoomGrid.IsValidGridPosition(newGridPosition))
        {
            // ✅ CRITICAL: Check room changed
            RoomGrid newRoom = LevelGrid.Instance?.GetRoomAtPosition(transform.position);
            
            if (newRoom != null && newRoom != currentRoomGrid)
            {
                Debug.Log($"[Unit] Player moved from {currentRoomGrid.gameObject.name} to {newRoom.gameObject.name}");
                
                // Remove from old room
                currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
                
                // Add to new room
                currentRoomGrid = newRoom;
                gridPosition = newRoom.GetGridPosition(transform.position);
                newRoom.AddUnitAtGridPosition(gridPosition, this);
                
                // Update room manager
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
        
        // ✅ CRITICAL: Update grid position as player moves
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

    // ✅ CRITICAL: PlaceInRoom is called by LevelGenerator
    public void PlaceInRoom(RoomGrid roomGrid, GridPosition newGridPosition)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = roomGrid;
        gridPosition = newGridPosition;

        // ✅ CRITICAL: Get world position
        Vector3 targetPosition = roomGrid.GetWorldPosition(newGridPosition);
        
        // ✅ CRITICAL: Preserve player's current Y value
        // This keeps the player at ground level, not sinking
        float playerY = transform.position.y;
        targetPosition.y = playerY;
        
        Debug.Log($"[Unit] PlaceInRoom - Grid: {newGridPosition}, World: {targetPosition}, Y Preserved: {playerY}");
        
        // ✅ Set position FIRST
        transform.position = targetPosition;
        
        // ✅ Then update grid
        roomGrid.AddUnitAtGridPosition(newGridPosition, this);
        isInitialized = true;
        
        Debug.Log($"[Unit] Player placed in room {roomGrid.gameObject.name} at grid {gridPosition}, world {transform.position}");
    }

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged -= TurnSystem_OnTurnChanged;
        }
    }
}