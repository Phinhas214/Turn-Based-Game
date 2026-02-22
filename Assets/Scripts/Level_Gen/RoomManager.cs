using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;

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

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        currentRoom = room;
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

        GridPosition spawnGridPosition = targetRoom.roomGrid.GetGridPosition(doorWorldPosition);
        player.PlaceInRoom(targetRoom.roomGrid, spawnGridPosition);

        SetCurrentRoom(targetRoom);
    }
}