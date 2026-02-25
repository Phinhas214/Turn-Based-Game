using System.Collections.Generic;

public class GridObject
{
    private GridSystem gridSystem;
    private GridPosition gridPosition;
    private List<Unit> unitList;
    private List<EnemyUnit> enemyList; // NEW

    public GridObject(GridSystem gridSystem, GridPosition gridPosition)
    {
        this.gridSystem = gridSystem;
        this.gridPosition = gridPosition;
        unitList = new List<Unit>();
        enemyList = new List<EnemyUnit>(); // NEW
    }

    public override string ToString()
    {
        string unitString = "";
        foreach (Unit unit in unitList)
        {
            unitString += unit + "\n";
        }
        foreach (EnemyUnit enemy in enemyList) // NEW
        {
            unitString += enemy + "\n";
        }
        return gridPosition.ToString() + "\n" + unitString;
    }

    // ── Existing player unit methods (unchanged) ──────────────────────────

    public void AddUnit(Unit unit)
    {
        unitList.Add(unit);
    }

    public void RemoveUnit(Unit unit)
    {
        unitList.Remove(unit);
    }

    public List<Unit> GetUnitList()
    {
        return unitList;
    }

    public bool HasAnyUnit()
    {
        return unitList.Count > 0;
    }

    public Unit GetUnit()
    {
        if (HasAnyUnit())
        {
            return unitList[0];
        }
        return null;
    }

    // ── New enemy methods ─────────────────────────────────────────────────

    public void AddEnemy(EnemyUnit enemy)
    {
        enemyList.Add(enemy);
    }

    public void RemoveEnemy(EnemyUnit enemy)
    {
        enemyList.Remove(enemy);
    }

    public List<EnemyUnit> GetEnemyList()
    {
        return enemyList;
    }

    public bool HasAnyEnemy()
    {
        return enemyList.Count > 0;
    }

    public EnemyUnit GetEnemy()
    {
        if (HasAnyEnemy())
        {
            return enemyList[0];
        }
        return null;
    }
}