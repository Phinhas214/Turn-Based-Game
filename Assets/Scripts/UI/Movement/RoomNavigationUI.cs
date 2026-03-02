// RoomNavigationUI.cs
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

    [Header("Button Labels (Optional)")]
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
        LevelGenerator.OnLevelReady += OnLevelReady;
        if (RoomManager.Instance != null)
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        if (RoomManager.Instance != null)
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
    }

    private void Start()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    private void OnLevelReady()
    {
        levelGenerator = FindFirstObjectByType<LevelGenerator>();
        UpdateButtons();
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom) => UpdateButtons();
    private void OnTurnChanged(object sender, System.EventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        bool enemiesPresent = AreEnemiesInCurrentRoom(currentRoom.roomGrid);
        if (enemyWarningPanel != null)
            enemyWarningPanel.SetActive(enemiesPresent);

        foreach (LevelGenerator.Direction direction in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            Button btn = buttonMap[direction];
            TextMeshProUGUI txt = textMap[direction];
            if (btn == null) continue;

            LevelGenerator.PlacedRoom connected = levelGenerator.GetConnectedRoom(currentRoom, direction);
            bool roomExists = connected != null;

            if (enemiesPresent)
            {
                btn.interactable = false;
                if (txt != null) txt.text = enemyBlockMessage;
            }
            else if (roomExists)
            {
                btn.interactable = true;
                if (txt != null) txt.text = $"{direction}\n({connected.prefabData.roomType})";
            }
            else
            {
                btn.interactable = false;
                if (txt != null) txt.text = direction.ToString();
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

        LevelGenerator.PlacedRoom targetRoom = levelGenerator.GetConnectedRoom(currentRoom, travelDirection);
        if (targetRoom == null) return;

        Unit player = FindFirstObjectByType<Unit>();
        if (player == null) { Debug.LogError("[RoomNavigationUI] No player found!"); return; }

        // Player entered from the OPPOSITE direction of travel
        LevelGenerator.Direction entryDirection = levelGenerator.GetOppositeDirection(travelDirection);
        GridPosition spawnPos = GetSpawnPositionForEntry(targetRoom, entryDirection);

        player.PlaceInRoom(targetRoom.roomGrid, spawnPos);
        RoomManager.Instance.SetCurrentRoom(targetRoom);
        LevelGrid.Instance?.SetCurrentRoomGrid(targetRoom.roomGrid);

        Debug.Log($"[RoomNavigationUI] Entered {targetRoom.roomInstance.name} from {entryDirection} at {spawnPos}");
    }

    private GridPosition GetSpawnPositionForEntry(LevelGenerator.PlacedRoom room, LevelGenerator.Direction entryDirection)
    {
        // Read from the SpawnPoints tilemap layer
        RoomSpawnPointReader reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null)
            return reader.GetSpawnPosition(entryDirection, room.roomGrid);

        // Fallback
        return new GridPosition(room.roomGrid.GetWidth() / 2, room.roomGrid.GetHeight() / 2);
    }
}