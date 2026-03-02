using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Navigation UI for moving between rooms.
/// Buttons are disabled when enemies are present in the current room.
/// </summary>
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
    [Tooltip("Message shown on buttons when enemies are blocking navigation.")]
    [SerializeField] private string enemyBlockMessage = "Enemies present!";

    [SerializeField] private GameObject enemyWarningPanel; // optional — shown when locked

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

    void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged += UpdateButtons;
    }

    void Start()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged -= UpdateButtons;

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    private void OnLevelReady()
    {
        levelGenerator = FindFirstObjectByType<LevelGenerator>();
        RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
        RoomManager.Instance.OnRoomChanged += OnRoomChanged;
        UpdateButtons();
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom) => UpdateButtons();
    private void OnTurnChanged(object sender, System.EventArgs e)  => UpdateButtons();

    // ── Button state ───────────────────────────────────────────────────────

    private void UpdateButtons()
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        // Check if enemies are blocking navigation
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
                // Lock ALL navigation when enemies are alive in this room
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
        return EnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;
    }

    // ── Travel ─────────────────────────────────────────────────────────────

    private void TravelToRoom(LevelGenerator.Direction direction)
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        // Double-check enemies even if button somehow got clicked
        if (AreEnemiesInCurrentRoom(currentRoom.roomGrid))
        {
            Debug.Log("[RoomNavigationUI] Cannot leave — enemies are present!");
            return;
        }

        LevelGenerator.PlacedRoom targetRoom = levelGenerator.GetConnectedRoom(currentRoom, direction);
        if (targetRoom == null) return;

        Unit player = FindFirstObjectByType<Unit>();
        if (player == null) return;

        int centerX = targetRoom.roomGrid.GetWidth()  / 2;
        int centerZ = targetRoom.roomGrid.GetHeight() / 2;
        GridPosition spawnPos = new GridPosition(centerX, centerZ);

        player.PlaceInRoom(targetRoom.roomGrid, spawnPos);
        RoomManager.Instance.SetCurrentRoom(targetRoom);

        Debug.Log($"[RoomNavigationUI] Traveled {direction} to {targetRoom.roomInstance.name}");
    }
}