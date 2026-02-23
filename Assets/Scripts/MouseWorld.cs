using UnityEngine;

/// <summary>
/// Raycasts from the camera to the mouse-plane layer and returns the hit position.
/// Y is forced to zero before returning so that RoomGrid.GetGridPosition always
/// receives a flat position — preventing gridOffset.Y from causing tile mismatches.
/// </summary>
public class MouseWorld : MonoBehaviour
{
    private static MouseWorld instance;

    [SerializeField] private LayerMask mousePlaneLayerMask;

    [Header("Debug")]
    [Tooltip("Draw a sphere in the Scene view at the current mouse world position.")]
    [SerializeField] private bool showDebugSphere = false;

    private Vector3 lastHitPoint;

    private void Awake()
    {
        instance = this;
    }

    public static Vector3 GetPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit raycastHit, float.MaxValue, instance.mousePlaneLayerMask))
        {
            // Force Y to zero so the grid conversion in RoomGrid.GetGridPosition
            // doesn't get thrown off by the floor collider height or gridOffset.Y.
            Vector3 point = raycastHit.point;
            point.y = 0f;
            instance.lastHitPoint = point;
            return point;
        }

        return Vector3.zero;
    }

    private void OnDrawGizmos()
    {
        if (!showDebugSphere) return;
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(lastHitPoint, 0.2f);
    }
}