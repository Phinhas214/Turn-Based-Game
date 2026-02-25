/// <summary>
/// Implement this on any component that owns health stats (PlayerStats, EnemyUnit, etc.)
/// HealthComponent looks for this on its GameObject in Awake to auto-initialize max health.
/// </summary>
public interface IHasHealth
{
    int GetMaxHealth();
}