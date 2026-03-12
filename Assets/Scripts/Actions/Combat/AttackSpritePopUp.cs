using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns a SpriteRenderer above an enemy, plays through a sliced sprite sheet
/// once at the given FPS, then destroys itself.
///
/// Usage:
///   AttackSpritePopup.Show(stats.attackData, transform.position);
///   AttackSpritePopup.Show(stats.attackData, transform.position, offset: new Vector3(0.5f, 0f, 0.5f));
/// </summary>
public class AttackSpritePopup : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Frames per second — override by passing fps parameter to Show().")]
    [SerializeField] private float fps = 12f;

    // ── Static entry point ─────────────────────────────────────────────────

    /// <summary>
    /// Spawns an attack animation popup.
    /// targetWorldPosition  — base position (e.g. player or enemy world pos)
    /// offset               — XYZ offset from that position (nudge it anywhere)
    /// heightOffset         — additional Y lift above the tile surface
    /// fps                  — animation playback speed
    /// scale                — uniform world-space scale
    /// </summary>
    public static void Show(CombatActionData attackData,
                            Vector3 targetWorldPosition,
                            Vector3 offset     = default,
                            float heightOffset = 0.1f,
                            float fps          = 12f,
                            float scale        = 3f)
    {
        if (attackData == null) return;

        bool hasAnimation = attackData.animationFrames != null &&
                            attackData.animationFrames.Length > 0;

        if (!hasAnimation && attackData.icon == null) return;

        GameObject go = new GameObject("AttackSpritePopup");
        go.transform.position   = targetWorldPosition + offset + Vector3.up * heightOffset;
        go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite         = hasAnimation ? attackData.animationFrames[0] : attackData.icon;
        sr.sortingOrder   = 10;

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