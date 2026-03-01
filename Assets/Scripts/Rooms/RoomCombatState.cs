using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Coordinates room-wide effects when combat starts/ends.
/// - Locks doors during combat
/// - Unlocks doors after combat
/// - Triggers tile interactions on combat state change
/// 
/// Attach to each room GameObject.
/// </summary>
public class RoomCombatState : MonoBehaviour
{
    private RoomGrid roomGrid;
    private List<CombatDoor> doorsInRoom;
    private List<TileInteraction> interactionsInRoom;
    private bool isInCombat = false;
    private LevelGenerator.PlacedRoom currentPlacedRoom;

    private void OnEnable()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnEnemyPhaseBegin += EnterCombat;
            TurnSystem.Instance.OnEnemyPhaseEnd += ExitCombat;
        }

        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
        }
    }

    private void OnDisable()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnEnemyPhaseBegin -= EnterCombat;
            TurnSystem.Instance.OnEnemyPhaseEnd -= ExitCombat;
        }

        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
        }
    }

    /// <summary>Called when room changes. Updates which doors/interactions to track.</summary>
    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        currentPlacedRoom = newRoom;
        roomGrid = newRoom.roomGrid;

        // ✅ FIXED: Use FindObjectsByType instead of FindObjectsOfType
        doorsInRoom = FindObjectsByType<CombatDoor>(FindObjectsSortMode.None)
            .Where(d => d.transform.IsChildOf(transform))
            .ToList();

        interactionsInRoom = FindObjectsByType<TileInteraction>(FindObjectsSortMode.None)
            .Where(ti => ti.transform.IsChildOf(transform))
            .ToList();

        Debug.Log($"[RoomCombatState] Room '{newRoom.roomInstance.name}' " +
                  $"has {doorsInRoom.Count} doors and {interactionsInRoom.Count} interactions");

        isInCombat = false;
    }

    /// <summary>Called when enemy phase begins. Locks doors and triggers OnCombatStart.</summary>
    private void EnterCombat()
    {
        // Only affect the current room
        if (!isInCombat && roomGrid != null && currentPlacedRoom != null)
        {
            isInCombat = true;
            
            Debug.Log($"[RoomCombatState] Entering combat in '{currentPlacedRoom.roomInstance.name}'");

            // Lock all doors in this room
            foreach (var door in doorsInRoom)
            {
                if (door != null)
                    door.OnCombatStart();
            }

            // Notify all tile interactions
            foreach (var interaction in interactionsInRoom)
            {
                if (interaction != null)
                    interaction.OnCombatStart();
            }
        }
    }

    /// <summary>Called when enemy phase ends. Unlocks doors and triggers OnCombatEnd.</summary>
    private void ExitCombat()
    {
        // Only affect the current room
        if (isInCombat && roomGrid != null && currentPlacedRoom != null)
        {
            isInCombat = false;
            
            Debug.Log($"[RoomCombatState] Exiting combat in '{currentPlacedRoom.roomInstance.name}'");

            // Unlock all doors in this room
            foreach (var door in doorsInRoom)
            {
                if (door != null)
                    door.OnCombatEnd();
            }

            // Notify all tile interactions
            foreach (var interaction in interactionsInRoom)
            {
                if (interaction != null)
                    interaction.OnCombatEnd();
            }
        }
    }

    public bool IsInCombat => isInCombat;
}