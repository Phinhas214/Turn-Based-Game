using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomTilemapSetup : MonoBehaviour
{
    [Header("Grid Configuration")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 10;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector3 gridOffset = Vector3.zero;

    private Grid tilemapGrid;
    private Tilemap wallsTilemap;
    private Tilemap floorTilemap;
    private TilemapRoomGrid roomGrid;
    private RoomSpawnPointReader spawnPointReader;
    private bool isInitialized = false;

    public void Initialize()
    {
        if (isInitialized) return;

        // ── Grid ──────────────────────────────────────────────────────────
        tilemapGrid = GetComponent<Grid>();
        if (tilemapGrid == null)
            tilemapGrid = gameObject.AddComponent<Grid>();
        tilemapGrid.cellSize = new Vector3(cellSize, cellSize, 0);

        // ── Find tilemaps by name ──────────────────────────────────────────
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
        {
            switch (tm.gameObject.name)
            {
                case "Walls": wallsTilemap = tm; break;
                case "Floor": floorTilemap = tm; break;
            }
        }

        if (wallsTilemap == null) wallsTilemap = CreateTilemap("Walls",  0);
        if (floorTilemap == null) floorTilemap = CreateTilemap("Floor", -1);

        // ── NO 2D physics colliders — this is a 3D game ───────────────────
        // Wall blocking is handled by IsWallAtPosition() in TilemapRoomGrid
        // If you need physical collision use 3D box colliders on wall objects

        // ── TilemapRoomGrid ───────────────────────────────────────────────
        roomGrid = GetComponent<TilemapRoomGrid>();
        if (roomGrid == null)
            roomGrid = gameObject.AddComponent<TilemapRoomGrid>();
        roomGrid.Initialize(wallsTilemap, floorTilemap);

        // ── SpawnPoint Reader ─────────────────────────────────────────────
        spawnPointReader = GetComponent<RoomSpawnPointReader>();
        if (spawnPointReader == null)
            spawnPointReader = gameObject.AddComponent<RoomSpawnPointReader>();
        spawnPointReader.Initialize();

        isInitialized = true;
        Debug.Log($"[RoomTilemapSetup] ✅ {gameObject.name} initialized.");
    }

    private Tilemap CreateTilemap(string layerName, int sortingOrder)
    {
        GameObject go = new GameObject(layerName);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        Tilemap tm = go.AddComponent<Tilemap>();
        TilemapRenderer rend = go.AddComponent<TilemapRenderer>();
        rend.sortingOrder = sortingOrder;
        return tm;
    }

    public RoomSpawnPointReader GetSpawnPointReader() => spawnPointReader;
    public TilemapRoomGrid GetRoomGrid()   => roomGrid;
    public Tilemap GetWallsTilemap()       => wallsTilemap;
    public Tilemap GetFloorTilemap()       => floorTilemap;
    public int GetWidth()                  => width;
    public int GetHeight()                 => height;
    public float GetCellSize()             => cellSize;
    public Vector3 GetGridOffset()         => gridOffset;
    public bool IsInitialized()            => isInitialized;
}