using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkedEnemyUnit))]
public class NetworkedEnemyAI : NetworkBehaviour
{
    [Header("Movement Timing")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    private NetworkedEnemyUnit enemyUnit;

    private void Awake() => enemyUnit = GetComponent<NetworkedEnemyUnit>();

    public void TakeTurn(Action onComplete)
    {
        if (!IsServer)                                        { onComplete?.Invoke(); return; }
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead) { onComplete?.Invoke(); return; }
        StartCoroutine(TurnRoutine(onComplete));
    }

    private IEnumerator TurnRoutine(Action onComplete)
    {
        EnemyStats stats = enemyUnit.Stats;
        RoomGrid   room  = enemyUnit.CurrentRoomGrid;
        if (stats == null || room == null) { onComplete?.Invoke(); yield break; }

        int stepsLeft = stats.moveRange;
        while (stepsLeft > 0)
        {
            if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }

            var (bestUnit, bestPos) = FindNearestLivingPlayer(room);
            if (bestUnit == null) break;

            GridPosition myPos = enemyUnit.GridPosition;
            if (ManhattanDist(myPos, bestPos) <= stats.attackRange) break;

            List<GridPosition> path = new Pathfinder(room).FindPathToRange(myPos, bestPos, stats.attackRange);
            if (path.Count == 0) break;

            GridPosition nextStep = path[0];
            if (IsTileOccupied(nextStep, room)) break;

            enemyUnit.MoveToPosition(nextStep);
            stepsLeft--;
            yield return new WaitForSeconds(stepDelay);
        }

        yield return new WaitForSeconds(stepDelay);
        if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }

        {
            var (bestUnit, bestPos) = FindNearestLivingPlayer(room);
            if (bestUnit != null)
            {
                GridPosition myPos = enemyUnit.GridPosition;
                if (ManhattanDist(myPos, bestPos) <= stats.attackRange)
                {
                    PerformAttack(bestUnit, myPos, bestPos, stats);
                    yield return new WaitForSeconds(stepDelay);
                }
            }
        }

        onComplete?.Invoke();
    }

    private (Unit unit, GridPosition pos) FindNearestLivingPlayer(RoomGrid room)
    {
        Unit         best     = null;
        GridPosition bestPos  = default;
        int          bestDist = int.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
            if (health != null && health.IsDead) continue;

            var netUnit = client.PlayerObject.GetComponent<NetworkedUnit>();
            if (netUnit == null) continue;
            if (netUnit.GetCurrentRoomGrid() != room) continue;

            GridPosition pos  = netUnit.GetGridPosition();
            int          dist = ManhattanDist(enemyUnit.GridPosition, pos);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = client.PlayerObject.GetComponent<Unit>();
                bestPos  = pos;
            }
        }

        return (best, bestPos);
    }

    private void PerformAttack(Unit target, GridPosition myPos, GridPosition playerPos, EnemyStats stats)
    {
        if (stats.attackData == null) { Debug.LogWarning($"[NetworkedEnemyAI] {stats.enemyName} has no attackData."); return; }

        var netHealth = target.GetComponent<NetworkedHealthComponent>();
        if (netHealth == null)
        {
            target.GetComponent<HealthComponent>()?.TakeDamage(stats.attackData.baseDamage);
            return;
        }

        if (stats.attackData.attackPattern != null)
        {
            Vector2Int         facing   = GetFacingToward(myPos, playerPos);
            List<GridPosition> hitTiles = stats.attackData.attackPattern.GetAffectedPositions(myPos, facing);
            bool patternHit = false;

            foreach (GridPosition tile in hitTiles)
                if (tile == playerPos) { patternHit = true; break; }

            if (!patternHit)
                foreach (GridPosition tile in hitTiles)
                {
                    GridPosition absolute = new GridPosition(myPos.x + tile.x, myPos.z + tile.z);
                    if (absolute == playerPos) { patternHit = true; break; }
                }

            if (!patternHit && ManhattanDist(myPos, playerPos) <= stats.attackRange)
                patternHit = true;

            if (patternHit)
                netHealth.TakeDamage(stats.attackData.baseDamage);
        }
        else
        {
            netHealth.TakeDamage(stats.attackData.baseDamage);
        }
    }

    private bool IsTileOccupied(GridPosition pos, RoomGrid room)
    {
        var enemies = NetworkedEnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies != null)
            foreach (var other in enemies)
            {
                if (other == enemyUnit || other == null || other.IsDead) continue;
                if (other.GridPosition == pos) return true;
            }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
            if (health != null && (health.IsDead || health.IsDown)) continue;
            var netUnit = client.PlayerObject.GetComponent<NetworkedUnit>();
            if (netUnit == null || netUnit.GetCurrentRoomGrid() != room) continue;
            if (netUnit.GetGridPosition() == pos) return true;
        }

        return false;
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);

    private Vector2Int GetFacingToward(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x;
        int dz = to.z - from.z;
        return Mathf.Abs(dz) > Mathf.Abs(dx)
            ? (dz >= 0 ? new Vector2Int(0, 1) : new Vector2Int(0, -1))
            : (dx >= 0 ? new Vector2Int(1, 0) : new Vector2Int(-1, 0));
    }
}