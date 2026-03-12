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

    // ── True if we are in an active network session (MP), false in SP ──────
    private static bool IsNetworked =>
        Unity.Netcode.NetworkManager.Singleton != null &&
        Unity.Netcode.NetworkManager.Singleton.IsListening;

    private RoomGrid GetUnitRoomGrid()   => unit.GetCurrentRoomGrid();
    private GridPosition GetUnitGridPosition() => unit.GetGridPosition();

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
        // SyncGridPositionAfterMove → UpdatePositionServerRpc → server gridPosition updated
        // so enemy AI reads the correct tile on their next turn.
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
        if (currentRoom == null)
        {
            Debug.LogWarning("[MoveAction] GetValidActionGridPositionList: currentRoom is NULL");
            return validList;
        }

        TilemapRoomGrid tilemapGrid = currentRoom.GetTilemapRoomGrid();
        if (tilemapGrid == null)
        {
            Debug.LogWarning("[MoveAction] GetValidActionGridPositionList: tilemapGrid is NULL");
            return validList;
        }

        GridPosition unitPos     = GetUnitGridPosition();
        int          moveDistance = GetMoveDistance();

        Debug.Log($"[MoveAction] GetValidActionGridPositionList: unitPos={unitPos} moveDistance={moveDistance} room={currentRoom.gameObject.name} unit.isInitialized={unit.IsInitialized()} unit.roomGrid={(unit.GetCurrentRoomGrid()?.gameObject.name ?? "NULL")}");

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
                if (IsTileOccupiedByOther(testPos, currentRoom)) continue;

                List<GridPosition> path = pathfinder.FindPath(unitPos, testPos);
                if (path.Count > 0 && path.Count <= moveDistance)
                    validList.Add(testPos);
            }
        }

        return validList;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Occupancy check — prevents players moving onto enemies or other players
    // ─────────────────────────────────────────────────────────────────────

    private bool IsTileOccupiedByOther(GridPosition pos, RoomGrid room)
    {
        // Living enemies in this room
        if (NetworkedEnemyManager.Instance != null)
        {
            foreach (var enemy in NetworkedEnemyManager.Instance.GetEnemiesInRoom(room))
            {
                if (enemy == null || enemy.IsDead) continue;
                if (enemy.GridPosition == pos) return true;
            }
        }

        // Other living players (MP only)
        if (IsNetworked)
        {
            foreach (var client in Unity.Netcode.NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                if (client.PlayerObject == unit.gameObject) continue; // not our own tile
                var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
                // Dead and downed players don't block movement tiles
                if (health != null && (health.IsDead || health.IsDown)) continue;
                var netUnit = client.PlayerObject.GetComponent<NetworkedUnit>();
                if (netUnit == null || netUnit.GetCurrentRoomGrid() != room) continue;
                if (netUnit.GetGridPosition() == pos) return true;
            }
        }

        return false;
    }

    public override string GetActionName() => "Move";

    public int GetMoveCost(GridPosition targetGridPosition)
    {
        RoomGrid currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null) return -1;

        GridPosition startPos = unit.GetGridPosition();
        List<GridPosition> path = new Pathfinder(currentRoom).FindPath(startPos, targetGridPosition);
        return path.Count == 0 ? -1 : path.Count;
    }
}