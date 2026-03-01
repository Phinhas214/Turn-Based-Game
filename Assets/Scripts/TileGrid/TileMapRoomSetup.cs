using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Initializes tilemap structure for a room prefab.
/// If Grid/Tilemaps don't exist, creates them.
/// If they DO exist (pre-painted), finds them and initializes.
/// </summary>
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

    [Header("Debug")]
    [SerializeField] private bool createDebugBorder = false;

    // Runtime references
    private Grid tilemapGrid;
    private Tilemap wallsTilemap;
    private Tilemap floorTilemap;
    private TilemapRoomGrid roomGrid;
    private bool isInitialized = false;

    public void Initialize()
    {
        if (isInitialized) return;

        Debug.Log($"[RoomTilemapSetup] Starting initialization of {gameObject.name}");

        // STEP 1: Find or create Grid
        tilemapGrid = GetComponent<Grid>();
        if (tilemapGrid == null)
        {
            Debug.Log($"[RoomTilemapSetup] No Grid found, creating one...");
            tilemapGrid = gameObject.AddComponent<Grid>();
        }
        else
        {
            Debug.Log($"[RoomTilemapSetup] Found existing Grid");
        }

        tilemapGrid.cellSize = new Vector3(cellSize, cellSize, cellSize);

        // STEP 2: Find Walls and Floor tilemaps (already in prefab)
        Tilemap[] allTilemaps = GetComponentsInChildren<Tilemap>();
        
        foreach (Tilemap tm in allTilemaps)
        {
            if (tm.gameObject.name == "Walls")
                wallsTilemap = tm;
            else if (tm.gameObject.name == "Floor")
                floorTilemap = tm;
        }

        // If they don't exist, create them
        if (wallsTilemap == null)
        {
            Debug.Log($"[RoomTilemapSetup] No Walls tilemap found, creating...");
            wallsTilemap = CreateTilemap("Walls", 0);
        }
        else
        {
            Debug.Log($"[RoomTilemapSetup] Found Walls tilemap");
        }

        if (floorTilemap == null)
        {
            Debug.Log($"[RoomTilemapSetup] No Floor tilemap found, creating...");
            floorTilemap = CreateTilemap("Floor", -1);
        }
        else
        {
            Debug.Log($"[RoomTilemapSetup] Found Floor tilemap");
        }

        // STEP 3: Setup colliders on Walls
        SetupColliders();

        // STEP 4: Create or find TilemapRoomGrid
        roomGrid = GetComponent<TilemapRoomGrid>();
        if (roomGrid == null)
        {
            Debug.Log($"[RoomTilemapSetup] No TilemapRoomGrid found, creating...");
            roomGrid = gameObject.AddComponent<TilemapRoomGrid>();
        }
        else
        {
            Debug.Log($"[RoomTilemapSetup] Found TilemapRoomGrid");
        }

        // Initialize TilemapRoomGrid with references
        roomGrid.Initialize(wallsTilemap, floorTilemap);

        isInitialized = true;
        Debug.Log($"[RoomTilemapSetup] ✅ Initialization complete!");
    }

    private Tilemap CreateTilemap(string layerName, int sortingOrder)
    {
        GameObject tilemapGO = new GameObject(layerName);
        tilemapGO.transform.parent = transform;
        tilemapGO.transform.localPosition = gridOffset;

        Tilemap tilemap = tilemapGO.AddComponent<Tilemap>();
        TilemapRenderer renderer = tilemapGO.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = sortingOrder;

        return tilemap;
    }

    private void SetupColliders()
    {
        if (wallsTilemap == null) return;

        TilemapCollider2D collider = wallsTilemap.GetComponent<TilemapCollider2D>();
        if (collider == null)
        {
            collider = wallsTilemap.gameObject.AddComponent<TilemapCollider2D>();
        }

        CompositeCollider2D composite = wallsTilemap.GetComponent<CompositeCollider2D>();
        if (composite == null)
        {
            composite = wallsTilemap.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        }

        collider.compositeOperation = Collider2D.CompositeOperation.Merge;
    }



    // Public API
    public TilemapRoomGrid GetRoomGrid()   => roomGrid;
    public Tilemap GetWallsTilemap()      => wallsTilemap;
    public Tilemap GetFloorTilemap()      => floorTilemap;
    public int GetWidth()                 => width;
    public int GetHeight()                => height;
    public float GetCellSize()            => cellSize;
    public Vector3 GetGridOffset()        => gridOffset;
    public bool IsInitialized()           => isInitialized;
}