using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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

    private LevelGenerator levelGenerator;
    private Dictionary<LevelGenerator.Direction, Button> buttonMap;
    private Dictionary<LevelGenerator.Direction, TextMeshProUGUI> textMap;

    private void Awake()
    {
        buttonMap = new Dictionary<LevelGenerator.Direction, Button>
        {
            { LevelGenerator.Direction.North, northButton },
            { LevelGenerator.Direction.South, southButton },
            { LevelGenerator.Direction.East, eastButton },
            { LevelGenerator.Direction.West, westButton }
        };

        textMap = new Dictionary<LevelGenerator.Direction, TextMeshProUGUI>
        {
            { LevelGenerator.Direction.North, northText },
            { LevelGenerator.Direction.South, southText },
            { LevelGenerator.Direction.East, eastText },
            { LevelGenerator.Direction.West, westText }
        };

        // Add button listeners
        northButton?.onClick.AddListener(() => TravelToRoom(LevelGenerator.Direction.North));
        southButton?.onClick.AddListener(() => TravelToRoom(LevelGenerator.Direction.South));
        eastButton?.onClick.AddListener(() => TravelToRoom(LevelGenerator.Direction.East));
        westButton?.onClick.AddListener(() => TravelToRoom(LevelGenerator.Direction.West));
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
        
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
        }
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
        }
    }

    private void OnLevelReady()
    {
        levelGenerator = FindFirstObjectByType<LevelGenerator>();

        RoomManager.Instance.OnRoomChanged -= OnRoomChanged; // prevent double-subscribe
        RoomManager.Instance.OnRoomChanged += OnRoomChanged;

        UpdateButtons();
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        // Check each direction
        foreach (var dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            LevelGenerator.Direction direction = (LevelGenerator.Direction)dir;
            LevelGenerator.PlacedRoom connectedRoom = levelGenerator.GetConnectedRoom(currentRoom, direction);

            Button button = buttonMap[direction];
            TextMeshProUGUI text = textMap[direction];

            if (connectedRoom != null)
            {
                // Room exists in this direction - enable button
                button.interactable = true;
                
                if (text != null)
                {
                    string roomName = connectedRoom.prefabData.roomType.ToString();
                    text.text = $"{direction}\n({roomName})";
                }
            }
            else
            {
                // No room in this direction - disable button
                button.interactable = false;
                
                if (text != null)
                {
                    text.text = direction.ToString();
                }
            }
        }
    }

    private void TravelToRoom(LevelGenerator.Direction direction)
    {
        if (RoomManager.Instance == null || levelGenerator == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        LevelGenerator.PlacedRoom targetRoom = levelGenerator.GetConnectedRoom(currentRoom, direction);
        
        if (targetRoom == null)
        {
            Debug.LogWarning($"No room in direction {direction}!");
            return;
        }

        Unit player = FindFirstObjectByType<Unit>();
        if (player == null)
        {
            Debug.LogError("No player found!");
            return;
        }

        // Spawn player at center of target room
        int centerX = targetRoom.roomGrid.GetWidth() / 2;
        int centerZ = targetRoom.roomGrid.GetHeight() / 2;
        GridPosition spawnPos = new GridPosition(centerX, centerZ);

        player.PlaceInRoom(targetRoom.roomGrid, spawnPos);
        RoomManager.Instance.SetCurrentRoom(targetRoom);

        Debug.Log($"Traveled {direction} to {targetRoom.roomInstance.name}");
    }
}