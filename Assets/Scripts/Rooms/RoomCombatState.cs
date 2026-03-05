using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RoomCombatState : MonoBehaviour
{
    private RoomGrid roomGrid;
    private List<CombatDoor> doorsInRoom = new List<CombatDoor>();
    private List<TileInteraction> interactionsInRoom = new List<TileInteraction>();
    private bool isInCombat = false;
    private LevelGenerator.PlacedRoom currentPlacedRoom;

private void OnEnable()
{
    if (TurnSystem.Instance != null)
        TurnSystem.Instance.OnEnemyPhaseBegin += OnEnemyPhaseBegin;
    
    if (RoomManager.Instance != null)
        RoomManager.Instance.OnRoomChanged += OnRoomChanged;
    
    if (EnemyManager.Instance != null)
        EnemyManager.Instance.OnRoomCleared += OnRoomCleared;
}

    private void OnDisable()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnEnemyPhaseBegin -= OnEnemyPhaseBegin;

        if (RoomManager.Instance != null)
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        currentPlacedRoom = newRoom;
        roomGrid = newRoom.roomGrid;

        doorsInRoom = FindObjectsByType<CombatDoor>(FindObjectsSortMode.None)
            .Where(d => d.transform.IsChildOf(transform))
            .ToList();

        interactionsInRoom = FindObjectsByType<TileInteraction>(FindObjectsSortMode.None)
            .Where(ti => ti.transform.IsChildOf(transform))
            .ToList();

        Debug.Log($"[RoomCombatState] Room '{newRoom.roomInstance.name}' " +
                  $"has {doorsInRoom.Count} doors and {interactionsInRoom.Count} interactions");

        // Reset combat state when entering a new room
        isInCombat = false;
    }

    private void OnEnemyPhaseBegin()
    {
        // Only enter combat if this room has enemies
        if (isInCombat) return;
        if (roomGrid == null || currentPlacedRoom == null) return;

        // Check if there are actually enemies in THIS room before locking
        if (EnemyManager.Instance == null) return;
        List<EnemyUnit> enemies = EnemyManager.Instance.GetEnemiesInRoom(roomGrid);
        if (enemies.Count == 0) return;

        EnterCombat();
    }

    /// <summary>
    /// Called by EnemyManager when the last enemy in a room dies.
    /// Only responds if the cleared room is THIS room.
    /// </summary>
    private void OnRoomCleared(RoomGrid clearedRoom)
    {
        if (clearedRoom != roomGrid) return;
        if (!isInCombat) return;

        Debug.Log($"[RoomCombatState] All enemies dead in " +
                  $"'{currentPlacedRoom?.roomInstance.name}' — exiting combat.");
        ExitCombat();
    }

    private void EnterCombat()
    {
        isInCombat = true;
        Debug.Log($"[RoomCombatState] Entering combat in '{currentPlacedRoom?.roomInstance.name}'");

        foreach (var door in doorsInRoom)
            door?.OnCombatStart();

        foreach (var interaction in interactionsInRoom)
            interaction?.OnCombatStart();
    }

    private void ExitCombat()
    {
        isInCombat = false;
        Debug.Log($"[RoomCombatState] Exiting combat in '{currentPlacedRoom?.roomInstance.name}'");

        foreach (var door in doorsInRoom)
            door?.OnCombatEnd();

        foreach (var interaction in interactionsInRoom)
            interaction?.OnCombatEnd();

        // Force navigation UI to refresh so buttons unlock immediately
        // RoomNavigationUI listens to OnRoomChanged and OnTurnChanged
        // but we need it to update right now without waiting for next turn
        RoomNavigationUI nav = FindFirstObjectByType<RoomNavigationUI>();
        nav?.ForceUpdateButtons();
    }

    public bool IsInCombat => isInCombat;
}