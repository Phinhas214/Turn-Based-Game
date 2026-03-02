// MoveAction.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class MoveAction : BaseAction
{
    [SerializeField] private int maxMoveDistance = 4;

    private Vector3 targetPosition;
    private bool isMoving = false;
    private Queue<Vector3> waypointQueue = new Queue<Vector3>();

    protected override void Awake()
    {
        base.Awake();
        targetPosition = transform.position;
    }

    private void Update()
    {
        if (!isActive) return;

        if (waypointQueue.Count > 0 && !isMoving)
        {
            targetPosition = waypointQueue.Dequeue();
            isMoving = true;
        }

        if (isMoving)
        {
            Vector3 moveDir = (targetPosition - transform.position).normalized;
            float stoppingDistance = 0.05f;

            if (Vector3.Distance(transform.position, targetPosition) > stoppingDistance)
            {
                float moveSpeed = 8f;
                transform.position += moveDir * moveSpeed * Time.deltaTime;
            }
            else
            {
                transform.position = targetPosition;
                isMoving = false;

                if (waypointQueue.Count == 0)
                {
                    isActive = false;
                    onActionComplete?.Invoke();
                }
            }
        }
    }

    private int GetMoveDistance()
    {
        if (playerStats != null)
            return Mathf.Max(playerStats.currentStamina, 0);
        return maxMoveDistance;
    }

    public void Move(GridPosition targetGridPosition, Action onActionComplete)
    {
        this.onActionComplete = onActionComplete;

        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null)
        {
            Debug.LogError("[MoveAction] Unit has no current room grid!");
            onActionComplete?.Invoke();
            return;
        }

        GridPosition startGridPos = unit.GetGridPosition();

        // Use pathfinder to find a valid path (respects walls)
        Pathfinder pathfinder = new Pathfinder(currentRoom);
        List<GridPosition> path = pathfinder.FindPath(startGridPos, targetGridPosition);

        if (path.Count == 0)
        {
            Debug.LogWarning("[MoveAction] No path found to target!");
            onActionComplete?.Invoke();
            return;
        }

        // Deduct stamina based on actual path length taken
        int steps = Mathf.Min(path.Count, GetMoveDistance());
        List<GridPosition> usedPath = path.GetRange(0, steps);
        GridPosition finalGridPos = usedPath[usedPath.Count - 1];

        // Update grid registration
        currentRoom.RemoveUnitAtGridPosition(startGridPos, unit);
        currentRoom.AddUnitAtGridPosition(finalGridPos, unit);

        if (playerStats != null)
        {
            playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - steps);
        }

        // Queue up waypoints for smooth movement
        waypointQueue.Clear();
        foreach (GridPosition gp in usedPath)
        {
            waypointQueue.Enqueue(currentRoom.GetWorldPosition(gp));
        }

        isActive = true;
        Debug.Log($"[MoveAction] Moving {startGridPos} → {finalGridPos} ({steps} steps)");
    }

    public bool isValidActionGridPosition(GridPosition gridPosition)
    {
        return GetValidActionGridPositionList().Contains(gridPosition);
    }

    public List<GridPosition> GetValidActionGridPositionList()
    {
        List<GridPosition> validList = new List<GridPosition>();

        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null) return validList;

        TilemapRoomGrid tilemapGrid = currentRoom.GetTilemapRoomGrid();
        if (tilemapGrid == null) return validList;

        GridPosition unitPos = unit.GetGridPosition();
        int moveDistance = GetMoveDistance();

        // Use pathfinder to find all reachable tiles within move distance
        Pathfinder pathfinder = new Pathfinder(currentRoom);

        // BFS-style: check all positions within Manhattan range
        for (int x = -moveDistance; x <= moveDistance; x++)
        {
            for (int z = -moveDistance; z <= moveDistance; z++)
            {
                // Manhattan distance filter (orthogonal movement)
                if (Mathf.Abs(x) + Mathf.Abs(z) > moveDistance) continue;
                if (x == 0 && z == 0) continue;

                GridPosition testPos = new GridPosition(unitPos.x + x, unitPos.z + z);

                if (!currentRoom.IsValidGridPosition(testPos)) continue;
                if (!tilemapGrid.IsWalkable(testPos)) continue;

                // Verify a path actually exists (not blocked by walls)
                List<GridPosition> path = pathfinder.FindPath(unitPos, testPos);
                if (path.Count > 0 && path.Count <= moveDistance)
                    validList.Add(testPos);
            }
        }

        return validList;
    }

    public override string GetActionName() => "Move";
}