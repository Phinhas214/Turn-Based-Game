using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveAction : BaseAction
{
    [SerializeField] private int maxMoveDistance = 4;
    [SerializeField] private float moveSpeed = 8f;

    protected override void Awake()
    {
        base.Awake();
    }

    private int GetMoveDistance()
    {
        // If stamina system exists, stamina IS the move distance
        // If stamina is 0 or less, cannot move at all
        if (playerStats != null)
            return Mathf.Max(0, playerStats.currentStamina);

        return maxMoveDistance;
    }

    public bool CanMove()
    {
        return GetMoveDistance() > 0;
    }

    public void Move(GridPosition targetGridPosition, Action onActionComplete)
    {
        // Block move if no stamina
        if (!CanMove())
        {
            Debug.Log("[MoveAction] No stamina to move.");
            onActionComplete?.Invoke();
            return;
        }

        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null)
        {
            Debug.LogError("[MoveAction] No current room grid!");
            onActionComplete?.Invoke();
            return;
        }

        GridPosition startPos = unit.GetGridPosition();

        Pathfinder pathfinder = new Pathfinder(currentRoom);
        List<GridPosition> path = pathfinder.FindPath(startPos, targetGridPosition);

        if (path.Count == 0)
        {
            Debug.LogWarning("[MoveAction] No path found!");
            onActionComplete?.Invoke();
            return;
        }

        int steps = Mathf.Min(path.Count, GetMoveDistance());
        List<GridPosition> usedPath = path.GetRange(0, steps);
        GridPosition finalPos = usedPath[usedPath.Count - 1];

        // Update grid
        currentRoom.RemoveUnitAtGridPosition(startPos, unit);
        currentRoom.AddUnitAtGridPosition(finalPos, unit);

        // Deduct stamina by actual steps taken
        if (playerStats != null)
            playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - steps);

        // Build world waypoints
        List<Vector3> waypoints = new List<Vector3>();
        foreach (GridPosition gp in usedPath)
            waypoints.Add(currentRoom.GetWorldPosition(gp));

        isActive = true;
        StartCoroutine(MoveAlongPath(waypoints, onActionComplete));
    }

    private IEnumerator MoveAlongPath(List<Vector3> waypoints, Action onComplete)
    {
        foreach (Vector3 waypoint in waypoints)
        {
            // In 3D X/Z game — match the waypoint X and Z, keep current Y
            Vector3 target = new Vector3(waypoint.x, transform.position.y, waypoint.z);

            while (Vector3.Distance(transform.position, target) > 0.05f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }

            transform.position = target;
        }

        isActive = false;
        onComplete?.Invoke();
    }

    public bool isValidActionGridPosition(GridPosition gridPosition)
    {
        return GetValidActionGridPositionList().Contains(gridPosition);
    }

    public List<GridPosition> GetValidActionGridPositionList()
    {
        List<GridPosition> validList = new List<GridPosition>();

        // No stamina = no valid positions = no highlights
        if (!CanMove()) return validList;

        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null) return validList;

        TilemapRoomGrid tilemapGrid = currentRoom.GetTilemapRoomGrid();
        if (tilemapGrid == null) return validList;

        GridPosition unitPos = unit.GetGridPosition();
        int moveDistance = GetMoveDistance();

        Pathfinder pathfinder = new Pathfinder(currentRoom);

        for (int x = -moveDistance; x <= moveDistance; x++)
        {
            for (int z = -moveDistance; z <= moveDistance; z++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(z) > moveDistance) continue;
                if (x == 0 && z == 0) continue;

                GridPosition testPos = new GridPosition(unitPos.x + x, unitPos.z + z);

                if (!currentRoom.IsValidGridPosition(testPos)) continue;
                if (!tilemapGrid.IsWalkable(testPos)) continue;

                List<GridPosition> path = pathfinder.FindPath(unitPos, testPos);
                if (path.Count > 0 && path.Count <= moveDistance)
                    validList.Add(testPos);
            }
        }

        return validList;
    }

    public override string GetActionName() => "Move";

    public int GetMoveCost(GridPosition targetGridPosition)
    {
        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null) return -1;

        GridPosition startPos = unit.GetGridPosition();

        Pathfinder pathfinder = new Pathfinder(currentRoom);
        List<GridPosition> path = pathfinder.FindPath(startPos, targetGridPosition);

        if (path.Count == 0)
            return -1;

        return path.Count;
    }
}