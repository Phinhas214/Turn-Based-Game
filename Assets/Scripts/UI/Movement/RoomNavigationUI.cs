using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Room navigation UI — per-player in multiplayer.
///
/// KEY FIXES:
///   - UpdateButtons and TravelToRoom now use the LOCAL player's current room
///     (from NetworkedUnit.GetCurrentRoomGrid) instead of the shared RoomManager.
///     This means each player sees buttons for THEIR room, not whoever last
///     updated the global RoomManager.
///   - Enemy blocking checks the LOCAL player's room for enemies.
///   - Players can only leave if there are no enemies in THEIR room.
/// </summary>
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
    [SerializeField] private GameObject enemyWarningPanel; // optional — leave unassigned if not used

    private LevelGenerator          spLevelGenerator;
    private NetworkedLevelGenerator  mpLevelGenerator;

    private Dictionary<LevelGenerator.Direction, Button>          buttonMap;
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
        LevelGenerator.OnLevelReady          += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;

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
        LevelGenerator.OnLevelReady          -= OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
        else if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;

        if (NetworkedEnemyManager.Instance != null)
            NetworkedEnemyManager.Instance.OnEnemyListChanged -= UpdateButtons;
        else if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged -= UpdateButtons;
    }

    private void OnLevelReady()
    {
        mpLevelGenerator = FindFirstObjectByType<NetworkedLevelGenerator>();
        if (mpLevelGenerator == null)
        {
            LevelGenerator found = FindFirstObjectByType<LevelGenerator>();
            spLevelGenerator = (found != null && found.GetAllRooms()?.Count > 0) ? found : null;
        }
        else
        {
            spLevelGenerator = null;
        }
        UpdateButtons();
    }

    private void OnTurnChanged(object sender, System.EventArgs e) => UpdateButtons();

    // ─────────────────────────────────────────────────────────────────────
    // UpdateButtons — uses LOCAL player's room
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateButtons()
    {
        // Lazy-init generators in case events fire before OnLevelReady
        if (mpLevelGenerator == null && spLevelGenerator == null)
        {
            mpLevelGenerator = FindFirstObjectByType<NetworkedLevelGenerator>();
            if (mpLevelGenerator == null)
            {
                LevelGenerator found = FindFirstObjectByType<LevelGenerator>();
                spLevelGenerator = (found != null && found.GetAllRooms()?.Count > 0) ? found : null;
            }
        }

        LevelGenerator.PlacedRoom localRoom = GetLocalPlayerRoom();
        if (localRoom == null) return;

        bool enemiesPresent = AreEnemiesInRoom(localRoom.roomGrid);
        // enemyWarningPanel?.SetActive(enemiesPresent); // disabled until minimap is built

        foreach (LevelGenerator.Direction dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            Button btn           = buttonMap[dir];
            TextMeshProUGUI txt  = textMap[dir];
            if (btn == null) continue;

            LevelGenerator.PlacedRoom connected = GetConnectedRoom(localRoom, dir);

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

    // ─────────────────────────────────────────────────────────────────────
    // Travel — uses LOCAL player's room
    // ─────────────────────────────────────────────────────────────────────

    private void TravelToRoom(LevelGenerator.Direction travelDirection)
    {
        LevelGenerator.PlacedRoom localRoom = GetLocalPlayerRoom();
        if (localRoom == null) return;

        if (AreEnemiesInRoom(localRoom.roomGrid))
        {
            Debug.Log("[RoomNavigationUI] Cannot leave — enemies present!");
            return;
        }

        LevelGenerator.PlacedRoom targetRoom = GetConnectedRoom(localRoom, travelDirection);
        if (targetRoom == null) return;

        Unit player = FindLocalPlayerUnit();
        if (player == null) return;

        LevelGenerator.Direction entryDir = GetOppositeDirection(travelDirection);
        GridPosition spawnPos = GetSpawnPosition(targetRoom, entryDir);

        // Update room state — pass local client ID in MP so each player
        // tracks their own room independently
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            ulong localId = Unity.Netcode.NetworkManager.Singleton.LocalClientId;
            RoomManager.Instance?.SetCurrentRoom(targetRoom, localId);
        }
        else
        {
            RoomManager.Instance?.SetCurrentRoom(targetRoom);
        }
        LevelGrid.Instance?.SetCurrentRoomGrid(targetRoom.roomGrid);

        // Place on Unit (SP path)
        player.PlaceInRoom(targetRoom.roomGrid, spawnPos);

        // In MP, also update NetworkedUnit so its currentRoomGrid stays in sync.
        // Unit and NetworkedUnit are separate components with separate room fields.
        // Only done when a network session is active — safe to skip in SP.
        bool isNetworked = Unity.Netcode.NetworkManager.Singleton != null &&
                           Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (isNetworked)
        {
            var netUnit = player.GetComponent<NetworkedUnit>();
            netUnit?.PlaceInRoom(targetRoom.roomGrid, spawnPos);

            // Tell the per-room turn system the player moved so it can clean up
            // the old room's submitted set and unblock that room's enemy phase
            // if all remaining players in the old room have already submitted.
            ulong localId = Unity.Netcode.NetworkManager.Singleton.LocalClientId;
            MultiplayerTurnSystem.Instance?.RequestNotifyRoomChange(
                localId, localRoom.roomGrid, targetRoom.roomGrid);
        }

        FreeTacticsCameraController.Instance?.FocusOnPlayer();

        UpdateButtons();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Local player room — reads from the owned NetworkedUnit
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the PlacedRoom the local player's unit is currently in.
    /// In MP, reads from the owned NetworkedUnit's RoomGrid.
    /// In SP, falls back to RoomManager.
    /// </summary>
    private LevelGenerator.PlacedRoom GetLocalPlayerRoom()
    {
        // Multiplayer: find the RoomGrid the local NetworkedUnit is in,
        // then find which PlacedRoom that RoomGrid belongs to.
        if (mpLevelGenerator != null)
        {
            NetworkedUnit localUnit = FindLocalNetworkedUnit();
            if (localUnit != null)
            {
                RoomGrid unitRoom = localUnit.GetCurrentRoomGrid();
                if (unitRoom != null)
                {
                    foreach (var mpRoom in mpLevelGenerator.GetAllRooms())
                    {
                        if (mpRoom.roomGrid == unitRoom)
                            return mpLevelGenerator.ConvertToOldPlacedRoom(mpRoom);
                    }
                }
            }
        }

        // Single-player: use RoomManager
        return RoomManager.Instance?.GetCurrentRoom();
    }

    private NetworkedUnit FindLocalNetworkedUnit()
    {
        foreach (var unit in FindObjectsByType<NetworkedUnit>(FindObjectsSortMode.None))
        {
            if (unit.IsOwner) return unit;
        }
        return null;
    }

    private Unit FindLocalPlayerUnit()
    {
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = unit.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null) { if (netObj.IsOwner) return unit; }
            else return unit; // single-player
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private bool AreEnemiesInRoom(RoomGrid room)
    {
        if (room == null) return false;

        // MUST use HasEnemiesInRoom (not GetEnemiesInRoom) on clients.
        // GetEnemiesInRoom searches activeEnemies which is only populated on
        // the server — it is always empty on clients, so the combat lock
        // (preventing room navigation while enemies are present) never fired.
        // HasEnemiesInRoom uses a synced count cache that works on all clients.
        if (NetworkedEnemyManager.Instance != null)
            return NetworkedEnemyManager.Instance.HasEnemiesInRoom(room);

        if (EnemyManager.Instance != null)
            return EnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;

        return false;
    }

    private LevelGenerator.PlacedRoom GetConnectedRoom(LevelGenerator.PlacedRoom room,
                                                        LevelGenerator.Direction dir)
    {
        if (mpLevelGenerator != null)
        {
            var allRooms = mpLevelGenerator.GetAllRooms();
            if (allRooms == null) return null;
            foreach (var mpRoom in allRooms)
            {
                if (mpRoom.roomInstance == room.roomInstance)
                {
                    var mpConnected = mpLevelGenerator.GetConnectedRoom(mpRoom, dir);
                    return mpConnected != null ? mpLevelGenerator.ConvertToOldPlacedRoom(mpConnected) : null;
                }
            }
            return null;
        }
        if (spLevelGenerator != null)
        {
            var allRooms = spLevelGenerator.GetAllRooms();
            if (allRooms == null || allRooms.Count == 0) return null;
            return spLevelGenerator.GetConnectedRoom(room, dir);
        }
        return null;
    }

    private GridPosition GetSpawnPosition(LevelGenerator.PlacedRoom room,
                                          LevelGenerator.Direction entryDirection)
    {
        RoomSpawnPointReader reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(entryDirection))
            return reader.GetSpawnPosition(entryDirection, room.roomGrid);

        int cx = room.roomGrid.GetWidth()  / 2;
        int cz = room.roomGrid.GetHeight() / 2;
        return new GridPosition(cx, cz);
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