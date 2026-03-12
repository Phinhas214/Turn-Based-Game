using System.Collections.Generic;
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

    [Header("Door Strips (wall closed = active, open = inactive)")]
    public GameObject northDoorStrip;
    public GameObject southDoorStrip;
    public GameObject eastDoorStrip;
    public GameObject westDoorStrip;

    [Header("Room Type")]
    public RoomType roomType = RoomType.Normal;

    private void Awake()
    {
        // Auto-find strips by name if not assigned in Inspector
        if (northDoorStrip == null) northDoorStrip = FindChildByName("NorthDoorStrip");
        if (southDoorStrip == null) southDoorStrip = FindChildByName("SouthDoorStrip");
        if (eastDoorStrip  == null) eastDoorStrip  = FindChildByName("EastDoorStrip");
        if (westDoorStrip  == null) westDoorStrip  = FindChildByName("WestDoorStrip");
    }

    private GameObject FindChildByName(string childName)
    {
        Transform t = transform.Find(childName);
        return t != null ? t.gameObject : null;
    }

    /// <summary>
    /// Opens a doorway (hides the wall strip) or closes it (shows the wall strip).
    /// </summary>
    public void SetDoorOpen(LevelGenerator.Direction direction, bool open)
    {
        GameObject strip = GetStrip(direction);
        if (strip != null)
            strip.SetActive(!open); // strip active = wall visible = door closed
    }

    /// <summary>Close all doors by default — generator will open the connected ones.</summary>
    public void CloseAllDoors()
    {
        foreach (LevelGenerator.Direction dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            SetDoorOpen(dir, false);
    }

    private GameObject GetStrip(LevelGenerator.Direction direction)
    {
        switch (direction)
        {
            case LevelGenerator.Direction.North: return northDoorStrip;
            case LevelGenerator.Direction.South: return southDoorStrip;
            case LevelGenerator.Direction.East:  return eastDoorStrip;
            case LevelGenerator.Direction.West:  return westDoorStrip;
            default: return null;
        }
    }

    // ── Existing connection point API (unchanged) ──────────────────────────

    public ConnectionPoint GetConnectionPoint(LevelGenerator.Direction direction)
    {
        switch (direction)
        {
            case LevelGenerator.Direction.North: return northConnection;
            case LevelGenerator.Direction.South: return southConnection;
            case LevelGenerator.Direction.East:  return eastConnection;
            case LevelGenerator.Direction.West:  return westConnection;
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
            point.isConnected = true;
    }

    private void OnDrawGizmos()
    {
        DrawConnectionGizmo(northConnection, Color.blue);
        DrawConnectionGizmo(southConnection, Color.red);
        DrawConnectionGizmo(eastConnection,  Color.green);
        DrawConnectionGizmo(westConnection,  Color.yellow);
    }

    private void DrawConnectionGizmo(ConnectionPoint connection, Color color)
    {
        if (connection != null && connection.transform != null)
        {
            Gizmos.color = connection.isConnected ? Color.gray : color;
            Gizmos.DrawSphere(connection.transform.position, 0.3f);
            Gizmos.DrawLine(connection.transform.position,
                            connection.transform.position + connection.transform.forward * 1f);
        }
    }
}

public enum RoomType { Start, End, Normal, Special, Boss }