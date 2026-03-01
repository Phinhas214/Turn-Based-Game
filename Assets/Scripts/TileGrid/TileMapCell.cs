using System.Collections.Generic;

/// <summary>
/// Lightweight replacement for GridObject.
/// Stores units and enemies at a specific tilemap cell.
/// No grid logic—just data container.
/// </summary>
public class TilemapCell
{
    private List<Unit> unitList = new List<Unit>();
    private List<EnemyUnit> enemyList = new List<EnemyUnit>();

    // ── Unit methods ───────────────────────────────────────────────────────

    public void AddUnit(Unit unit)
    {
        if (!unitList.Contains(unit))
            unitList.Add(unit);
    }

    public void RemoveUnit(Unit unit)
    {
        unitList.Remove(unit);
    }

    public List<Unit> GetUnitList()
    {
        return new List<Unit>(unitList);
    }

    public bool HasAnyUnit()
    {
        return unitList.Count > 0;
    }

    public Unit GetUnit()
    {
        return unitList.Count > 0 ? unitList[0] : null;
    }

    // ── Enemy methods ──────────────────────────────────────────────────────

    public void AddEnemy(EnemyUnit enemy)
    {
        if (!enemyList.Contains(enemy))
            enemyList.Add(enemy);
    }

    public void RemoveEnemy(EnemyUnit enemy)
    {
        enemyList.Remove(enemy);
    }

    public List<EnemyUnit> GetEnemyList()
    {
        return new List<EnemyUnit>(enemyList);
    }

    public bool HasAnyEnemy()
    {
        return enemyList.Count > 0;
    }

    public EnemyUnit GetEnemy()
    {
        return enemyList.Count > 0 ? enemyList[0] : null;
    }

    // ── Combined check ────────────────────────────────────────────────────

    public bool HasAnyUnitOrEnemy()
    {
        return HasAnyUnit() || HasAnyEnemy();
    }

    public override string ToString()
    {
        string result = "";
        foreach (Unit u in unitList)
            result += u + "\n";
        foreach (EnemyUnit e in enemyList)
            result += e + "\n";
        return result;
    }
}