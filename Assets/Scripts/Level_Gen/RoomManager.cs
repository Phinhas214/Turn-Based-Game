using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;

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

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        Debug.Log("ROOM MANAGER SET CURRENT ROOM: " + room.roomInstance.name);

        currentRoom = room;

        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        // --- CAMERA UPDATE ---
        if (FreeTacticsCameraController.Instance == null)
        {
            Debug.LogWarning("⚠ Camera controller missing");
            return;
        }

        CameraRoomBounds bounds = room.roomInstance.GetComponentInChildren<CameraRoomBounds>();

        if (bounds == null)
        {
            Debug.LogError("❌ CameraRoomBounds NOT FOUND in room: " + room.roomInstance.name);
            return;
        }

        Bounds b = bounds.GetBounds();

        Debug.Log("📦 Camera bounds set: Center=" + b.center + " Size=" + b.size);

        FreeTacticsCameraController.Instance.SetRoomBounds(b);
        FreeTacticsCameraController.Instance.FocusOnPlayer();
    }

    public LevelGenerator.PlacedRoom GetCurrentRoom()
    {
        return currentRoom;
    }

    public RoomGrid GetCurrentRoomGrid()
    {
        return currentRoom?.roomGrid;
    }

    public void TransitionToRoom(LevelGenerator.PlacedRoom targetRoom, Vector3 doorWorldPosition)
    {
        if (targetRoom == null)
        {
            Debug.LogError("❌ Cannot transition to null room!");
            return;
        }

        Debug.Log("➡ Transitioning to room: " + targetRoom.roomInstance.name);

        Unit player = FindFirstObjectByType<Unit>();

        if (player == null)
        {
            Debug.LogError("❌ RoomManager: No player unit found!");
            return;
        }

        GridPosition spawnGridPosition = targetRoom.roomGrid.GetGridPosition(doorWorldPosition);

        player.PlaceInRoom(targetRoom.roomGrid, spawnGridPosition);

        SetCurrentRoom(targetRoom);

        // 🔎 DEBUG — list children in this room
        Debug.Log("---- CHILDREN OF ROOM INSTANCE ----");
        foreach (Transform t in targetRoom.roomInstance.GetComponentsInChildren<Transform>(true))
        {
            Debug.Log("Child: " + t.name);
        }
        Debug.Log("-----------------------------------");

        CameraRoomBounds bounds = targetRoom.roomInstance.GetComponentInChildren<CameraRoomBounds>();

        if (bounds == null)
        {
            Debug.LogError("❌ No CameraRoomBounds found in room: " + targetRoom.roomInstance.name);
            return;
        }

        Debug.Log("✅ CameraRoomBounds FOUND in: " + bounds.gameObject.name);

        Bounds b = bounds.GetBounds();

        Debug.Log(
            "📦 Room Bounds → Center: " + b.center +
            " Size: " + b.size
        );

        if (FreeTacticsCameraController.Instance == null)
        {
            Debug.LogError("❌ Camera controller instance missing!");
            return;
        }

        FreeTacticsCameraController.Instance.SetRoomBounds(b);

        Debug.Log("🎥 Camera received bounds");

        FreeTacticsCameraController.Instance.FocusOnPlayer();
    }
}