using System.Collections.Generic;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    // SP: single current room
    private LevelGenerator.PlacedRoom currentRoom;

    // MP: per-client room tracking (keyed by NGO client ID)
    private Dictionary<ulong, LevelGenerator.PlacedRoom> clientRooms
        = new Dictionary<ulong, LevelGenerator.PlacedRoom>();

    public System.Action<LevelGenerator.PlacedRoom> OnRoomChanged;
    public static System.Action<LevelGenerator.PlacedRoom> OnAnyRoomChanged;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── SetCurrentRoom ────────────────────────────────────────────────────
    // In MP, always call the overload with clientId so each player tracks
    // their own room independently.
    // RoomNavigationUI already calls this — just needs to pass clientId too.

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        // SP path — no network manager present
        currentRoom = room;
        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);
    }

    /// <summary>MP-aware version — sets the room for a specific client.</summary>
    public void SetCurrentRoom(LevelGenerator.PlacedRoom room, ulong clientId)
    {
        clientRooms[clientId] = room;

        // Only fire the event if this is the LOCAL client's room changing
        ulong localId = GetLocalClientId();
        if (clientId == localId)
        {
            currentRoom = room;
            OnRoomChanged?.Invoke(room);
            OnAnyRoomChanged?.Invoke(room);
        }
    }

    // ── GetCurrentRoom ────────────────────────────────────────────────────

    /// <summary>Returns the current room for the LOCAL client.</summary>
    public LevelGenerator.PlacedRoom GetCurrentRoom()
    {
        // In MP, return this client's own room
        if (IsMP())
        {
            ulong localId = GetLocalClientId();
            if (clientRooms.TryGetValue(localId, out var room))
                return room;
        }
        return currentRoom;
    }

    /// <summary>Returns the current room for a specific client (MP only).</summary>
    public LevelGenerator.PlacedRoom GetCurrentRoom(ulong clientId)
    {
        clientRooms.TryGetValue(clientId, out var room);
        return room;
    }

    public RoomGrid GetCurrentRoomGrid()
    {
        return GetCurrentRoom()?.roomGrid;
    }

    // ── Legacy — unchanged for any code that still uses it ───────────────

    public void TransitionToRoom(LevelGenerator.PlacedRoom targetRoom, Vector3 doorWorldPosition)
    {
        if (targetRoom == null) { Debug.LogError("Cannot transition to null room!"); return; }
        Unit player = FindFirstObjectByType<Unit>();
        if (player == null) { Debug.LogError("RoomManager: No player unit found!"); return; }
        GridPosition spawnGridPosition = targetRoom.roomGrid.GetGridPosition(doorWorldPosition);
        player.PlaceInRoom(targetRoom.roomGrid, spawnGridPosition);
        SetCurrentRoom(targetRoom);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private bool IsMP()
    {
        return Unity.Netcode.NetworkManager.Singleton != null
            && Unity.Netcode.NetworkManager.Singleton.IsListening;
    }

    private ulong GetLocalClientId()
    {
        return Unity.Netcode.NetworkManager.Singleton?.LocalClientId ?? 0;
    }
}