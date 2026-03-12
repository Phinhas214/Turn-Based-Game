using TMPro;
using UnityEngine;

public class DamageNumber : MonoBehaviour
{
    [Header("References")]
    public TextMeshPro text;

    [Header("Timing")]
    public float lifetime = 0.6f;

    [Header("Movement")]
    public Vector3 floatVelocity = new Vector3(0, 1f, 0);

    [Header("Transform Overrides")]
    [Tooltip("Offset applied to the spawn position.")]
    public Vector3 positionOffset = new Vector3(0f, 0.5f, 0f);

    [Tooltip("Rotation of the number — e.g. (90, 0, 0) to lie flat in a top-down view.")]
    public Vector3 rotation = new Vector3(90f, 0f, 0f);

    [Tooltip("Uniform scale of the number.")]
    public float scale = 1f;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        transform.localRotation = Quaternion.Euler(rotation);
        transform.localScale    = Vector3.one * scale;
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        transform.position += floatVelocity * Time.deltaTime;
    }

    // ── Init ───────────────────────────────────────────────────────────────

    public void Initialize(int amount)
    {
        text.text = amount.ToString();
    }

    // ── Static spawn helper ────────────────────────────────────────────────

    /// <summary>
    /// Instantiates a damage number at worldPosition + the prefab's positionOffset.
    /// </summary>
    public static DamageNumber Spawn(GameObject prefab, Vector3 worldPosition, int amount)
    {
        if (prefab == null) return null;

        DamageNumber dn = prefab.GetComponent<DamageNumber>();
        Vector3 spawnPos = worldPosition + (dn != null ? dn.positionOffset : Vector3.zero);

        GameObject go   = Instantiate(prefab, spawnPos, Quaternion.identity);
        DamageNumber dmg = go.GetComponent<DamageNumber>();
        dmg?.Initialize(amount);
        return dmg;
    }
}