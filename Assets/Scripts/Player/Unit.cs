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
        moveAction      = GetComponent<MoveAction>();
        spinAction      = GetComponent<SpinAction>();
        baseActionArray = GetComponents<BaseAction>();
        playerStats     = GetComponent<PlayerStats>();
    }

    private void Start()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;
    }

    private void Update()
    {
        if (!isInitialized || currentRoomGrid == null) return;

        GridPosition newGridPosition = currentRoomGrid.GetGridPosition(transform.position);

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
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
    }

    public void PlaceInRoom(RoomGrid roomGrid, GridPosition newGridPosition)
    {
        // Remove from old room
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = roomGrid;
        gridPosition    = newGridPosition;

        // Get world position from the room's tilemap
        Vector3 targetPos = roomGrid.GetWorldPosition(newGridPosition);

        // In 3D X/Z game — keep the player's current Y (ground level)
        targetPos.y = transform.position.y;

        transform.position = targetPos;
        roomGrid.AddUnitAtGridPosition(newGridPosition, this);
        isInitialized = true;

        Debug.Log($"[Unit] Placed in {roomGrid.gameObject.name} at grid {newGridPosition}, world {targetPos}");
    }

    // ── Getters ────────────────────────────────────────────────────────────

    public MoveAction   GetMoveAction()        => moveAction;
    public SpinAction   GetSpinAction()        => spinAction;
    public BaseAction[] GetBaseActionArray()   => baseActionArray;
    public RoomGrid     GetCurrentRoomGrid()   => currentRoomGrid;
    public bool         IsInitialized()        => isInitialized;

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

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= TurnSystem_OnTurnChanged;
    }
}