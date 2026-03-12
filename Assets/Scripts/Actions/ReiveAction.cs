using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ReviveAction — attach to the player prefab alongside other actions.
///
/// HOW IT WORKS:
///   - During the player's turn, shows all adjacent downed allies as valid targets.
///   - Spending this action revives the downed ally at 25% HP (configurable on
///     NetworkedHealthComponent.reviveHealthPercent).
///   - Costs the acting player's full turn (calls UseAction / EndTurn after).
///   - Works in both SP and MP; in MP it routes through a ServerRpc so the server
///     applies the heal authoritatively.
///
/// SETUP:
///   - Add this component to your player prefab.
///   - It will appear as an action in your existing action bar if you have an
///     ActionSystem that reads GetComponent<BaseAction>() or similar.
///   - No extra wiring needed — it uses NetworkManager to find downed players.
/// </summary>
public class ReviveAction : NetworkBehaviour
{
    [Header("Revive Settings")]
    [SerializeField] private int actionPointCost = 1;

    private Unit unit;

    private void Awake()
    {
        unit = GetComponent<Unit>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all downed allied players adjacent to this unit (Manhattan dist = 1).
    /// Use these positions to show valid revive targets in your action UI.
    /// </summary>
    public List<GridPosition> GetValidRevivePositions()
    {
        var result = new List<GridPosition>();
        if (unit == null) return result;

        GridPosition myPos  = unit.GetGridPosition();
        RoomGrid     myRoom = unit.GetCurrentRoomGrid();
        if (myRoom == null) return result;

        // Check adjacent tiles for downed allied players
        var offsets = new[] {
            new GridPosition( 1, 0), new GridPosition(-1, 0),
            new GridPosition( 0, 1), new GridPosition( 0,-1)
        };

        foreach (var offset in offsets)
        {
            GridPosition check = new GridPosition(myPos.x + offset.x, myPos.z + offset.z);
            if (!myRoom.IsValidGridPosition(check)) continue;

            // Find any downed NetworkedHealthComponent at this position
            if (GetDownedPlayerAt(check, myRoom) != null)
                result.Add(check);
        }

        return result;
    }

    /// <summary>
    /// Returns true if there is at least one downed ally adjacent to this unit.
    /// Use this to show/hide the revive button in your UI.
    /// </summary>
    public bool CanRevive() => GetValidRevivePositions().Count > 0;

    /// <summary>
    /// Revive the downed player at the given grid position.
    /// Call this from your action UI when the player confirms the target.
    /// </summary>
    public void TriggerRevive(GridPosition targetPos)
    {
        if (!CanRevive()) return;

        RoomGrid myRoom = unit?.GetCurrentRoomGrid();
        if (myRoom == null) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            ReviveAtPositionServerRpc(targetPos.x, targetPos.z);
        else
            ApplyReviveAtPosition(targetPos, myRoom);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Network
    // ─────────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void ReviveAtPositionServerRpc(int gx, int gz)
    {
        GridPosition targetPos = new GridPosition(gx, gz);
        RoomGrid     myRoom    = unit?.GetCurrentRoomGrid();
        if (myRoom == null) return;

        ApplyReviveAtPosition(targetPos, myRoom);
    }

    private void ApplyReviveAtPosition(GridPosition targetPos, RoomGrid room)
    {
        NetworkedHealthComponent target = GetDownedPlayerAt(targetPos, room);
        if (target == null)
        {
            Debug.LogWarning($"[ReviveAction] No downed player found at {targetPos}");
            return;
        }

        target.Revive();

        // Re-place the revived player's NetworkedUnit in the room grid so they
        // can act again next turn. Their transform is already at the right position.
        NetworkedUnit netUnit = target.GetComponent<NetworkedUnit>();
        netUnit?.PlaceInRoom(room, targetPos);

        Debug.Log($"[ReviveAction] {unit?.gameObject.name} revived {target.gameObject.name} at {targetPos}");

        // Consume the REVIVING player's turn.
        // We must not call SubmitEndTurn() directly from a ServerRpc context — that
        // sends a new ServerRpc as clientId=0 (the server) instead of the reviving
        // player's actual clientId. Pass the clientId explicitly instead.
        NetworkedUnit reviverNetUnit = unit?.GetComponent<NetworkedUnit>();
        if (reviverNetUnit != null)
            MultiplayerTurnSystem.Instance?.SubmitEndTurnForClient(reviverNetUnit.OwnerClientId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private NetworkedHealthComponent GetDownedPlayerAt(GridPosition pos, RoomGrid room)
    {
        // In MP: scan connected clients for a downed player at this tile
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
                if (health == null || !health.IsDown) continue;

                var netUnit = client.PlayerObject.GetComponent<NetworkedUnit>();
                if (netUnit == null) continue;
                if (netUnit.GetCurrentRoomGrid() != room) continue;
                if (netUnit.GetGridPosition() == pos) return health;
            }
            return null;
        }

        // SP fallback: scan Unit components (no network)
        foreach (Unit u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            if (u == unit) continue; // don't revive self
            var health = u.GetComponent<NetworkedHealthComponent>();
            if (health == null || !health.IsDown) continue;
            if (u.GetCurrentRoomGrid() == room && u.GetGridPosition() == pos) return health;
        }
        return null;
    }
}