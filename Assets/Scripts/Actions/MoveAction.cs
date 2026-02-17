using System;
using System.Collections.Generic;
using UnityEngine;

public class MoveAction : BaseAction
{
    private Vector3 targetPosition;
    private GridPosition targetGridPosition;
    private PlayerStats playerStats;

    [SerializeField] private int maxMoveDistance = 4;

    protected override void Awake()
    {
        base.Awake();
        playerStats = GetComponent<PlayerStats>();
    }

    private void Update()
    {
        if (!isActive)
            return;

        Vector3 moveDirection = (targetPosition - transform.position).normalized;
        float stoppingDistance = 0.05f;
        float moveSpeed = 8f;

        if (Vector3.Distance(transform.position, targetPosition) > stoppingDistance)
        {
            transform.position += moveDirection * moveSpeed * Time.deltaTime;
        }
        else
        {
            isActive = false;
            unit.PlaceInRoom(unit.GetCurrentRoomGrid(), targetGridPosition);

            onActionComplete?.Invoke();
        }
    }

    private int GetMoveDistance()
    {
        if (playerStats != null)
            return Mathf.Max(playerStats.currentStamina, 0);

        return maxMoveDistance;
    }

    public void Move(GridPosition gridPosition, Action onActionComplete)
    {
        this.onActionComplete = onActionComplete;
        
        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null)
        {
            Debug.LogError("MoveAction: Unit has no current room grid!");
            return;
        }

        // Remove unit from old position
        GridPosition oldGridPosition = unit.GetGridPosition();
        currentRoom.RemoveUnitAtGridPosition(oldGridPosition, unit);
        
        // Calculate distance and deduct stamina
        int distance = Mathf.Max(
            Mathf.Abs(oldGridPosition.x - gridPosition.x),
            Mathf.Abs(oldGridPosition.z - gridPosition.z)
        );

        if (playerStats != null)
        {
            playerStats.currentStamina -= distance;
            playerStats.currentStamina = Mathf.Max(playerStats.currentStamina, 0);
        }

        // Add unit to new position
        currentRoom.AddUnitAtGridPosition(gridPosition, unit);
        
        this.targetPosition = currentRoom.GetWorldPosition(gridPosition);
        isActive = true;
    }

    public bool isValidActionGridPosition(GridPosition gridPosition)
    {
        return GetValidActionGridPositionList().Contains(gridPosition);
    }

    public List<GridPosition> GetValidActionGridPositionList()
    {
        List<GridPosition> validGridPositionList = new();

        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null)
            return validGridPositionList;

        GridPosition unitGridPosition = unit.GetGridPosition();
        int moveDistance = GetMoveDistance();

        for (int x = -moveDistance; x <= moveDistance; x++)
        {
            for (int z = -moveDistance; z <= moveDistance; z++)
            {
                int distance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                if (distance > moveDistance) continue;

                GridPosition testGridPosition =
                    unitGridPosition + new GridPosition(x, z);

                if (!currentRoom.IsValidGridPosition(testGridPosition))
                    continue;

                if (testGridPosition == unitGridPosition)
                    continue;

                if (currentRoom.HasAnyUnitOnGridPosition(testGridPosition))
                    continue;

                validGridPositionList.Add(testGridPosition);
            }
        }

        return validGridPositionList;
    }

    public override string GetActionName()
    {
        return "Move";
    }
}
