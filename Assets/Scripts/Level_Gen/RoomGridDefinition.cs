using UnityEngine;

// Attach this to each room prefab to define its grid settings
// The LevelGenerator will read this automatically
public class RoomGridDefinition : MonoBehaviour
{
    [Header("Grid Size")]
    public int width = 10;
    public int height = 10;

    [Header("Grid Height Offset")]
    [Tooltip("Adjust this to move the grid up or down to sit on your floor")]
    public float gridHeightOffset = 0.1f;

    [Header("Debug Visualization")]
    [Tooltip("Show the grid boundary in the editor as a gizmo")]
    public bool showGizmos = true;

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Gizmos.color = Color.cyan;

        float cellSize = 2f; // Default preview size

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = transform.position + new Vector3(x * cellSize, gridHeightOffset, 0);
            Vector3 end = transform.position + new Vector3(x * cellSize, gridHeightOffset, height * cellSize);
            Gizmos.DrawLine(start, end);
        }

        for (int z = 0; z <= height; z++)
        {
            Vector3 start = transform.position + new Vector3(0, gridHeightOffset, z * cellSize);
            Vector3 end = transform.position + new Vector3(width * cellSize, gridHeightOffset, z * cellSize);
            Gizmos.DrawLine(start, end);
        }
    }
}