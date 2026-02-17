using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;
    private LevelGenerator levelGenerator;

    public System.Action<LevelGenerator.PlacedRoom> OnRoomChanged;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        levelGenerator = FindFirstObjectByType<LevelGenerator>();
    }

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        currentRoom = room;
        Debug.Log($"Current room set to: {room.roomInstance.name}");
        
        // Notify LevelGrid and other systems
        OnRoomChanged?.Invoke(room);
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
            Debug.LogError("Cannot transition to null room!");
            return;
        }

        Unit player = FindFirstObjectByType<Unit>();
        if (player == null)
        {
            Debug.LogError("RoomManager: No player unit found!");
            return;
        }

        // Convert door world position → grid position
        GridPosition spawnGridPosition = targetRoom.roomGrid.GetGridPosition(doorWorldPosition);

        // Place player in new room
        player.PlaceInRoom(targetRoom.roomGrid, spawnGridPosition);

        // Update current room (this triggers OnRoomChanged event)
        SetCurrentRoom(targetRoom);
    }
}