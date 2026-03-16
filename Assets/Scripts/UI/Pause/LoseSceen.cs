using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Shows when the player dies.
/// Displays how many stages were cleared this run and offers a Main Menu button.
///
/// Hook up by calling LoseScreen.Show() from wherever player death is handled
/// (e.g. your GameStateManager or HealthComponent OnDeath event on the player).
///
/// Place this on a persistent GameObject or in your UI scene.
/// </summary>
public class LoseScreen : MonoBehaviour
{
    public static LoseScreen Instance { get; private set; }

    [Header("UI References (wire up in Inspector, or fallback UI auto-builds)")]
    [SerializeField] private GameObject          panelRoot;
    [SerializeField] private TextMeshProUGUI     titleLabel;
    [SerializeField] private TextMeshProUGUI     stagesClearedLabel;
    [SerializeField] private TextMeshProUGUI     levelReachedLabel;
    [SerializeField] private Button              mainMenuButton;
    [SerializeField] private Button              retryButton;

    [Header("Settings")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [Tooltip("If true, Retry regenerates the level at level 1. If false, Retry continues from the current level.")]
    [SerializeField] private bool retryResetsProgress = true;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (panelRoot == null)
            BuildFallbackUI();

        HidePanel();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Call this when the player dies to show the lose screen.
    /// Can be called from GameStateManager, HealthComponent.OnDeath, etc.
    /// </summary>
    public static void Show()
    {
        if (Instance != null)
            Instance.ShowPanel();
        else
            Debug.LogWarning("[LoseScreen] No LoseScreen instance found in scene!");
    }

    // ── Panel ──────────────────────────────────────────────────────────────

    private void ShowPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);

        int stagesCleared = WaveManager.Instance != null ? WaveManager.Instance.StagesCleared : 0;
        int levelReached  = WaveManager.Instance != null ? WaveManager.Instance.CurrentLevel  : 1;

        if (titleLabel != null)
            titleLabel.text = "You Died";

        if (stagesClearedLabel != null)
            stagesClearedLabel.text = stagesCleared == 0
                ? "No stages cleared"
                : stagesCleared == 1
                    ? "1 Stage Cleared"
                    : $"{stagesCleared} Stages Cleared";

        if (levelReachedLabel != null)
            levelReachedLabel.text = $"Reached Level {levelReached}";

        Time.timeScale = 0f;
        Debug.Log($"[LoseScreen] Player died. StagesCleared={stagesCleared} LevelReached={levelReached}");
    }

    private void HidePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ── Buttons ────────────────────────────────────────────────────────────

    public void OnMainMenuClicked()
    {
        Time.timeScale = 1f;
        WaveManager.Instance?.ResetToLevel1();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void OnRetryClicked()
    {
        Time.timeScale = 1f;
        HidePanel();

        if (retryResetsProgress)
            WaveManager.Instance?.ResetToLevel1();

        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen != null)
            levelGen.GenerateLevel();
        else
            Debug.LogError("[LoseScreen] No LevelGenerator found for retry!");
    }

    // ── Fallback UI ────────────────────────────────────────────────────────

    private void BuildFallbackUI()
    {
        GameObject canvasGO = new GameObject("LoseScreenCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        panelRoot = new GameObject("LosePanel");
        panelRoot.transform.SetParent(canvasGO.transform, false);
        RectTransform pr = panelRoot.AddComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.25f, 0.2f);
        pr.anchorMax = new Vector2(0.75f, 0.8f);
        pr.offsetMin = pr.offsetMax = Vector2.zero;
        panelRoot.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.97f);

        titleLabel         = MakeLabel(panelRoot, "Title",         new Vector2(0f, 0.78f), new Vector2(1f, 0.97f), 36, new Color(0.9f, 0.2f, 0.2f));
        stagesClearedLabel = MakeLabel(panelRoot, "StagesCleared", new Vector2(0f, 0.60f), new Vector2(1f, 0.78f), 24, Color.white);
        levelReachedLabel  = MakeLabel(panelRoot, "LevelReached",  new Vector2(0f, 0.46f), new Vector2(1f, 0.60f), 20, new Color(0.7f, 0.7f, 0.7f));

        retryButton = MakeButton(panelRoot, "Retry",
            new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.44f),
            new Color(0.2f, 0.4f, 0.7f));
        retryButton.onClick.AddListener(OnRetryClicked);

        mainMenuButton = MakeButton(panelRoot, "Main Menu",
            new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.22f),
            new Color(0.55f, 0.15f, 0.15f));
        mainMenuButton.onClick.AddListener(OnMainMenuClicked);
    }

    private TextMeshProUGUI MakeLabel(GameObject parent, string name,
                                      Vector2 anchorMin, Vector2 anchorMax,
                                      float fontSize, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize  = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = color;
        return tmp;
    }

    private Button MakeButton(GameObject parent, string label,
                               Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject go = new GameObject(label + "Btn");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = color;
        var btn = go.AddComponent<Button>();

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        return btn;
    }
}