using UnityEngine;

public class CameraRoomBounds : MonoBehaviour
{
    public Bounds GetBounds()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        return box.bounds;
    }

    public Vector3 GetCenter()
    {
        return GetComponent<BoxCollider>().bounds.center;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(GetBounds().center, GetBounds().size);
    }
}