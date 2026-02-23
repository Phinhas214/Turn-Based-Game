using UnityEngine;

/// <summary>
/// Single grid tile visual quad.
/// Supports colored highlighting via MaterialPropertyBlock (no material duplication).
/// Works with URP Lit, URP Unlit, and Standard shaders.
/// </summary>
public class GridSystemVisualSingle : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("The MeshRenderer on this tile visual. Auto-found in children if left empty.")]
    [SerializeField] private MeshRenderer meshRenderer;

    [Header("Default Color")]
    [Tooltip("Color applied when Show() is called without an explicit color argument.")]
    [SerializeField] private Color defaultColor = Color.white;

    // ─────────────────────────────────────────────────────────────────────
    //  Shader property IDs (cached for performance)
    // ─────────────────────────────────────────────────────────────────────
    private static readonly int _baseColorId    = Shader.PropertyToID("_BaseColor");   // URP
    private static readonly int _colorId        = Shader.PropertyToID("_Color");       // Standard

    private MaterialPropertyBlock _propBlock;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (meshRenderer == null)
            meshRenderer = GetComponentInChildren<MeshRenderer>();

        _propBlock = new MaterialPropertyBlock();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Show tile with an explicit color.</summary>
    public void Show(Color color)
    {
        if (meshRenderer == null) return;

        meshRenderer.enabled = true;
        meshRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor(_baseColorId, color);  // URP
        _propBlock.SetColor(_colorId,     color);  // Standard (no-op on URP)
        meshRenderer.SetPropertyBlock(_propBlock);
    }

    /// <summary>Show tile using the Inspector-set defaultColor.</summary>
    public void Show() => Show(defaultColor);

    /// <summary>Hide this tile.</summary>
    public void Hide()
    {
        if (meshRenderer != null)
            meshRenderer.enabled = false;
    }
}
