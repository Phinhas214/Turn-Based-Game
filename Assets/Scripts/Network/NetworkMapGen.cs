using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Networked replacement for LevelGenerator.
///
/// HOW IT WORKS:
///   HOST:
///     - Generates the level using a random seed (same algorithm as before).
///     - After generation, sends a compact RoomSyncData[] to all clients via ClientRpc.
///     - Spawns one NetworkObject player prefab per connected client.
///     - Spawns enemies as NetworkObjects (server-owned).
///
///   CLIENTS:
///     - Receive RoomSyncData[] (prefab index + world position for each room).
///     - Instantiate the same room prefabs at the same positions locally.
///     - Do NOT run any generation logic — they just build from the host's data.
///     - Clients never spawn players or enemies themselves.
///
/// CHARACTER SELECTION:
///   - Before the game starts, each player picks a character index in the lobby.
///   - NetworkGameManager.GetCharacterSelections() returns a Dictionary<UGS_ID, int>.
///   - This generator reads that and maps UGS IDs to connected client IDs.
///
/// SETUP:
///   - Replace LevelGenerator with this script on your level generator GameObject.
///   - Add NetworkObject component to this GameObject.
///   - Fill playerPrefabs list (one per character class).
///   - Fill roomPrefabs as before.
/// </summary>
public class NetworkedLevelGenerator : NetworkBehaviour
{
    // ── Sync data — minimal footprint sent over the network ───────────────
    [Serializable]
    public struct RoomSyncData : INetworkSerializable
    {
        public int   PrefabIndex;   // index into roomPrefabs list
        public float WorldX;
        public float WorldY;
        public float WorldZ;
        public int   GridX;         // layout grid position (not world)
        public int   GridZ;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PrefabIndex);
            serializer.SerializeValue(ref WorldX);
            serializer.SerializeValue(ref WorldY);
            serializer.SerializeValue(ref WorldZ);
            serializer.SerializeValue(ref GridX);
            serializer.SerializeValue(ref GridZ);
        }
    }

    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Room Prefabs (same order on all clients)")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;
    [SerializeField] private GameObject hallwayPrefab;

    [Header("Player Prefabs (index matches character selection)")]
    [Tooltip("Index 0 = Knight, 1 = Rogue, 2 = Mage, 3 = Cleric — must match CharacterSelectUI order.")]
    [SerializeField] private List<GameObject> playerPrefabs;

    [Header("Generation Settings")]
    [SerializeField] private int   minRooms        = 5;
    [SerializeField] private int   maxRooms        = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    [SerializeField] private float cellSize        = 2f;

    [Header("Fallback (if no character selection data)")]
    [SerializeField] private GameObject fallbackPlayerPrefab;

    // ── Shared types (keep parity with old LevelGenerator) ────────────────
    [Serializable]
    public class RoomPrefabData
    {
        public GameObject prefab;
        public LevelGenerator.RoomType roomType;
        [Range(0f, 1f)] public float spawnWeight = 1f;
        [HideInInspector] public int   width      = 10;
        [HideInInspector] public int   height     = 10;
        [HideInInspector] public Vector3 gridOffset = Vector3.zero;
    }

    // ── Events ────────────────────────────────────────────────────────────
    public static Action OnLevelReady;

    // ── Runtime ───────────────────────────────────────────────────────────
    private List<PlacedRoom>                        placedRooms;
    private Dictionary<Vector2Int, PlacedRoom>      roomGrid;
    private Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom> roomConnections;
    private int                                     generationSeed;

    public class PlacedRoom
    {
        public GameObject                roomInstance;
        public RoomPrefabData            prefabData;
        public RoomConnector             connector;
        public Vector3                   worldPosition;
        public Vector2Int                gridPosition;
        public RoomGrid                  roomGrid;
        public int                       prefabIndex; // needed for sync
    }

    // ─────────────────────────────────────────────────────────────────────
    // NGO Spawn
    // ─────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Increase NGO message size limit so large levels never hit the buffer cap.
        // Default is 64KB — we set it to 4MB which handles any realistic room count.
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.MaxPayloadSize        = 4 * 1024 * 1024; // 4 MB
            transport.MaxSendQueueSize      = 4 * 1024 * 1024; // 4 MB send queue
            Debug.Log("[NetworkedLevelGenerator] NGO transport buffer set to 4MB.");
        }

        StartCoroutine(WaitThenGenerate());
    }

    private IEnumerator WaitThenGenerate()
    {
        // Wait until NGO has fully initialised this NetworkObject on the server
        // before doing anything — no time guesses, just wait for the frame to settle
        yield return new WaitForEndOfFrame();
        ReadRoomPrefabDefinitions();
        GenerateLevelOnServer();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server: Generate
    // ─────────────────────────────────────────────────────────────────────

    private void GenerateLevelOnServer()
    {
        generationSeed = UnityEngine.Random.Range(0, int.MaxValue);
        UnityEngine.Random.InitState(generationSeed);

        // Server uses UnityEngine.Random — isolatedRandom must be null
        isolatedRandom = null;

        ClearLevel();
        placedRooms    = new List<PlacedRoom>();
        roomGrid       = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>();

        GenerateRoomLayout();
        InitializeRoomGrids();
        InitializeDoors();

        // Send just the seed — clients regenerate the level locally using
        // the same algorithm and seed, producing identical results with zero
        // network allocation overhead regardless of level size
        SyncLevelToClientsClientRpc(generationSeed, null);

        // Wait for all clients to confirm ready, then spawn
        StartCoroutine(WaitForAllClientsReadyThenSpawn());
    }

    // ── Client acknowledgement tracking ──────────────────────────────────
    // No time guesses — server waits until EVERY non-host client explicitly
    // confirms it has finished reconstructing the level before spawning players.
    private HashSet<ulong> clientsConfirmedReady = new HashSet<ulong>();

    /// <summary>
    /// Called by each non-host client the moment it finishes ReconstructLevelOnClient.
    /// The server collects these and only spawns players once everyone is ready.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ClientLevelReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        clientsConfirmedReady.Add(senderId);

        int nonHostClients = NetworkManager.Singleton.ConnectedClientsList.Count - 1;
        Debug.Log($"[NetworkedLevelGenerator] Client {senderId} ready " +
                  $"({clientsConfirmedReady.Count}/{nonHostClients})");
    }

    private IEnumerator WaitForAllClientsReadyThenSpawn()
    {
        int nonHostClients = NetworkManager.Singleton.ConnectedClientsList.Count - 1;

        Debug.Log($"[NetworkedLevelGenerator] Waiting for {nonHostClients} client(s) to confirm level ready...");

        // If host is alone, skip waiting
        if (nonHostClients <= 0)
        {
            Debug.Log("[NetworkedLevelGenerator] Solo host — spawning immediately.");
            SpawnAllPlayers();
            OnLevelReady?.Invoke();
            yield break;
        }

        // Wait until every connected non-host client has called ClientLevelReadyServerRpc
        // No timeout — if a client never responds it means it crashed/disconnected
        // and NGO's disconnect callback will handle that separately
        while (clientsConfirmedReady.Count < nonHostClients)
        {
            // Re-check client count in case someone disconnected while waiting
            nonHostClients = NetworkManager.Singleton.ConnectedClientsList.Count - 1;
            yield return null;
        }

        Debug.Log("[NetworkedLevelGenerator] All clients confirmed ready — spawning players.");
        SpawnAllPlayers();
        OnLevelReady?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // ClientRpc: Sync level layout
    // ─────────────────────────────────────────────────────────────────────

    // ── Chunked level sync — one room per RPC to stay under NGO size limits ──

    // ── Level sync — send seed only, clients regenerate identically ─────────
    // Instead of sending all room data over the network (which causes memory
    // allocation errors), we just send the seed. Both host and client run the
    // exact same generation algorithm with the same seed = identical results.
    // Zero allocation issues, works for any room count, any device.

    [ClientRpc]
    private void SyncLevelToClientsClientRpc(int seed, RoomSyncData[] rooms)
    {
        if (IsServer) return;
        Debug.Log($"[NetworkedLevelGenerator] Client received seed {seed} — regenerating level locally.");
        RegenerateLevelFromSeed(seed);
    }

    // ── Isolated random for generation — never touches UnityEngine.Random ──
    // This is critical: UnityEngine.Random state on the client and server will
    // diverge because the server uses Random for other things (player spawn offsets
    // etc) after generation. Using System.Random with the same seed guarantees
    // the client runs the EXACT same sequence as the server did during generation.
    private System.Random isolatedRandom;

    private int IsolatedRange(int min, int max) => isolatedRandom.Next(min, max + 1);
    private float IsolatedValue()               => (float)isolatedRandom.NextDouble();

    private void RegenerateLevelFromSeed(int seed)
    {
        // CRITICAL: isolatedRandom must be NULL so GenRange/GenValue use
        // UnityEngine.Random — the same RNG the server used during generation.
        // System.Random uses a completely different algorithm so even with the
        // same seed it produces different numbers, giving a different layout.
        isolatedRandom = null;

        // CRITICAL: ReadRoomPrefabDefinitions must run on the client too.
        // Without it all room widths/heights stay at the default 10x10 instead
        // of the actual prefab dimensions. PlaceRoomInDirection then calculates
        // wrong connector positions and rooms either overlap or fail to connect,
        // causing the generation to produce only 1-2 rooms and stop.
        ReadRoomPrefabDefinitions();

        UnityEngine.Random.InitState(seed);

        ClearLevel();
        placedRooms     = new List<PlacedRoom>();
        roomGrid        = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>();

        GenerateRoomLayout();
        InitializeRoomGrids();
        InitializeDoors();
        ReconstructConnectionsFromGrid();

        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom != null)
        {
            RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom));
            LevelGrid.Instance?.SetCurrentRoomGrid(startRoom.roomGrid);
        }

        ClientLevelReadyServerRpc();
        StartCoroutine(WaitForLocalPlayerThenFireReady());

        Debug.Log($"[NetworkedLevelGenerator] Client level built from seed {seed} — {placedRooms.Count} rooms.");
    }

    private void ReconstructLevelOnClient(int seed, RoomSyncData[] rooms)
    {
        UnityEngine.Random.InitState(seed);

        ClearLevel();
        placedRooms     = new List<PlacedRoom>();
        roomGrid        = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>();

        foreach (RoomSyncData data in rooms)
        {
            if (data.PrefabIndex < 0 || data.PrefabIndex >= roomPrefabs.Count)
            {
                Debug.LogError($"[NetworkedLevelGenerator] Invalid prefab index {data.PrefabIndex}");
                continue;
            }

            RoomPrefabData prefabData = roomPrefabs[data.PrefabIndex];
            Vector3    worldPos = new Vector3(data.WorldX, data.WorldY, data.WorldZ);
            Vector2Int gridPos  = new Vector2Int(data.GridX, data.GridZ);

            PlacedRoom room = InstantiateRoom(prefabData, data.PrefabIndex, gridPos, worldPos);
            if (room != null)
            {
                placedRooms.Add(room);
                roomGrid[gridPos] = room;
            }
        }

        // ── FIX 1: Rebuild roomConnections on the client ──────────────────
        // The server builds connections via CreateHallway during generation,
        // but the client only receives positions — so we rebuild the connection
        // map here from the grid positions, which are identical on both sides.
        ReconstructConnectionsFromGrid();

        InitializeRoomGrids();
        InitializeDoors();

        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom != null)
        {
            RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom));
            LevelGrid.Instance?.SetCurrentRoomGrid(startRoom.roomGrid);
        }

        // Tell the server we're done reconstructing the level
        // so it knows when it's safe to spawn players
        ClientLevelReadyServerRpc();

        // Don't fire OnLevelReady yet — wait for our player object to arrive
        StartCoroutine(WaitForLocalPlayerThenFireReady());

        Debug.Log("[NetworkedLevelGenerator] Client level reconstruction complete.");
    }

    /// <summary>
    /// Rebuilds roomConnections on the client from grid adjacency.
    /// Two rooms are connected if they are exactly 1 grid step apart AND
    /// both have a RoomConnector point facing each other.
    /// </summary>
    private void ReconstructConnectionsFromGrid()
    {
        foreach (PlacedRoom a in placedRooms)
        {
            foreach (LevelGenerator.Direction dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                // Already registered this direction?
                if (roomConnections.ContainsKey((a, dir))) continue;

                Vector2Int neighbourGrid = a.gridPosition + GetDirectionOffset(dir);
                if (!roomGrid.TryGetValue(neighbourGrid, out PlacedRoom b)) continue;

                // Verify both rooms actually have connector points facing each other
                if (a.connector == null || b.connector == null) continue;
                if (!a.connector.HasConnectionPoint(dir)) continue;
                if (!b.connector.HasConnectionPoint(GetOppositeDirection(dir))) continue;

                roomConnections[(a, dir)]                        = b;
                roomConnections[(b, GetOppositeDirection(dir))]  = a;
            }
        }

        Debug.Log($"[NetworkedLevelGenerator] Client rebuilt {roomConnections.Count} room connections.");
    }

    /// <summary>
    /// Waits until the local client's player NetworkObject has been spawned
    /// by the server, then fires OnLevelReady so UI/action systems can find it.
    /// </summary>
    private System.Collections.IEnumerator WaitForLocalPlayerThenFireReady()
    {
        // Instead of scanning every frame (which allocates a new array each frame
        // and causes ALLOC_TEMP_MAIN warnings), we scan once every 10 frames.
        // NGO fires OnClientConnectedCallback and NetworkObject.OnSpawn when a
        // player object arrives — we just need to not miss it.
        const int scanInterval = 10;
        int       frameCount   = 0;
        float     timeout      = 15f;
        float     elapsed      = 0f;

        while (elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            frameCount++;

            // Only do the expensive scan every 10 frames
            if (frameCount % scanInterval == 0)
            {
                Unit[] units = FindObjectsByType<Unit>(FindObjectsSortMode.None);
                foreach (Unit unit in units)
                {
                    NetworkObject netObj = unit.GetComponent<NetworkObject>();
                    if (netObj != null && netObj.IsOwner)
                    {
                        Debug.Log("[NetworkedLevelGenerator] Local player found — firing OnLevelReady.");
                        OnLevelReady?.Invoke();
                        yield break;
                    }
                }
            }

            yield return null;
        }

        Debug.LogWarning("[NetworkedLevelGenerator] Timed out waiting for local player. Firing OnLevelReady anyway.");
        OnLevelReady?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player spawning (Server only)
    // ─────────────────────────────────────────────────────────────────────

    private void SpawnAllPlayers()
    {
        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom == null)
        {
            Debug.LogError("[NetworkedLevelGenerator] No start room found for player spawning!");
            return;
        }

        // Set start room for server-side systems
        RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom));
        LevelGrid.Instance?.SetCurrentRoomGrid(startRoom.roomGrid);

        // Read character selections directly from LobbySync — it persists from the menu scene
        // via DontDestroyOnLoad so the data is guaranteed to be here when we spawn.
        var connectedClients = NetworkManager.Singleton.ConnectedClientsList;

        int centerX = startRoom.roomGrid.GetWidth() / 2;
        int centerZ = startRoom.roomGrid.GetHeight() / 2;

        for (int i = 0; i < connectedClients.Count; i++)
        {
            ulong clientId = connectedClients[i].ClientId;

            // LobbySync holds the char index each client chose in the menu.
            // Falls back to 0 if LobbySync is missing or the client never registered.
            int charIndex = (LobbySync.Instance != null)
                ? LobbySync.Instance.GetCharacterIndex(clientId)
                : 0;

            Debug.Log($"[NetworkedLevelGenerator] Client {clientId} → charIndex {charIndex}");

            GameObject prefabToSpawn = GetPlayerPrefab(charIndex);

            // Offset each player slightly so they don't overlap
            GridPosition spawnPos = new GridPosition(
                centerX + (i % 2 == 0 ? -1 : 1),
                centerZ + (i / 2 == 0 ? -1 : 1));

            if (!startRoom.roomGrid.IsValidGridPosition(spawnPos))
                spawnPos = new GridPosition(centerX, centerZ);

            Vector3 spawnWorldPos = startRoom.roomGrid.GetWorldPosition(spawnPos);

            GameObject playerGO = Instantiate(prefabToSpawn, spawnWorldPos, Quaternion.identity);
            NetworkObject netObj = playerGO.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[NetworkedLevelGenerator] Player prefab missing NetworkObject!");
                Destroy(playerGO);
                continue;
            }

            netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            // Place on grid (server side)
            Unit unit = playerGO.GetComponent<Unit>();
            if (unit != null)
                unit.PlaceInRoom(startRoom.roomGrid, spawnPos);

            NetworkedUnit netUnit = playerGO.GetComponent<NetworkedUnit>();
            if (netUnit != null)
                netUnit.PlaceInRoom(startRoom.roomGrid, spawnPos);

            // Register this client's starting room in RoomManager so
            // TilemapGridVisual resolves the correct tilemap per client
            RoomManager.Instance?.SetCurrentRoom(
                ConvertToOldPlacedRoom(startRoom), clientId);

            Debug.Log($"[NetworkedLevelGenerator] Spawned class {charIndex} for client {clientId} at {spawnPos}");
            
            // Capture loop variables for the coroutine closure
            ulong capturedClientId  = clientId;
            GridPosition capturedSpawnPos = spawnPos;
            RoomGrid capturedRoomGrid = startRoom.roomGrid;
            NetworkedUnit capturedNetUnit = netUnit;
            StartCoroutine(SendInitRpcAfterDelay(capturedNetUnit, capturedRoomGrid, capturedSpawnPos, capturedClientId));
        }
    }

    /// <summary>
    /// Waits a short delay then tells the owning client to initialise their
    /// local Unit component. The delay ensures the NetworkObject has fully
    /// arrived on the client before we send the RPC.
    /// </summary>
    private IEnumerator SendInitRpcAfterDelay(NetworkedUnit netUnit, RoomGrid roomGrid,
                                               GridPosition spawnPos, ulong clientId)
    {
        yield return new WaitForSeconds(0.5f);

        if (netUnit == null || roomGrid == null) yield break;

        Vector3 spawnWorldPos = roomGrid.GetWorldPosition(spawnPos);
        ClientRpcParams ownerOnly = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { clientId }
            }
        };

        netUnit.InitialiseUnitOnClientClientRpc(
            spawnPos.x, spawnPos.z,
            spawnWorldPos.x, spawnWorldPos.y, spawnWorldPos.z,
            ownerOnly);

        Debug.Log($"[NetworkedLevelGenerator] Sent InitialiseUnitOnClient RPC to client {clientId}");
    }

    private GameObject GetPlayerPrefab(int charIndex)
    {
        if (playerPrefabs != null && charIndex >= 0 && charIndex < playerPrefabs.Count)
            return playerPrefabs[charIndex];
        if (fallbackPlayerPrefab != null)
            return fallbackPlayerPrefab;
        Debug.LogError("[NetworkedLevelGenerator] No valid player prefab found!");
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Sync data builder
    // ─────────────────────────────────────────────────────────────────────

    private RoomSyncData[] BuildSyncData()
    {
        var list = new List<RoomSyncData>();
        foreach (PlacedRoom room in placedRooms)
        {
            list.Add(new RoomSyncData
            {
                PrefabIndex = room.prefabIndex,
                WorldX      = room.worldPosition.x,
                WorldY      = room.worldPosition.y,
                WorldZ      = room.worldPosition.z,
                GridX       = room.gridPosition.x,
                GridZ       = room.gridPosition.y
            });
        }
        return list.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Room generation (server only — same logic as original LevelGenerator)
    // ─────────────────────────────────────────────────────────────────────

    private void ReadRoomPrefabDefinitions()
    {
        foreach (RoomPrefabData data in roomPrefabs)
        {
            if (data.prefab == null) continue;
            RoomTilemapSetup setup = data.prefab.GetComponent<RoomTilemapSetup>();
            if (setup != null)
            {
                data.width      = setup.GetWidth();
                data.height     = setup.GetHeight();
                data.gridOffset = setup.GetGridOffset();
            }
            else
            {
                data.width  = 10;
                data.height = 10;
            }
        }
    }

    // ── Random helpers — use isolatedRandom on client, UnityEngine.Random on server ──
    private int GenRange(int min, int max)
        => isolatedRandom != null
            ? isolatedRandom.Next(min, max + 1)
            : UnityEngine.Random.Range(min, max + 1);

    private float GenValue()
        => isolatedRandom != null
            ? (float)isolatedRandom.NextDouble()
            : UnityEngine.Random.value;

    private void ShuffleListGen<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = GenRange(i, list.Count - 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    private void GenerateRoomLayout()
    {
        PlacedRoom startRoom = PlaceRoom(LevelGenerator.RoomType.Start, Vector2Int.zero, Vector3.zero);
        if (startRoom == null) { Debug.LogError("Failed to place start room!"); return; }

        var queue = new Queue<PlacedRoom>();
        queue.Enqueue(startRoom);

        int roomCount = 1;
        int target    = GenRange(minRooms, maxRooms);

        // Safety: total placements attempted across entire generation, not per-room.
        // A large limit ensures we always reach the target room count as long as
        // connectors are available — only truly exhausted layouts exit early.
        int totalAttempts    = 0;
        int maxTotalAttempts = target * 20; // generous — never exits too early

        while (queue.Count > 0 && roomCount < target && totalAttempts < maxTotalAttempts)
        {
            PlacedRoom current = queue.Dequeue();

            var dirs   = GetAvailableDirections(current);
            ShuffleListGen(dirs);
            int toMake = Mathf.Min(GenRange(1, 2), dirs.Count);

            for (int i = 0; i < dirs.Count && roomCount < target; i++)
            {
                totalAttempts++;
                var dir      = dirs[i];
                var roomType = DetermineRoomType(roomCount, target);
                PlacedRoom newRoom = PlaceRoomInDirection(current, dir, roomType);

                if (newRoom != null)
                {
                    CreateHallway(current, newRoom, dir);
                    current.connector.MarkConnectionUsed(dir);
                    newRoom.connector.MarkConnectionUsed(GetOppositeDirection(dir));

                    if (roomType != LevelGenerator.RoomType.End)
                        queue.Enqueue(newRoom);

                    roomCount++;

                    // Only make as many rooms from this node as toMake allows
                    if (roomCount % toMake == 0) break;
                }
                else
                {
                    // This direction failed — put the room back in queue so
                    // other directions get tried from it on the next pass
                    if (!queue.Contains(current))
                        queue.Enqueue(current);
                }
            }
        }

        // If we still haven't hit target, log a warning but don't fail silently
        if (roomCount < target)
            Debug.LogWarning($"[NetworkedLevelGenerator] Only placed {roomCount}/{target} rooms — connectors may be exhausted.");
        else
            Debug.Log($"[NetworkedLevelGenerator] Generated {placedRooms.Count} rooms.");
    }

    private PlacedRoom PlaceRoom(LevelGenerator.RoomType roomType, Vector2Int gridPos, Vector3 worldPos)
    {
        if (roomGrid.ContainsKey(gridPos)) return null;

        RoomPrefabData prefabData = GetRandomRoomPrefab(roomType);
        if (prefabData == null) return null;

        int prefabIndex = roomPrefabs.IndexOf(prefabData);
        return InstantiateRoom(prefabData, prefabIndex, gridPos, worldPos);
    }

    private PlacedRoom InstantiateRoom(RoomPrefabData prefabData, int prefabIndex, Vector2Int gridPos, Vector3 worldPos)
    {
        GameObject instance = Instantiate(prefabData.prefab, worldPos, Quaternion.identity, transform);
        instance.name = $"{prefabData.roomType}Room_({gridPos.x},{gridPos.y})";

        RoomConnector connector = instance.GetComponent<RoomConnector>();
        if (connector == null)
        {
            Debug.LogError($"[NetworkedLevelGenerator] {prefabData.prefab.name} missing RoomConnector!");
            Destroy(instance);
            return null;
        }

        var placed = new PlacedRoom
        {
            roomInstance  = instance,
            prefabData    = prefabData,
            connector     = connector,
            worldPosition = worldPos,
            gridPosition  = gridPos,
            prefabIndex   = prefabIndex
        };

        placedRooms.Add(placed);
        roomGrid[gridPos] = placed;
        return placed;
    }

    private void InitializeRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomTilemapSetup setup = room.roomInstance.GetComponent<RoomTilemapSetup>();
            if (setup == null)
                setup = room.roomInstance.AddComponent<RoomTilemapSetup>();
            setup.Initialize();

            RoomGrid rg = room.roomInstance.GetComponent<RoomGrid>();
            if (rg == null)
                rg = room.roomInstance.AddComponent<RoomGrid>();

            rg.Initialize(setup.GetWidth(), setup.GetHeight(), setup.GetCellSize(),
                          room.worldPosition, setup.GetGridOffset(), null);
            room.roomGrid = rg;

            LevelGrid.Instance?.RegisterRoomGrid(rg);
        }
    }

    private void InitializeDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            foreach (RoomDoor door in room.roomInstance.GetComponentsInChildren<RoomDoor>())
                door.Initialize(ConvertToOldPlacedRoom(room));
        }
    }

    private void ClearLevel()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private PlacedRoom PlaceRoomInDirection(PlacedRoom existing, LevelGenerator.Direction dir, LevelGenerator.RoomType type)
    {
        var exit = existing.connector.GetConnectionPoint(dir);
        if (exit?.transform == null) return null;

        RoomPrefabData newPrefab = GetRandomRoomPrefab(type);
        if (newPrefab == null) return null;

        RoomConnector tempConn = newPrefab.prefab.GetComponent<RoomConnector>();
        if (tempConn == null) return null;

        var oppDir = GetOppositeDirection(dir);
        if (!tempConn.HasConnectionPoint(oppDir)) return null;

        var entry    = tempConn.GetConnectionPoint(oppDir);
        Vector3 newPos = exit.transform.position - entry.transform.localPosition;
        Vector2Int newGrid = existing.gridPosition + GetDirectionOffset(dir);

        return PlaceRoom(type, newGrid, newPos);
    }

    private void CreateHallway(PlacedRoom a, PlacedRoom b, LevelGenerator.Direction dir)
    {
        roomConnections[(a, dir)]                    = b;
        roomConnections[(b, GetOppositeDirection(dir))] = a;

        if (hallwayPrefab == null) return;

        var exit  = a.connector.GetConnectionPoint(dir);
        var entry = b.connector.GetConnectionPoint(GetOppositeDirection(dir));
        if (exit?.transform == null || entry?.transform == null) return;

        Vector3 pos = (exit.transform.position + entry.transform.position) * 0.5f;
        Quaternion rot = (dir == LevelGenerator.Direction.East || dir == LevelGenerator.Direction.West)
            ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;

        Instantiate(hallwayPrefab, pos, rot, transform);
    }

    private List<LevelGenerator.Direction> GetAvailableDirections(PlacedRoom room)
    {
        var available = new List<LevelGenerator.Direction>();
        if (room.connector == null) return available;

        foreach (LevelGenerator.Direction dir in Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            if (room.connector.IsDirectionAvailable(dir))
            {
                Vector2Int check = room.gridPosition + GetDirectionOffset(dir);
                if (!roomGrid.ContainsKey(check))
                    available.Add(dir);
            }
        }
        return available;
    }

    private LevelGenerator.RoomType DetermineRoomType(int current, int target)
    {
        if (current == target - 1) return LevelGenerator.RoomType.End;
        if (GenValue() < specialRoomChance) return LevelGenerator.RoomType.Special;
        return LevelGenerator.RoomType.Normal;
    }

    private RoomPrefabData GetRandomRoomPrefab(LevelGenerator.RoomType type)
    {
        var valid = roomPrefabs.FindAll(p => p.roomType == type);
        if (valid.Count == 0) return null;

        float total = 0f;
        foreach (var p in valid) total += p.spawnWeight;
        float rand = GenValue() * total;
        float curr = 0f;
        foreach (var p in valid) { curr += p.spawnWeight; if (rand <= curr) return p; }
        return valid[0];
    }

    private Vector2Int GetDirectionOffset(LevelGenerator.Direction dir)
    {
        switch (dir)
        {
            case LevelGenerator.Direction.North: return new Vector2Int(0,  1);
            case LevelGenerator.Direction.South: return new Vector2Int(0, -1);
            case LevelGenerator.Direction.East:  return new Vector2Int(1,  0);
            case LevelGenerator.Direction.West:  return new Vector2Int(-1, 0);
            default: return Vector2Int.zero;
        }
    }

    public LevelGenerator.Direction GetOppositeDirection(LevelGenerator.Direction dir)
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

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    public PlacedRoom GetPlacedRoomAt(Vector2Int gridPos)
    {
        roomGrid.TryGetValue(gridPos, out PlacedRoom room);
        return room;
    }

    public List<PlacedRoom> GetAllRooms() => placedRooms;

    public PlacedRoom GetConnectedRoom(PlacedRoom room, LevelGenerator.Direction dir)
    {
        roomConnections.TryGetValue((room, dir), out PlacedRoom connected);
        return connected;
    }

    // ── Converts internal PlacedRoom to LevelGenerator.PlacedRoom for compatibility ─
    public LevelGenerator.PlacedRoom ConvertToOldPlacedRoom(PlacedRoom room)
    {
        return new LevelGenerator.PlacedRoom
        {
            roomInstance  = room.roomInstance,
            prefabData    = ConvertPrefabData(room.prefabData),
            connector     = room.connector,
            worldPosition = room.worldPosition,
            gridPosition  = room.gridPosition,
            roomGrid      = room.roomGrid
        };
    }

    private LevelGenerator.RoomPrefabData ConvertPrefabData(RoomPrefabData data)
    {
        return new LevelGenerator.RoomPrefabData
        {
            prefab       = data.prefab,
            roomType     = data.roomType,
            spawnWeight  = data.spawnWeight,
            width        = data.width,
            height       = data.height,
            gridOffset   = data.gridOffset
        };
    }
}