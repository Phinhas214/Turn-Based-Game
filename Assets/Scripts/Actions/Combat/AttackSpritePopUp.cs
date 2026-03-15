using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a SpriteRenderer above an enemy, plays through a sliced sprite sheet
/// once at the given FPS, then destroys itself.
///
/// Usage (single target — unchanged):
///   AttackSpritePopup.Show(stats.attackData, transform.position);
///
/// Usage (multi-tile — pass the hit tile positions):
///   AttackSpritePopup.ShowOnTiles(stats.attackData, hitPositions, GridManager.Instance);
/// </summary>
public class AttackSpritePopup : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Frames per second — override by passing fps parameter to Show().")]
    [SerializeField] private float fps = 12f;

    // ── Static entry point (single target — unchanged) ─────────────────────

    /// <summary>
    /// Spawns an attack animation popup at a single world position.
    /// </summary>
    public static void Show(CombatActionData attackData,
                            Vector3 targetWorldPosition,
                            Vector3 offset     = default,
                            float heightOffset = 0.1f,
                            float fps          = 12f,
                            float scale        = 3f)
    {
        if (!HasVisuals(attackData)) return;

        SpawnPopup(attackData, targetWorldPosition + offset + Vector3.up * heightOffset, fps, scale);
    }

    // ── Multi-tile entry point ─────────────────────────────────────────────

    /// <summary>
    /// Spawns one popup per affected tile. Only fires when attackData.showSpritePerTile
    /// is true — otherwise falls back to a single popup at the first tile's position.
    ///
    /// hitTilePositions  — the GridPositions returned by AttackPattern.GetAffectedPositions()
    /// gridManager       — used to convert GridPosition → world position
    /// </summary>
    public static void ShowOnTiles(CombatActionData   attackData,
                                   List<GridPosition> hitTilePositions,
                                   float heightOffset = 0.1f,
                                   float fps          = 12f,
                                   float scale        = 3f)
    {
        if (!HasVisuals(attackData)) return;
        if (hitTilePositions == null || hitTilePositions.Count == 0) return;
        if (LevelGrid.Instance == null) return;

        if (attackData.showSpritePerTile)
        {
            // Spawn one popup on every hit tile
            foreach (GridPosition tile in hitTilePositions)
            {
                Vector3 worldPos = LevelGrid.Instance.GetWorldPosition(tile) + Vector3.up * heightOffset;
                SpawnPopup(attackData, worldPos, fps, scale);
            }
        }
        else
        {
            // Fallback: single popup on the first tile (same as old behaviour)
            Vector3 worldPos = LevelGrid.Instance.GetWorldPosition(hitTilePositions[0]) + Vector3.up * heightOffset;
            SpawnPopup(attackData, worldPos, fps, scale);
        }
    }

    // ── Internal spawn ─────────────────────────────────────────────────────

    private static bool HasVisuals(CombatActionData attackData)
    {
        if (attackData == null) return false;
        bool hasAnimation = attackData.animationFrames != null &&
                            attackData.animationFrames.Length > 0;
        return hasAnimation || attackData.icon != null;
    }

    private static void SpawnPopup(CombatActionData attackData,
                                   Vector3          worldPos,
                                   float            fps,
                                   float            scale)
    {
        bool hasAnimation = attackData.animationFrames != null &&
                            attackData.animationFrames.Length > 0;

        GameObject go = new GameObject("AttackSpritePopup");
        go.transform.position   = worldPos;
        go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = hasAnimation ? attackData.animationFrames[0] : attackData.icon;
        sr.sortingOrder = 10;

        AttackSpritePopup popup = go.AddComponent<AttackSpritePopup>();
        popup.fps    = fps;
        popup.frames = hasAnimation ? attackData.animationFrames : null;
        popup.sr     = sr;
    }

    // ── Runtime state ──────────────────────────────────────────────────────

    private Sprite[]       frames;
    private SpriteRenderer sr;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (frames != null && frames.Length > 1)
            StartCoroutine(PlayAnimation());
        else
            StartCoroutine(StaticFlash());
    }

    // ── Animation ─────────────────────────────────────────────────────────

    private IEnumerator PlayAnimation()
    {
        float frameDuration = 1f / fps;
        foreach (Sprite frame in frames)
        {
            if (frame != null)
                sr.sprite = frame;
            yield return new WaitForSeconds(frameDuration);
        }
        Destroy(gameObject);
    }

    // ── Static flash fallback ──────────────────────────────────────────────

    private IEnumerator StaticFlash()
    {
        yield return new WaitForSeconds(3f / fps);
        Destroy(gameObject);
    }
}