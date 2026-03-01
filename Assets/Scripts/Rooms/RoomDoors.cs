using UnityEngine;

public class RoomDoor : MonoBehaviour
{
    [SerializeField] private LevelGenerator.Direction doorDirection;
    
    private LevelGenerator.PlacedRoom ownerRoom;
    private LevelGenerator.PlacedRoom connectedRoom;
    private LevelGenerator levelGenerator;
    private bool isInitialized = false;

    public void Initialize(LevelGenerator.PlacedRoom owner)
    {
        ownerRoom = owner;
        
        levelGenerator = FindFirstObjectByType<LevelGenerator>();
        if (levelGenerator == null)
        {
            Debug.LogError("RoomDoor: Could not find LevelGenerator!");
            return;
        }

        doorDirection = DetermineDoorDirection(owner);
        connectedRoom = levelGenerator.GetConnectedRoom(owner, doorDirection);

        isInitialized = true;
    }

    private LevelGenerator.Direction DetermineDoorDirection(LevelGenerator.PlacedRoom owner)
    {
        Vector3 doorLocalPos = owner.roomInstance.transform.InverseTransformPoint(transform.position);
        
        float absX = Mathf.Abs(doorLocalPos.x);
        float absZ = Mathf.Abs(doorLocalPos.z);
        
        if (absX > absZ)
        {
            return doorLocalPos.x > 0 ? LevelGenerator.Direction.East : LevelGenerator.Direction.West;
        }
        else
        {
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

        LevelGenerator.Direction oppositeDir = levelGenerator.GetOppositeDirection(doorDirection);
        Vector3 spawnPos = GetSpawnPositionInRoom(connectedRoom, oppositeDir);

        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.TransitionToRoom(connectedRoom, spawnPos);
        }
    }

    private Vector3 GetSpawnPositionInRoom(LevelGenerator.PlacedRoom room, LevelGenerator.Direction entranceDirection)
    {
        RoomConnector.ConnectionPoint entrance = room.connector.GetConnectionPoint(entranceDirection);
        
        if (entrance != null && entrance.transform != null)
        {
            Vector3 offset = entrance.transform.forward * 4f;
            return entrance.transform.position + offset;
        }

        int centerX = room.prefabData.width / 2;
        int centerZ = room.prefabData.height / 2;
        return room.roomGrid.GetWorldPosition(new GridPosition(centerX, centerZ));
    }

    private void Start()
    {
        if (GetComponent<Collider>() == null)
        {
            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 3f, 0.5f);
        }
    }
}