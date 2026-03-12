using UnityEngine;

/// <summary>
/// Attach automatically by LevelGenerator to the boss room's exit door strip.
/// Keeps the strip active (wall) until the boss in the room dies,
/// then deactivates it (open hallway).
/// </summary>
public class BossRoomDoor : MonoBehaviour
{
    private RoomGrid bossRoomGrid;
    private bool     isUnlocked = false;

    public void Initialize(RoomGrid roomGrid)
    {
        bossRoomGrid = roomGrid;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared += OnRoomCleared;

        // Start locked
        gameObject.SetActive(true);
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }

    private void OnRoomCleared(RoomGrid clearedRoom)
    {
        if (isUnlocked) return;
        if (clearedRoom != bossRoomGrid) return;

        isUnlocked = true;
        gameObject.SetActive(false); // open the exit
        Debug.Log("[BossRoomDoor] Boss defeated — exit unlocked!");
    }
}