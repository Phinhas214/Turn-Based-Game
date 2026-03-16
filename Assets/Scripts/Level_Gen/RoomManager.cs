using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;

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

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        Debug.Log($"🏠 [SP] Setting Current Room: {room.roomInstance.name}");

        currentRoom = room;

        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        UpdateCameraForRoom(room);
    }

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room, ulong clientId)
    {
        clientRooms[clientId] = room;

        if (clientId == GetLocalClientId())
        {
            Debug.Log($"🌐 [MP] Local Room Changed: {room.roomInstance.name}");
            currentRoom = room;

            OnRoomChanged?.Invoke(room);
            OnAnyRoomChanged?.Invoke(room);

            UpdateCameraForRoom(room);
        }
    }

    // ── ClearCurrentRoom ──────────────────────────────────────────────────

    /// <summary>
    /// Resets all room tracking. Called by LevelGenerator.ClearLevel()
    /// before regenerating so stale room references don't persist.
    /// </summary>
    public void ClearCurrentRoom()
    {
        currentRoom = null;
        clientRooms.Clear();
        Debug.Log("[RoomManager] Current room cleared.");
    }

    // ── Getters ───────────────────────────────────────────────────────────

    public LevelGenerator.PlacedRoom GetCurrentRoom()
    {
        if (IsMP())
        {
            ulong localId = GetLocalClientId();
            if (clientRooms.TryGetValue(localId, out var room))
                return room;
        }
        return currentRoom;
    }

    public LevelGenerator.PlacedRoom GetCurrentRoom(ulong clientId)
    {
        clientRooms.TryGetValue(clientId, out var room);
        return room;
    }

    public RoomGrid GetCurrentRoomGrid()
    {
        return GetCurrentRoom()?.roomGrid;
    }

    // ── Transition Logic ──────────────────────────────────────────────────

    public void TransitionToRoom(LevelGenerator.PlacedRoom targetRoom, Vector3 doorWorldPosition)
    {
        if (targetRoom == null)
        {
            Debug.LogError("❌ Cannot transition to null room!");
            return;
        }

        Unit player = FindFirstObjectByType<Unit>();
        if (player == null)
        {
            Debug.LogError("❌ RoomManager: No player unit found!");
            return;
        }

        GridPosition spawnGridPosition = targetRoom.roomGrid.GetGridPosition(doorWorldPosition);
        player.PlaceInRoom(targetRoom.roomGrid, spawnGridPosition);

        if (IsMP())
            SetCurrentRoom(targetRoom, GetLocalClientId());
        else
            SetCurrentRoom(targetRoom);
    }

    // ── Camera Integration ────────────────────────────────────────────────

    private void UpdateCameraForRoom(LevelGenerator.PlacedRoom room)
    {
        if (FreeTacticsCameraController.Instance == null)
        {
            Debug.LogWarning("⚠ Camera controller missing from scene.");
            return;
        }

        if (room?.roomInstance == null) return;

        CameraRoomBounds bounds = room.roomInstance.GetComponentInChildren<CameraRoomBounds>();

        if (bounds == null)
        {
            Debug.LogError($"❌ CameraRoomBounds NOT FOUND in room: {room.roomInstance.name}");
            return;
        }

        Bounds b = bounds.GetBounds();
        Debug.Log($"📦 Camera bounds updated: Center={b.center} Size={b.size}");

        FreeTacticsCameraController.Instance.SetRoomBounds(b);
        FreeTacticsCameraController.Instance.FocusOnPlayer();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private bool IsMP()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private ulong GetLocalClientId()
    {
        return NetworkManager.Singleton?.LocalClientId ?? 0;
    }
}