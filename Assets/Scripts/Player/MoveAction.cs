using System.Collections.Generic;
using UnityEngine;

public class MoveAction : MonoBehaviour
{
    private Vector3 targetPosition;
    private Unit unit;
    private PlayerStats playerStats;


    private void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        unit = GetComponent<Unit>();
        targetPosition = transform.position;
    }

    private void Update()
    {
        float stoppingDistance = 0.1f;
        if (Vector3.Distance(transform.position, targetPosition) > stoppingDistance)
        {
            Vector3 moveDirection = (targetPosition - transform.position).normalized;
            float moveSpeed = 4f;
            transform.position += moveDirection * moveSpeed * Time.deltaTime;
        }
    }

    private int GetMoveDistance()
    {
        if (playerStats == null) return 0;
        return playerStats.currentStamina;
    }

    public void Move(GridPosition gridPosition)
    {
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

}
