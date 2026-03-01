using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* pathfinder updated for tilemap-based rooms.
/// 
/// Changes from original:
/// • Updated IsWalkable() to check tilemap walls via TilemapRoomGrid
/// • Added optional wall-layer checking
/// • Everything else (pathfinding algorithm) remains the same
/// </summary>
public class Pathfinder
{
    private class Node
    {
        public GridPosition position;
        public Node parent;
        public int gCost; // cost from start
        public int hCost; // heuristic cost to end
        public int fCost => gCost + hCost;

        public Node(GridPosition position, Node parent, int gCost, int hCost)
        {
            this.position = position;
            this.parent   = parent;
            this.gCost    = gCost;
            this.hCost    = hCost;
        }
    }

    private RoomGrid roomGrid;
    private TilemapRoomGrid tilemapGrid; // UPDATED: Direct tilemap access

    public Pathfinder(RoomGrid roomGrid)
    {
        this.roomGrid = roomGrid;
        this.tilemapGrid = roomGrid?.GetTilemapRoomGrid(); // Get tilemap interface
    }

    /// <summary>
    /// Find a path from start to end on the room grid.
    /// Checks for units/enemies AND wall tiles.
    /// Returns positions from the step AFTER start up to and including end.
    /// </summary>
    public List<GridPosition> FindPath(GridPosition start, GridPosition end, bool ignoreUnits = false)
    {
        List<Node>       openList   = new List<Node>();
        HashSet<GridPosition> closedSet = new HashSet<GridPosition>();

        Node startNode = new Node(start, null, 0, GetHeuristic(start, end));
        openList.Add(startNode);

        int maxIterations = roomGrid.GetWidth() * roomGrid.GetHeight();
        int iterations    = 0;

        while (openList.Count > 0 && iterations < maxIterations)
        {
            iterations++;

            // Pick node with lowest fCost (tie-break on hCost)
            Node current = openList[0];
            foreach (Node n in openList)
                if (n.fCost < current.fCost || (n.fCost == current.fCost && n.hCost < current.hCost))
                    current = n;

            openList.Remove(current);
            closedSet.Add(current.position);

            // Reached destination
            if (current.position == end)
                return BuildPath(current);

            // Explore neighbours (4-directional)
            foreach (GridPosition neighbour in GetNeighbours(current.position))
            {
                if (closedSet.Contains(neighbour)) continue;
                if (!roomGrid.IsValidGridPosition(neighbour)) continue;

                // UPDATED: Check both units/enemies AND walls
                bool isWalkable = ignoreUnits || IsWalkable(neighbour, isDestination: (neighbour == end));
                if (!isWalkable) continue;

                int newGCost = current.gCost + 1;
                Node existing = openList.Find(n => n.position == neighbour);

                if (existing == null)
                {
                    openList.Add(new Node(neighbour, current, newGCost, GetHeuristic(neighbour, end)));
                }
                else if (newGCost < existing.gCost)
                {
                    existing.parent = current;
                    existing.gCost  = newGCost;
                }
            }
        }

        // No path found
        return new List<GridPosition>();
    }

    /// <summary>
    /// Returns the closest reachable position to the target within attackRange tiles.
    /// Useful for enemies that want to get adjacent to the player without standing on them.
    /// </summary>
    public List<GridPosition> FindPathToRange(GridPosition start, GridPosition target, int attackRange)
    {
        // Already in range
        if (GetHeuristic(start, target) <= attackRange)
            return new List<GridPosition>();

        // Try positions around target at exactly attackRange distance, closest first
        List<GridPosition> candidates = new List<GridPosition>();
        for (int x = -attackRange; x <= attackRange; x++)
        {
            for (int z = -attackRange; z <= attackRange; z++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(z) != attackRange) continue;
                GridPosition candidate = new GridPosition(target.x + x, target.z + z);
                if (roomGrid.IsValidGridPosition(candidate) && IsWalkable(candidate, isDestination: true))
                    candidates.Add(candidate);
            }
        }

        // Sort candidates by distance from start
        candidates.Sort((a, b) => GetHeuristic(start, a).CompareTo(GetHeuristic(start, b)));

        foreach (GridPosition candidate in candidates)
        {
            List<GridPosition> path = FindPath(start, candidate);
            if (path.Count > 0)
                return path;
        }

        // Fallback: path directly toward target
        return FindPath(start, target, ignoreUnits: true);
    }

    // ────────────────────────────────────────────────────────────────────
    //  UPDATED: Walkability checking
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if a position is walkable (no wall, no unit/enemy).
    /// isDestination allows standing on the destination even if occupied.
    /// </summary>
    private bool IsWalkable(GridPosition gridPos, bool isDestination = false)
    {
        // Check bounds
        if (!roomGrid.IsValidGridPosition(gridPos)) return false;

        // Check for wall tile
        if (tilemapGrid != null && tilemapGrid.IsWallAtPosition(gridPos))
            return false;

        // Check for unit/enemy blocking (unless this is destination)
        if (!isDestination && roomGrid.HasAnyUnitOnGridPosition(gridPos))
            return false;

        return true;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Unchanged: Helpers
    // ────────────────────────────────────────────────────────────────────

    private List<GridPosition> BuildPath(Node endNode)
    {
        List<GridPosition> path = new List<GridPosition>();
        Node current = endNode;

        while (current.parent != null)
        {
            path.Add(current.position);
            current = current.parent;
        }

        path.Reverse();
        return path;
    }

    private int GetHeuristic(GridPosition a, GridPosition b)
    {
        // Manhattan distance
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }

    private List<GridPosition> GetNeighbours(GridPosition pos)
    {
        return new List<GridPosition>
        {
            new GridPosition(pos.x + 1, pos.z),
            new GridPosition(pos.x - 1, pos.z),
            new GridPosition(pos.x,     pos.z + 1),
            new GridPosition(pos.x,     pos.z - 1),
        };
    }
}