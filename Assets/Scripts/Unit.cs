using UnityEngine;

public class Unit : MonoBehaviour
{
    private GridPosition gridPosition;
    private MoveAction moveAction;
    private SpinAction spinAction;
    private BaseAction[] baseActionArray;
    private RoomGrid currentRoomGrid;
    private bool isInitialized = false;

    private void Awake()
    {
        moveAction = GetComponent<MoveAction>();
        spinAction = GetComponent<SpinAction>();
        baseActionArray = GetComponents<BaseAction>();
        
        // Debug what components we actually have
        Debug.Log($"Unit Awake: {gameObject.name}");
        Debug.Log($"  - MoveAction: {(moveAction != null ? "✓ FOUND" : "✗ NULL")}");
        Debug.Log($"  - SpinAction: {(spinAction != null ? "✓ FOUND" : "✗ NULL")}");
        Debug.Log($"  - BaseActions count: {baseActionArray.Length}");
        
        // List all components on this GameObject
        Component[] allComponents = GetComponents<Component>();
        Debug.Log($"  - All components on {gameObject.name}:");
        foreach (Component comp in allComponents)
        {
            Debug.Log($"    • {comp.GetType().Name}");
        }
    }

    private void OnEnable()
    {
        // Subscribe to level ready event
        LevelGenerator.OnLevelReady += InitializeUnit;
    }

    private void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        LevelGenerator.OnLevelReady -= InitializeUnit;
    }

    private void InitializeUnit()
    {
        Debug.Log($"=== Unit.InitializeUnit called for {gameObject.name} (via OnLevelReady event) ===");
        
        // Get current room from RoomManager
        if (RoomManager.Instance != null)
        {
            currentRoomGrid = RoomManager.Instance.GetCurrentRoomGrid();
            Debug.Log($"Got room from RoomManager: {(currentRoomGrid != null ? "✓ Success" : "✗ NULL")}");
        }
        else
        {
            Debug.LogWarning("✗ RoomManager.Instance is NULL in Unit.InitializeUnit");
        }

        // Fallback: try to find room at our world position
        if (currentRoomGrid == null)
        {
            Debug.Log($"Trying to find room at world position: {transform.position}");
            LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
            if (levelGen != null)
            {
                currentRoomGrid = levelGen.GetRoomAtWorldPosition(transform.position);
                Debug.Log($"LevelGen found room: {(currentRoomGrid != null ? "✓ Success" : "✗ NULL")}");
            }
        }
        
        if (currentRoomGrid != null)
        {
            gridPosition = currentRoomGrid.GetGridPosition(transform.position);
            currentRoomGrid.AddUnitAtGridPosition(gridPosition, this);
            isInitialized = true;
            Debug.Log($"✓✓✓ Unit {gameObject.name} initialized at grid position {gridPosition} ✓✓✓");
        }
        else
        {
            Debug.LogError($"✗✗✗ Unit {gameObject.name} at position {transform.position} could not find a room!");
        }
    }

    private void Update()
    {
        if (!isInitialized || currentRoomGrid == null) return;

        GridPosition newGridPosition = currentRoomGrid.GetGridPosition(transform.position);
        
        if (newGridPosition != gridPosition)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
            currentRoomGrid.AddUnitAtGridPosition(newGridPosition, this);
            gridPosition = newGridPosition;
        }
    }

    public MoveAction GetMoveAction()
    {
        if (moveAction == null)
        {
            Debug.LogError($"GetMoveAction() called but moveAction is NULL on {gameObject.name}!");
        }
        return moveAction;
    }
    
    public SpinAction GetSpinAction() => spinAction;
    public GridPosition GetGridPosition() => gridPosition;
    public RoomGrid GetCurrentRoomGrid() => currentRoomGrid;
    public BaseAction[] GetBaseActionArray() => baseActionArray;

    public void SetCurrentRoomGrid(RoomGrid roomGrid)
    {
        if (currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
        }

        currentRoomGrid = roomGrid;
        
        if (currentRoomGrid != null)
        {
            gridPosition = currentRoomGrid.GetGridPosition(transform.position);
            currentRoomGrid.AddUnitAtGridPosition(gridPosition, this);
            isInitialized = true;
        }
    }

    public void PlaceInRoom(RoomGrid roomGrid, GridPosition newGridPosition)
{
    if (currentRoomGrid != null)
    {
        currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);
    }

    currentRoomGrid = roomGrid;
    gridPosition = newGridPosition;

    transform.position = roomGrid.GetWorldPosition(newGridPosition);
    roomGrid.AddUnitAtGridPosition(newGridPosition, this);
}

}