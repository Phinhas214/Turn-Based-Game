using UnityEngine;

public class RoomDoor : MonoBehaviour
{
    [SerializeField] private LevelGenerator.Direction doorDirection;
    private LevelGenerator.PlacedRoom ownerRoom;
    private LevelGenerator.PlacedRoom connectedRoom;
    private LevelGenerator levelGenerator;
    private bool isInitialized = false;

    // FIXED: Changed to only take PlacedRoom, we'll figure out direction ourselves
    public void Initialize(LevelGenerator.PlacedRoom owner)
    {
        ownerRoom = owner;
        
        // Find level generator
        levelGenerator = FindFirstObjectByType<LevelGenerator>();  // FIXED
        if (levelGenerator == null)
        {
            Debug.LogError("RoomDoor: Could not find LevelGenerator!");
            return;
        }

        // Try to determine which direction this door faces based on its position
        doorDirection = DetermineDoorDirection(owner);
        
        // Find connected room in that direction
        connectedRoom = levelGenerator.GetConnectedRoom(owner, doorDirection);

        isInitialized = true;
        
        Debug.Log($"Door initialized in {owner.roomInstance.name}, facing {doorDirection}, " +
                  $"connects to: {(connectedRoom != null ? connectedRoom.roomInstance.name : "NONE")}");
    }

    private LevelGenerator.Direction DetermineDoorDirection(LevelGenerator.PlacedRoom owner)
    {
        // Get door's local position relative to room center
        Vector3 doorLocalPos = owner.roomInstance.transform.InverseTransformPoint(transform.position);
        
        // Determine which edge the door is closest to
        float absX = Mathf.Abs(doorLocalPos.x);
        float absZ = Mathf.Abs(doorLocalPos.z);
        
        if (absX > absZ)
        {
            // Door is on East or West edge
            return doorLocalPos.x > 0 ? LevelGenerator.Direction.East : LevelGenerator.Direction.West;
        }
        else
        {
            // Door is on North or South edge
            return doorLocalPos.z > 0 ? LevelGenerator.Direction.North : LevelGenerator.Direction.South;
        }
    }

    private void OnMouseDown()
    {
        if (!isInitialized || connectedRoom == null)
        {
            Debug.Log("This door leads nowhere!");
            return;
        }

        Debug.Log($"Transitioning from {ownerRoom.roomInstance.name} to {connectedRoom.roomInstance.name}");

        // Calculate spawn position in connected room (at the opposite door)
        LevelGenerator.Direction oppositeDir = levelGenerator.GetOppositeDirection(doorDirection);
        Vector3 spawnPos = GetSpawnPositionInRoom(connectedRoom, oppositeDir);

        RoomManager.Instance.TransitionToRoom(connectedRoom, spawnPos);
    }

    private Vector3 GetSpawnPositionInRoom(LevelGenerator.PlacedRoom room, LevelGenerator.Direction entranceDirection)
    {
        // Spawn player near the entrance connection point
        RoomConnector.ConnectionPoint entrance = room.connector.GetConnectionPoint(entranceDirection);
        
        if (entrance != null && entrance.transform != null)
        {
            // Offset into the room a bit (2 grid cells worth)
            Vector3 offset = entrance.transform.forward * (levelGenerator.GetComponent<LevelGenerator>() ? 4f : 4f);
            return entrance.transform.position + offset;
        }

        // Fallback to room center
        int centerX = room.prefabData.width / 2;
        int centerZ = room.prefabData.height / 2;
        return room.roomGrid.GetWorldPosition(new GridPosition(centerX, centerZ));
    }

    private void OnMouseEnter()
    {
        if (isInitialized && connectedRoom != null)
        {
            // Could highlight door or show tooltip
            Debug.Log($"Door to {connectedRoom.roomInstance.name}");
        }
    }

    // Optional: Add a collider at runtime if needed
    private void Start()
    {
        // Ensure there's a collider for mouse detection
        if (GetComponent<Collider>() == null)
        {
            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 3f, 0.5f); // Adjust size as needed
            Debug.Log("Added BoxCollider to door");
        }
    }
}