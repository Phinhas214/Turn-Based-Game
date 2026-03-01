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
        Debug.Log("[RoomNavigationUI] Level ready");
        UpdateButtons();
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        Debug.Log($"[RoomNavigationUI] Room changed to {newRoom.roomInstance.name}");
        UpdateButtons();
    }

    private void OnTurnChanged(object sender, System.EventArgs e)
    {
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null)
        {
            Debug.LogError("[RoomNavigationUI] No current room!");
            return;
        }

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
                if (txt != null)
                    txt.text = $"{direction}\n({connected.prefabData.roomType})";
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
        List<EnemyUnit> enemies = EnemyManager.Instance.GetEnemiesInRoom(room);
        bool hasEnemies = enemies.Count > 0;
        Debug.Log($"[RoomNavigationUI] Enemies in room: {enemies.Count}");
        return hasEnemies;
    }

    private void TravelToRoom(LevelGenerator.Direction direction)
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        if (AreEnemiesInCurrentRoom(currentRoom.roomGrid))
        {
            Debug.Log("[RoomNavigationUI] Cannot leave — enemies are present!");
            return;
        }

        LevelGenerator.PlacedRoom targetRoom = levelGenerator.GetConnectedRoom(currentRoom, direction);
        if (targetRoom == null)
        {
            Debug.Log($"[RoomNavigationUI] No room connected in {direction} direction");
            return;
        }

        Unit player = FindFirstObjectByType<Unit>();
        if (player == null)
        {
            Debug.LogError("[RoomNavigationUI] No player found!");
            return;
        }

        int centerX = targetRoom.roomGrid.GetWidth()  / 2;
        int centerZ = targetRoom.roomGrid.GetHeight() / 2;
        GridPosition spawnPos = new GridPosition(centerX, centerZ);
        
        Debug.Log($"[RoomNavigationUI] Traveling {direction} to {targetRoom.roomInstance.name}, spawn at {spawnPos}");

        player.PlaceInRoom(targetRoom.roomGrid, spawnPos);
        RoomManager.Instance.SetCurrentRoom(targetRoom);
    }
}