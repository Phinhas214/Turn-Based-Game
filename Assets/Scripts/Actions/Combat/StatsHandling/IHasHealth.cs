/// <summary>
/// Implement on any component that owns health stats.
/// HealthComponent finds this on its GameObject in Awake to auto-initialize max health.
/// Works for players (PlayerStats), enemies (EnemyUnit), or anything else.
/// </summary>
public interface IHasHealth
{
    int GetMaxHealth();
}