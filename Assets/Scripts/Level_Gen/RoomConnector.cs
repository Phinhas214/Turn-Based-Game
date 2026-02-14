using UnityEngine;

public class RoomConnector : MonoBehaviour
{
    [System.Serializable]
    public class ConnectionPoint
    {
        public Transform transform;
        public LevelGenerator.Direction direction;
        public bool isConnected = false;
    }

    [Header("Connection Points")]
    public ConnectionPoint northConnection;
    public ConnectionPoint southConnection;
    public ConnectionPoint eastConnection;
    public ConnectionPoint westConnection;

    public ConnectionPoint GetConnectionPoint(LevelGenerator.Direction direction)
    {
        switch (direction)
        {
            case LevelGenerator.Direction.North: return northConnection;
            case LevelGenerator.Direction.South: return southConnection;
            case LevelGenerator.Direction.East: return eastConnection;
            case LevelGenerator.Direction.West: return westConnection;
            default: return null;
        }
    }

    public bool HasConnectionPoint(LevelGenerator.Direction direction)
    {
        ConnectionPoint point = GetConnectionPoint(direction);
        return point != null && point.transform != null;
    }

    public bool IsDirectionAvailable(LevelGenerator.Direction direction)
    {
        ConnectionPoint point = GetConnectionPoint(direction);
        return point != null && point.transform != null && !point.isConnected;
    }

    public void MarkConnectionUsed(LevelGenerator.Direction direction)
    {
        ConnectionPoint point = GetConnectionPoint(direction);
        if (point != null)
        {
            point.isConnected = true;
        }
    }

    // Draw gizmos to visualize connection points
    private void OnDrawGizmos()
    {
        DrawConnectionGizmo(northConnection, Color.blue);
        DrawConnectionGizmo(southConnection, Color.red);
        DrawConnectionGizmo(eastConnection, Color.green);
        DrawConnectionGizmo(westConnection, Color.yellow);
    }

    private void DrawConnectionGizmo(RoomConnector.ConnectionPoint connection, Color color)
    {
        if (connection != null && connection.transform != null)
        {
            Gizmos.color = connection.isConnected ? Color.gray : color;
            Gizmos.DrawSphere(connection.transform.position, 0.3f);
            Gizmos.DrawLine(connection.transform.position, connection.transform.position + connection.transform.forward * 1f);
        }
    }
}