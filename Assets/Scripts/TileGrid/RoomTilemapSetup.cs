// RoomTilemapSetup.cs — replace your existing file
using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomTilemapSetup : MonoBehaviour
{
    [Header("Grid Configuration")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 10;
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private Vector3 gridOffset = new Vector3(0, 0.1f, 0);

    [Header("Optional Tile Assets")]
    [SerializeField] private Tile wallTilePrefab;
    [SerializeField] private Tile floorTilePrefab;

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
        tilemapGrid.cellSize = new Vector3(cellSize, cellSize, cellSize);

        // ── Find tilemaps ─────────────────────────────────────────────────
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
        {
            switch (tm.gameObject.name)
            {
                case "Walls":  wallsTilemap  = tm; break;
                case "Floor":  floorTilemap  = tm; break;
            }
        }

        if (wallsTilemap == null) wallsTilemap = CreateTilemap("Walls",  0);
        if (floorTilemap == null) floorTilemap = CreateTilemap("Floor", -1);

        // ── Colliders ─────────────────────────────────────────────────────
        SetupColliders();

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
        go.transform.parent = transform;
        go.transform.localPosition = gridOffset;
        Tilemap tm = go.AddComponent<Tilemap>();
        TilemapRenderer rend = go.AddComponent<TilemapRenderer>();
        rend.sortingOrder = sortingOrder;
        return tm;
    }

    private void SetupColliders()
    {
        if (wallsTilemap == null) return;

        TilemapCollider2D col = wallsTilemap.GetComponent<TilemapCollider2D>();
        if (col == null) col = wallsTilemap.gameObject.AddComponent<TilemapCollider2D>();

        CompositeCollider2D comp = wallsTilemap.GetComponent<CompositeCollider2D>();
        if (comp == null)
        {
            comp = wallsTilemap.gameObject.AddComponent<CompositeCollider2D>();
            comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
        }

        col.compositeOperation = Collider2D.CompositeOperation.Merge;
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