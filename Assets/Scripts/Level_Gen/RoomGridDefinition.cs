using UnityEngine;

public class RoomGridDefinition : MonoBehaviour
{
    [Header("Grid Size")]
    public int width = 10;
    public int height = 10;

    [Header("Grid Position Offset")]
    [Tooltip("Move the grid left/right")]
    public float gridOffsetX = 0f;
    [Tooltip("Move the grid up/down to sit on your floor")]
    public float gridOffsetY = 0.1f;
    [Tooltip("Move the grid forward/back")]
    public float gridOffsetZ = 0f;

    [Header("Debug Visualization")]
    public bool showGizmos = true;

    public Vector3 GetGridOffset()
    {
        return new Vector3(gridOffsetX, gridOffsetY, gridOffsetZ);
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        float cellSize = 2f;

        // Calculate the starting corner of the grid with all offsets applied
        // Vector3 gridOrigin = transform.position + new Vector3(gridOffsetX, gridOffsetY, gridOffsetZ);

        // float cellSize = 2f;

        Vector3 gridOrigin = transform.position + new Vector3(gridOffsetX, gridOffsetY, gridOffsetZ) + new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);

        

        Gizmos.color = Color.cyan;

        // Draw vertical lines (along Z axis)
        for (int x = 0; x <= width; x++)
        {
            Vector3 start = gridOrigin + new Vector3(x * cellSize, 0, 0);
            Vector3 end = gridOrigin + new Vector3(x * cellSize, 0, height * cellSize);
            Gizmos.DrawLine(start, end);
        }

        // Draw horizontal lines (along X axis)
        for (int z = 0; z <= height; z++)
        {
            Vector3 start = gridOrigin + new Vector3(0, 0, z * cellSize);
            Vector3 end = gridOrigin + new Vector3(width * cellSize, 0, z * cellSize);
            Gizmos.DrawLine(start, end);
        }

        // Draw corner markers
        Gizmos.color = Color.yellow;
        float markerSize = 0.3f;
        Gizmos.DrawSphere(gridOrigin, markerSize);
        Gizmos.DrawSphere(gridOrigin + new Vector3(width * cellSize, 0, 0), markerSize);
        Gizmos.DrawSphere(gridOrigin + new Vector3(0, 0, height * cellSize), markerSize);
        Gizmos.DrawSphere(gridOrigin + new Vector3(width * cellSize, 0, height * cellSize), markerSize);

        // Draw label at origin corner
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(gridOrigin, $"Grid {width}x{height}");
        #endif
    }
}