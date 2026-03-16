using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles LOSE state only (player death).
/// Win/progression is handled entirely by EndRoomUI to avoid conflicts.
/// Restart regenerates in-place — does NOT reload the scene so
/// WaveManager and other DontDestroyOnLoad singletons stay intact.
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    public event Action OnGameLost;
    public event Action OnGameRestarted;

    public enum GameState { Playing, Lost }
    public GameState CurrentState { get; private set; } = GameState.Playing;

    private HealthComponent playerHealth;
    private bool subscribedToPlayer = false;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        UnsubscribeFromPlayer();
    }

    private void Update()
    {
        if (!subscribedToPlayer)
            TrySubscribeToPlayer();

        if (CurrentState == GameState.Playing && playerHealth != null && playerHealth.IsDead)
            HandlePlayerDeath();
    }

    // ── Level ready ────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        CurrentState       = GameState.Playing;
        subscribedToPlayer = false;
        Time.timeScale     = 1f;
        TrySubscribeToPlayer();
        Debug.Log("[GameStateManager] Level ready — watching for lose.");
    }

    // ── Player subscription ────────────────────────────────────────────────

    private void TrySubscribeToPlayer()
    {
        Unit player = FindFirstObjectByType<Unit>();
        if (player == null) return;

        HealthComponent hc = player.GetComponent<HealthComponent>();
        if (hc == null || hc == playerHealth) return;

        UnsubscribeFromPlayer();
        playerHealth          = hc;
        playerHealth.OnDeath += HandlePlayerDeath;
        subscribedToPlayer    = true;

        Debug.Log($"[GameStateManager] Subscribed to player health ({playerHealth.MaxHealth} HP).");
    }

    private void UnsubscribeFromPlayer()
    {
        if (playerHealth != null)
            playerHealth.OnDeath -= HandlePlayerDeath;
        playerHealth       = null;
        subscribedToPlayer = false;
    }

    // ── Death ──────────────────────────────────────────────────────────────

    private void HandlePlayerDeath()
    {
        if (CurrentState != GameState.Playing) return;
        CurrentState = GameState.Lost;
        Debug.Log("[GameStateManager] Player died — GAME OVER.");
        OnGameLost?.Invoke();
        LoseScreen.Show();
    }

    // ── Called by EndRoomUI when advancing to the next level ───────────────

    /// <summary>
    /// Resets game state to Playing and notifies listeners (e.g. GameStateUI).
    /// Call this instead of invoking OnGameRestarted directly from outside.
    /// </summary>
    public void NotifyLevelAdvanced()
    {
        CurrentState       = GameState.Playing;
        subscribedToPlayer = false;
        Time.timeScale     = 1f;
        OnGameRestarted?.Invoke();
        Debug.Log("[GameStateManager] Level advanced — state reset to Playing.");
    }

    // ── Restart ────────────────────────────────────────────────────────────

    /// <summary>
    /// Restarts in-place. resetProgress=true resets WaveManager to level 1.
    /// Does NOT reload the scene.
    /// </summary>
    public void RestartGame(bool resetProgress = true)
    {
        Debug.Log($"[GameStateManager] Restarting. ResetProgress={resetProgress}");
        Time.timeScale = 1f;

        if (resetProgress)
            WaveManager.Instance?.ResetToLevel1();

        OnGameRestarted?.Invoke();

        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen != null)
            levelGen.GenerateLevel();
        else
        {
            Debug.LogWarning("[GameStateManager] No LevelGenerator — reloading scene.");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}