using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    // SP: single current room reference
    private LevelGenerator.PlacedRoom currentRoom;

    // MP: per-client room tracking (keyed by NGO client ID)
    private Dictionary<ulong, LevelGenerator.PlacedRoom> clientRooms 
        = new Dictionary<ulong, LevelGenerator.PlacedRoom>();

    public System.Action<LevelGenerator.PlacedRoom> OnRoomChanged;
    public static System.Action<LevelGenerator.PlacedRoom> OnAnyRoomChanged;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ── SetCurrentRoom ────────────────────────────────────────────────────

    /// <summary>Single-player path or general room set.</summary>
    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        Debug.Log($"🏠 [SP] Setting Current Room: {room.roomInstance.name}");
        
        currentRoom = room;

        // Trigger Events
        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        // Update local camera boundaries
        UpdateCameraForRoom(room);
    }

    /// <summary>Multiplayer-aware version — sets the room for a specific client.</summary>
    public void SetCurrentRoom(LevelGenerator.PlacedRoom room, ulong clientId)
    {
        clientRooms[clientId] = room;

        // Only update the camera and local events if this is the LOCAL client
        if (clientId == GetLocalClientId())
        {
            Debug.Log($"🌐 [MP] Local Room Changed: {room.roomInstance.name}");
            currentRoom = room;
            
            OnRoomChanged?.Invoke(room);
            OnAnyRoomChanged?.Invoke(room);

            UpdateCameraForRoom(room);
        }
    }

    // ── Getters ───────────────────────────────────────────────────────────

    /// <summary>Returns the current room for the LOCAL client.</summary>
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

        // This handles both the dictionary (if MP) and the camera update
        if (IsMP())
            SetCurrentRoom(targetRoom, GetLocalClientId());
        else
            SetCurrentRoom(targetRoom);
    }

    // ── Camera Integration ───────────────────────────────────────────────

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

    // ── Helpers ──────────────────────────────────────────────────────────

    private bool IsMP()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private ulong GetLocalClientId()
    {
        return NetworkManager.Singleton?.LocalClientId ?? 0;
    }
}