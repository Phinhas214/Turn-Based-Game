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

    // Cached reference to whichever level generator is active
    private LevelGenerator           spLevelGenerator;  // single-player
    private NetworkedLevelGenerator   mpLevelGenerator;  // multiplayer

    private Dictionary<LevelGenerator.Direction, Button>             buttonMap;
    private Dictionary<LevelGenerator.Direction, TextMeshProUGUI>    textMap;

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
        LevelGenerator.OnLevelReady           += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged          += OnRoomChanged;

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged += OnTurnChanged;
        else if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;

        if (NetworkedEnemyManager.Instance != null)
            NetworkedEnemyManager.Instance.OnEnemyListChanged += UpdateButtons;
        else if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged += UpdateButtons;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady           -= OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged          -= OnRoomChanged;

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
        else if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;

        if (NetworkedEnemyManager.Instance != null)
            NetworkedEnemyManager.Instance.OnEnemyListChanged -= UpdateButtons;
        else if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged -= UpdateButtons;
    }

    private void OnDestroy()
    {
        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
        else if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    private void OnLevelReady()
    {
        // Always prefer the networked generator if it has rooms.
        // Only fall back to the SP generator if no networked one exists.
        mpLevelGenerator = FindFirstObjectByType<NetworkedLevelGenerator>();

        if (mpLevelGenerator == null)
        {
            // Single-player: use LevelGenerator only if it has rooms built
            LevelGenerator found = FindFirstObjectByType<LevelGenerator>();
            spLevelGenerator = (found != null && found.GetAllRooms() != null && found.GetAllRooms().Count > 0)
                ? found
                : null;
        }
        else
        {
            // Multiplayer: don't use the SP generator at all even if it exists in the scene
            spLevelGenerator = null;
        }

        UpdateButtons();
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom room) => UpdateButtons();
    private void OnTurnChanged(object sender, System.EventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        if (RoomManager.Instance == null) return;

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

            LevelGenerator.PlacedRoom connected = GetConnectedRoom(currentRoom, dir);

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

    /// <summary>Checks either the networked or single-player enemy manager.</summary>
    private bool AreEnemiesInCurrentRoom(RoomGrid room)
    {
        if (room == null) return false;

        if (NetworkedEnemyManager.Instance != null)
            return NetworkedEnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;

        if (EnemyManager.Instance != null)
            return EnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;

        return false;
    }

    /// <summary>Asks whichever generator is active for the connected room.</summary>
    private LevelGenerator.PlacedRoom GetConnectedRoom(LevelGenerator.PlacedRoom room,
                                                        LevelGenerator.Direction dir)
    {
        // Multiplayer path
        if (mpLevelGenerator != null)
        {
            var allRooms = mpLevelGenerator.GetAllRooms();
            if (allRooms == null) return null;

            foreach (var mpRoom in allRooms)
            {
                if (mpRoom.roomInstance == room.roomInstance)
                {
                    var mpConnected = mpLevelGenerator.GetConnectedRoom(mpRoom, dir);
                    return mpConnected != null
                        ? mpLevelGenerator.ConvertToOldPlacedRoom(mpConnected)
                        : null;
                }
            }
            return null;
        }

        // Single-player path — only call if the generator is valid
        if (spLevelGenerator != null)
        {
            var allRooms = spLevelGenerator.GetAllRooms();
            if (allRooms == null || allRooms.Count == 0) return null;
            return spLevelGenerator.GetConnectedRoom(room, dir);
        }

        return null;
    }

    private void TravelToRoom(LevelGenerator.Direction travelDirection)
    {
        if (RoomManager.Instance == null) return;

        LevelGenerator.PlacedRoom currentRoom = RoomManager.Instance.GetCurrentRoom();
        if (currentRoom == null) return;

        if (AreEnemiesInCurrentRoom(currentRoom.roomGrid))
        {
            Debug.Log("[RoomNavigationUI] Cannot leave — enemies present!");
            return;
        }

        LevelGenerator.PlacedRoom targetRoom = GetConnectedRoom(currentRoom, travelDirection);
        if (targetRoom == null) return;

        // In multiplayer, only move the LOCAL player
        Unit player = FindLocalPlayer();
        if (player == null) return;

        LevelGenerator.Direction entryDirection = GetOppositeDirection(travelDirection);
        GridPosition spawnPos = GetSpawnPosition(targetRoom, entryDirection);

        RoomManager.Instance.SetCurrentRoom(targetRoom);
        LevelGrid.Instance?.SetCurrentRoomGrid(targetRoom.roomGrid);
        player.PlaceInRoom(targetRoom.roomGrid, spawnPos);

        FreeTacticsCameraController.Instance?.FocusOnPlayer();

        UpdateButtons();
    }

    /// <summary>
    /// Finds the Unit owned by the local client.
    /// Falls back to FindFirstObjectByType for single-player.
    /// </summary>
    private Unit FindLocalPlayer()
    {
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = unit.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null)
            {
                if (netObj.IsOwner) return unit;
            }
            else
            {
                return unit; // single-player: no NetworkObject, just use it
            }
        }
        return null;
    }

    private GridPosition GetSpawnPosition(LevelGenerator.PlacedRoom room,
                                          LevelGenerator.Direction entryDirection)
    {
        RoomSpawnPointReader reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();

        if (reader != null && reader.HasSpawnPoint(entryDirection))
        {
            GridPosition sp = reader.GetSpawnPosition(entryDirection, room.roomGrid);
            Debug.Log($"[RoomNavigationUI] Using spawn point: {sp}");
            return sp;
        }

        Debug.LogWarning($"[RoomNavigationUI] No spawn point for {entryDirection} — using center.");
        int centerX = room.roomGrid.GetWidth()  / 2;
        int centerZ = room.roomGrid.GetHeight() / 2;
        return new GridPosition(centerX, centerZ);
    }

    private LevelGenerator.Direction GetOppositeDirection(LevelGenerator.Direction dir)
    {
        switch (dir)
        {
            case LevelGenerator.Direction.North: return LevelGenerator.Direction.South;
            case LevelGenerator.Direction.South: return LevelGenerator.Direction.North;
            case LevelGenerator.Direction.East:  return LevelGenerator.Direction.West;
            case LevelGenerator.Direction.West:  return LevelGenerator.Direction.East;
            default: return LevelGenerator.Direction.North;
        }
    }

    public void ForceUpdateButtons() => UpdateButtons();
}