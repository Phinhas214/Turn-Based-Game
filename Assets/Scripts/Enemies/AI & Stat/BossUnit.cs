using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to a boss prefab alongside EnemyUnit.
///
/// Responsibilities:
///   - Tracks invisibility state and exposes IsInvisible.
///   - Handles all four reveal conditions:
///       1. A player stands within the proximity radius.
///       2. A player attack lands (checked via TryHitInvisibleBoss).
///       3. All minions in the room are dead.
///       4. The turn-based cooldown has elapsed.
///   - Manages wave progression (calls EnemySpawner to place minions).
///   - Picks melee or ranged combat mode at spawn time.
///   - Drives the visual show/hide of the boss mesh.
///
/// The actual combat (move + attack) is handled by BossAI which reads
/// IsInvisible and the active CombatMode from this component.
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class BossUnit : MonoBehaviour
{
    [Header("Boss Configuration")]
    [SerializeField] private BossStats bossStats;

    [Header("Visuals")]
    [Tooltip("Root object that is hidden when invisible (should contain renderers but NOT colliders/logic).")]
    [SerializeField] private GameObject visualRoot;

    [Tooltip("Optional particle/effect to play on reveal.")]
    [SerializeField] private GameObject revealEffect;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // ── State ──────────────────────────────────────────────────────────────
    private EnemyUnit              enemyUnit;
    private BossStats.BossCombatMode combatMode;
    private bool                   isInvisible              = false;
    private int                    invisibilityCooldownLeft = 0;    // turns until can go invis again
    private int                    turnsSinceLastInvis      = 0;    // tracks the "go invis every N turns" timer
    private int                    currentWaveIndex         = -1;   // -1 = no wave started yet
    private bool                   waveInProgress           = false;
    private List<EnemyUnit>        currentWaveEnemies       = new List<EnemyUnit>();

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action OnBossRevealed;
    public event Action OnBossHidden;

    // ── Properties ────────────────────────────────────────────────────────
    public BossStats               BossStats  => bossStats;
    public BossStats.BossCombatMode CombatMode => combatMode;
    public bool                    IsInvisible => isInvisible;
    public bool                    HasMoreWaves => bossStats != null &&
                                                   currentWaveIndex + 1 < bossStats.waves.Count;

    // ──────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
    }

    private void Start()
    {
        // Pick combat mode
        if (bossStats != null && bossStats.randomiseCombatMode)
            combatMode = (BossStats.BossCombatMode)UnityEngine.Random.Range(0, 2);
        else if (bossStats != null)
            combatMode = bossStats.forcedCombatMode;

        if (showDebugLogs)
            Debug.Log($"[BossUnit] Combat mode: {combatMode}");

        SetVisibility(true);

        // Watch for enemies dying so we can track wave completion
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged += CheckWaveCompletion;
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged -= CheckWaveCompletion;
    }

    // ── Called by BossAI each turn ─────────────────────────────────────────

    /// <summary>
    /// Called at the START of the boss's turn.
    /// Ticks down cooldowns and checks proximity-based auto-reveals.
    /// </summary>
    public void OnTurnStart()
    {
        if (invisibilityCooldownLeft > 0)
            invisibilityCooldownLeft--;

        if (isInvisible)
        {
            CheckProximityReveal();
            CheckMinionReveal();
        }
    }

    /// <summary>
    /// Called by BossAI AFTER the boss attacks.
    /// If configured, makes the boss go invisible (subject to cooldown).
    /// </summary>
    public void OnAttackPerformed()
    {
        if (bossStats == null || bossStats.goesInvisAfterAttackEveryNTurns <= 0) return;

        turnsSinceLastInvis++;
        if (turnsSinceLastInvis >= bossStats.goesInvisAfterAttackEveryNTurns &&
            invisibilityCooldownLeft <= 0)
        {
            GoInvisible();
            turnsSinceLastInvis = 0;
        }
    }

    // ── Wave spawning ──────────────────────────────────────────────────────

    /// <summary>
    /// Triggers the next wave if one is available and no wave is currently in progress.
    /// Called by BossAI at the start of its turn.
    /// </summary>
    public void TrySpawnNextWave()
    {
        if (bossStats == null || waveInProgress || !HasMoreWaves) return;

        currentWaveIndex++;
        BossStats.WaveTable wave = bossStats.waves[currentWaveIndex];

        if (showDebugLogs)
            Debug.Log($"[BossUnit] Spawning wave {currentWaveIndex}: {wave.waveName}");

        currentWaveEnemies.Clear();
        waveInProgress = true;

        EnemySpawner spawner = FindFirstObjectByType<EnemySpawner>();
        if (spawner == null)
        {
            Debug.LogWarning("[BossUnit] No EnemySpawner found — cannot spawn wave.");
            waveInProgress = false;
            return;
        }

        RoomGrid room = enemyUnit.CurrentRoomGrid;
        if (room == null)
        {
            Debug.LogWarning("[BossUnit] Boss has no CurrentRoomGrid — cannot spawn wave.");
            waveInProgress = false;
            return;
        }

        foreach (BossStats.SpawnRow row in wave.spawns)
        {
            if (row.prefab == null) continue;

            for (int i = 0; i < row.count; i++)
            {
                GridPosition? pos = GetSpawnPositionNearBoss(room);
                if (pos == null)
                {
                    Debug.LogWarning("[BossUnit] No walkable spawn tile found near boss.");
                    continue;
                }

                EnemyUnit spawned = spawner.SpawnEnemy(row.prefab, room, pos.Value);
                if (spawned != null)
                    currentWaveEnemies.Add(spawned);
            }
        }
    }

    private void CheckWaveCompletion()
    {
        if (!waveInProgress || currentWaveEnemies.Count == 0) return;

        // Remove destroyed / dead entries
        currentWaveEnemies.RemoveAll(e => e == null || e.IsDead);

        if (currentWaveEnemies.Count == 0)
        {
            waveInProgress = false;
            if (showDebugLogs)
                Debug.Log($"[BossUnit] Wave {currentWaveIndex} cleared.");

            // Killing all minions reveals the boss
            if (isInvisible)
            {
                if (showDebugLogs) Debug.Log("[BossUnit] All minions dead — boss revealed.");
                Reveal();
            }
        }
    }

    // ── Invisibility API ───────────────────────────────────────────────────

    /// <summary>
    /// Called by the player's attack system before applying damage.
    /// Returns true if the attack connects (boss is visible OR luck + invis hit).
    /// </summary>
    public bool TryHitInvisibleBoss()
    {
        if (!isInvisible) return true;    // always hit if visible

        float roll = UnityEngine.Random.value;
        if (roll <= bossStats.invisHitChance)
        {
            Debug.Log($"[BossUnit] Lucky hit on invisible boss! (roll {roll:F2} ≤ {bossStats.invisHitChance:F2})");
            Reveal();
            return true;
        }

        Debug.Log($"[BossUnit] Attack missed invisible boss. (roll {roll:F2} > {bossStats.invisHitChance:F2})");
        return false;
    }

    private void CheckProximityReveal()
    {
        if (!isInvisible || bossStats == null) return;

        RoomGrid room = enemyUnit.CurrentRoomGrid;
        if (room == null) return;

        // Count players in room to scale radius
        List<PlayerTarget> playersInRoom = GetPlayersInRoom(room);
        int radius = bossStats.baseRevealRadius +
                     Mathf.Max(0, playersInRoom.Count - 1) * bossStats.revealRadiusPerExtraPlayer;

        GridPosition bossPos = enemyUnit.GridPosition;
        foreach (PlayerTarget pt in playersInRoom)
        {
            Unit unit = pt.GetUnit();
            if (unit == null) continue;

            int dist = ManhattanDist(bossPos, unit.GetGridPosition());
            if (dist <= radius)
            {
                if (showDebugLogs) Debug.Log($"[BossUnit] Player within {radius} tiles — boss revealed.");
                Reveal();
                return;
            }
        }
    }

    private void CheckMinionReveal()
    {
        if (!isInvisible || waveInProgress) return;

        // If we're between waves (no wave in progress) also check whether room is clear
        RoomGrid room = enemyUnit.CurrentRoomGrid;
        if (room == null) return;

        List<EnemyUnit> roomEnemies = EnemyManager.Instance?.GetEnemiesInRoom(room)
                                     ?? new List<EnemyUnit>();

        // Remove self from count
        roomEnemies.RemoveAll(e => e == enemyUnit);

        if (roomEnemies.Count == 0)
        {
            if (showDebugLogs) Debug.Log("[BossUnit] No minions alive — boss revealed (minion reveal).");
            Reveal();
        }
    }

    public void GoInvisible()
    {
        if (isInvisible || bossStats == null) return;
        if (invisibilityCooldownLeft > 0) return;

        isInvisible = true;
        SetVisibility(false);
        if (showDebugLogs) Debug.Log("[BossUnit] Boss went invisible.");
        OnBossHidden?.Invoke();
    }

    public void Reveal()
    {
        if (!isInvisible) return;

        isInvisible              = false;
        invisibilityCooldownLeft = bossStats != null ? bossStats.invisibilityCooldownTurns : 3;

        SetVisibility(true);

        if (revealEffect != null)
            Instantiate(revealEffect, transform.position, Quaternion.identity);

        if (showDebugLogs) Debug.Log("[BossUnit] Boss revealed.");
        OnBossRevealed?.Invoke();
    }

    // ── Visuals ────────────────────────────────────────────────────────────

    private void SetVisibility(bool visible)
    {
        if (visualRoot != null)
            visualRoot.SetActive(visible);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private GridPosition? GetSpawnPositionNearBoss(RoomGrid room)
    {
        TilemapRoomGrid tilemapGrid = room.GetTilemapRoomGrid();
        if (tilemapGrid == null) return null;

        GridPosition bossPos = enemyUnit.GridPosition;
        int radius = bossStats != null ? bossStats.spawnRadius : 5;

        List<GridPosition> candidates = new List<GridPosition>();
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dz) > radius) continue;
            GridPosition c = new GridPosition(bossPos.x + dx, bossPos.z + dz);
            if (tilemapGrid.IsWalkable(c) && c != bossPos)
                candidates.Add(c);
        }

        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private List<PlayerTarget> GetPlayersInRoom(RoomGrid room)
    {
        var result = new List<PlayerTarget>();
        // Single-player: check singleton
        PlayerTarget single = PlayerTarget.Instance;
        if (single != null && single.IsInRoom(room))
            result.Add(single);
        // Multiplayer extension point: iterate all PlayerTargets in the scene
        // foreach (var pt in FindObjectsByType<PlayerTarget>(FindObjectsSortMode.None))
        //     if (pt.IsInRoom(room) && !result.Contains(pt)) result.Add(pt);
        return result;
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
}