using UnityEngine;

/// Attach this to the player prefab.
/// Enemies use this to find and track the player without relying on
/// initialization order or room grid comparisons.
/// When multiplayer is added, all player GameObjects will have this component
/// and enemies will target the closest one.
public class PlayerTarget : MonoBehaviour
{
    public static PlayerTarget Instance { get; private set; }

    // For future multiplayer: FindAll instead of single instance
    private void Awake()
    {
        // For now, just track the single player
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// Get the Unit component on this player target.
    public Unit GetUnit() => GetComponent<Unit>();

    /// Get the RoomGrid this player is currently in.
    public RoomGrid GetCurrentRoom() => GetComponent<Unit>()?.GetCurrentRoomGrid();

    /// True if this player is in the given room.
    public bool IsInRoom(RoomGrid room)
    {
        if (room == null) return false;
        RoomGrid playerRoom = GetCurrentRoom();
        return playerRoom != null && playerRoom == room;
    }
}