using System;
using System.Collections.Generic;
using UnityEngine;

public class MoveAction : BaseAction
{
    private Vector3 targetPosition;
    private PlayerStats playerStats;
    
    [SerializeField] private int maxMoveDistance = 4;

    protected override void Awake()
    {
        base.Awake(); // This gets unit from BaseAction
        playerStats = GetComponent<PlayerStats>();
        targetPosition = transform.position;
    }

    private void Update()
    {
        if (!isActive)
        {
            return;
        }

        Vector3 moveDirection = (targetPosition - transform.position).normalized;

        float stoppingDistance = 0.1f;
        if (Vector3.Distance(transform.position, targetPosition) > stoppingDistance)
        {
            float moveSpeed = 8f;
            transform.position += moveDirection * moveSpeed * Time.deltaTime;
        }
        else
        {
            isActive = false;
            onActionComplete?.Invoke();
        }
    }

    private int GetMoveDistance()
    {
        // If PlayerStats exists, use stamina; otherwise use maxMoveDistance
        if (playerStats != null && playerStats.currentStamina > 0)
        {
            return playerStats.currentStamina;
        }
        return maxMoveDistance;
    }

    public void Move(GridPosition gridPosition, Action onActionComplete)
    {
        this.onActionComplete = onActionComplete;
        
        // Calculate distance and deduct stamina if PlayerStats exists
        GridPosition currentGridPosition = unit.GetGridPosition();
        int distance = Mathf.Max(
            Mathf.Abs(currentGridPosition.x - gridPosition.x),
            Mathf.Abs(currentGridPosition.z - gridPosition.z)
        );

        if (playerStats != null)
        {
            playerStats.currentStamina -= distance;
            playerStats.currentStamina = Mathf.Max(playerStats.currentStamina, 0);
        }

        this.targetPosition = LevelGrid.Instance.GetWorldPosition(gridPosition);
        isActive = true;
    }

    public bool isValidActionGridPosition(GridPosition gridPosition)
    {
        List<GridPosition> validGridPositionList = GetValidActionGridPositionList();
        return validGridPositionList.Contains(gridPosition);
    }

    public List<GridPosition> GetValidActionGridPositionList()
    {
        List<GridPosition> validGridPositionList = new List<GridPosition>();

        GridPosition unitGridPosition = unit.GetGridPosition();
        int moveDistance = GetMoveDistance();

        for (int x = -moveDistance; x <= moveDistance; x++)
        {
            for (int z = -moveDistance; z <= moveDistance; z++)
            {
                int distance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                if (distance > moveDistance) continue;

                GridPosition offsetGridPosition = new GridPosition(x, z);
                GridPosition testGridPosition = unitGridPosition + offsetGridPosition;

                if (!LevelGrid.Instance.isValidGridPosition(testGridPosition))
                    continue;

                if (unitGridPosition == testGridPosition)
                    continue;

                if (LevelGrid.Instance.HasAnyUnitOnGridPosition(testGridPosition))
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