using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomNavigationUI : MonoBehaviour
{
    [Header("UI Buttons")]
    [SerializeField] private Button northButton;
    [SerializeField] private Button southButton;
    [SerializeField] private Button eastButton;
    [SerializeField] private Button westButton;

    [Header("Button Labels")]
    [SerializeField] private TextMeshProUGUI northText;
    [SerializeField] private TextMeshProUGUI southText;
    [SerializeField] private TextMeshProUGUI eastText;
    [SerializeField] private TextMeshProUGUI westText;

    [Header("Enemy Lock")]
    [SerializeField] private string enemyBlockMessage = "Enemies present!";
    [SerializeField] private GameObject enemyWarningPanel;

    private LevelGenerator levelGenerator;
    private Dictionary<LevelGenerator.Direction, Button> buttonMap;
    private Dictionary<LevelGenerator.Direction, TextMeshProUGUI> textMap;

    private void Awake()
    {
        buttonMap = new Dictionary<LevelGenerator.Direction, Button>
        {
            { LevelGenerator.Direction.North, northButton },
            { LevelGenerator.Direction.South, southButton },
            { LevelGenerator.Direction.East,  eastButton  },
            { LevelGenerator.Direction.West,  westButton  }
        };

        textMap = new Dictionary<LevelGenerator.Direction, TextMeshProUGUI>
        {
            { LevelGenerator.Direction.North, northText },
            { LevelGenerator.Direction.South, southText },
            { LevelGenerator.Direction.East,  eastText  },
            { LevelGenerator.Direction.West,  westText  }
        };

        northButton?.onClick.AddListener(() => TravelToRoom(LevelGenerator.Direction.North));
        southButton?.onClick.AddListener(() => TravelToRoom(LevelGenerator.Direction.South));
        eastButton?.onClick.AddListener(()  => TravelToRoom(LevelGenerator.Direction.East));
        westButton?.onClick.AddListener(()  => TravelToRoom(LevelGenerator.Direction.West));
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady        += OnLevelReady;
        RoomManager.OnAnyRoomChanged       += OnRoomChanged;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady        -= OnLevelReady;
        RoomManager.OnAnyRoomChanged       -= OnRoomChanged;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    private void OnLevelReady()
    {
        levelGenerator = FindFirstObjectByType<LevelGenerator>();
        UpdateButtons();
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom room) => UpdateButtons();
    private void OnTurnChanged(object sender, System.EventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        bool enemiesPresent = AreEnemiesInCurrentRoom(currentRoom.roomGrid);
        if (enemyWarningPanel != null)
            enemyWarningPanel.SetActive(enemiesPresent);

        foreach (LevelGenerator.Direction dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            Button btn = buttonMap[dir];
            TextMeshProUGUI txt = textMap[dir];
            if (btn == null) continue;

            LevelGenerator.PlacedRoom connected = levelGenerator.GetConnectedRoom(currentRoom, dir);

            if (enemiesPresent)
            {
                btn.interactable = false;
                if (txt != null) txt.text = enemyBlockMessage;
            }
            else if (connected != null)
            {
                btn.interactable = true;
                if (txt != null) txt.text = $"{dir}\n({connected.prefabData.roomType})";
            }
            else
            {
                btn.interactable = false;
                if (txt != null) txt.text = dir.ToString();
            }
        }
    }

    private bool AreEnemiesInCurrentRoom(RoomGrid room)
    {
        if (room == null || EnemyManager.Instance == null) return false;
        return EnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;
    }

    private void TravelToRoom(LevelGenerator.Direction travelDirection)
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        if (AreEnemiesInCurrentRoom(currentRoom.roomGrid))
        {
            Debug.Log("[RoomNavigationUI] Cannot leave — enemies present!");
            return;
        }

        LevelGenerator.PlacedRoom targetRoom = 
            levelGenerator.GetConnectedRoom(currentRoom, travelDirection);
        if (targetRoom == null)
        {
            Debug.LogWarning($"[RoomNavigationUI] No room in direction {travelDirection}");
            return;
        }

        if (targetRoom.roomGrid == null)
        {
            Debug.LogError("[RoomNavigationUI] Target room has no RoomGrid!");
            return;
        }

        Unit player = FindFirstObjectByType<Unit>();
        if (player == null) { Debug.LogError("[RoomNavigationUI] No player!"); return; }

        // Player traveled North → they enter the next room from the South
        // So we look for the SpawnPointTile marked as "South" in the target room
        LevelGenerator.Direction entryDirection = 
            levelGenerator.GetOppositeDirection(travelDirection);

        GridPosition spawnPos = GetSpawnPosition(targetRoom, entryDirection);

        // Set room state first, then place player
        RoomManager.Instance.SetCurrentRoom(targetRoom);
        LevelGrid.Instance?.SetCurrentRoomGrid(targetRoom.roomGrid);
        player.PlaceInRoom(targetRoom.roomGrid, spawnPos);

        Debug.Log($"[RoomNavigationUI] Traveled {travelDirection} → " +
                $"entry from {entryDirection} → spawn at {spawnPos}");

        UpdateButtons();
    }

    private GridPosition GetSpawnPosition(LevelGenerator.PlacedRoom room, 
                                        LevelGenerator.Direction entryDirection)
    {
        RoomSpawnPointReader reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();

        if (reader != null && reader.HasSpawnPoint(entryDirection))
        {
            GridPosition sp = reader.GetSpawnPosition(entryDirection, room.roomGrid);
            Debug.Log($"[RoomNavigationUI] Using painted spawn point: {sp} " +
                    $"world: {room.roomGrid.GetWorldPosition(sp)}");
            return sp;
        }

        // Fallback to room center if no spawn point found
        Debug.LogWarning($"[RoomNavigationUI] No spawn point for {entryDirection} " +
                        $"in {room.roomInstance.name} — using center.");
        int centerX = room.roomGrid.GetWidth() / 2;
        int centerZ = room.roomGrid.GetHeight() / 2;
        return new GridPosition(centerX, centerZ);
    }

    /// <summary>Called externally to force an immediate button refresh.</summary>
    public void ForceUpdateButtons()
    {
        UpdateButtons();
    }

}