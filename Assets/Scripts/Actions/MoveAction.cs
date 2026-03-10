using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveAction : BaseAction
{
    [SerializeField] private int maxMoveDistance = 4;
    [SerializeField] private float moveSpeed = 8f;

    // Cached once in Awake — same GameObject, never changes
    private NetworkedUnit cachedNetUnit;

    protected override void Awake()
    {
        base.Awake();
        cachedNetUnit = GetComponent<NetworkedUnit>();
    }

    private int GetMoveDistance()
    {
        if (playerStats != null)
            return Mathf.Max(0, playerStats.currentStamina);
        return maxMoveDistance;
    }

    public bool CanMove() => GetMoveDistance() > 0;

    // ─────────────────────────────────────────────────────────────────────
    // Room grid helper — prefers NetworkedUnit so both components stay in sync
    // ─────────────────────────────────────────────────────────────────────

    // ── True if we are in an active network session (MP), false in SP ──────
    private static bool IsNetworked =>
        Unity.Netcode.NetworkManager.Singleton != null &&
        Unity.Netcode.NetworkManager.Singleton.IsListening;

    private RoomGrid GetUnitRoomGrid()
    {
        // Unit.currentRoomGrid is always accurate — updated by PlaceInRoom and Unit.Update
        return unit.GetCurrentRoomGrid();
    }

    private GridPosition GetUnitGridPosition()
    {
        // Unit.gridPosition is always accurate — updated every frame from transform.position
        return unit.GetGridPosition();
    }

    // ─────────────────────────────────────────────────────────────────────

    public void Move(GridPosition targetGridPosition, Action onActionComplete)
    {
        if (!CanMove())
        {
            Debug.Log("[MoveAction] No stamina to move.");
            onActionComplete?.Invoke();
            return;
        }

        RoomGrid currentRoom = GetUnitRoomGrid();
        if (currentRoom == null)
        {
            Debug.LogError("[MoveAction] No current room grid!");
            onActionComplete?.Invoke();
            return;
        }

        GridPosition startPos = GetUnitGridPosition();
        Debug.Log($"[MoveAction] Move called. startPos={startPos} targetPos={targetGridPosition} room={currentRoom}");

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

        // Update grid occupancy
        currentRoom.RemoveUnitAtGridPosition(startPos, unit);
        currentRoom.AddUnitAtGridPosition(finalPos, unit);

        // Deduct stamina
        if (playerStats != null)
            playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - steps);

        // Build waypoints
        List<Vector3> waypoints = new List<Vector3>();
        foreach (GridPosition gp in usedPath)
            waypoints.Add(currentRoom.GetWorldPosition(gp));

        // Update NetworkedUnit's grid position immediately so GetValidActionGridPositionList
        // radiates from the correct position as soon as this move is committed.
        if (IsNetworked && cachedNetUnit != null)
        {
            cachedNetUnit.IsMoving = true;
            cachedNetUnit.SyncGridPositionAfterMove(finalPos);
        }
        isActive = true;
        StartCoroutine(MoveAlongPath(waypoints, finalPos, onActionComplete));
    }

    private IEnumerator MoveAlongPath(List<Vector3> waypoints, GridPosition finalGridPos,
                                      Action onComplete)
    {
        foreach (Vector3 waypoint in waypoints)
        {
            Vector3 target = new Vector3(waypoint.x, transform.position.y, waypoint.z);
            while (Vector3.Distance(transform.position, target) > 0.05f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = target;
        }

        // Clear IsMoving now that the visual coroutine is done.
        if (IsNetworked && cachedNetUnit != null)
            cachedNetUnit.IsMoving = false;

        Debug.Log($"[MoveAction] Move complete. Final grid pos: {finalGridPos}");

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
        if (!CanMove()) return validList;

        RoomGrid currentRoom = GetUnitRoomGrid();
        if (currentRoom == null) return validList;

        TilemapRoomGrid tilemapGrid = currentRoom.GetTilemapRoomGrid();
        if (tilemapGrid == null) return validList;

        GridPosition unitPos = GetUnitGridPosition();
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
}