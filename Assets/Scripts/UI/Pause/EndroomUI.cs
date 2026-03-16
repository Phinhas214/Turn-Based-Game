using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Attach to a trigger collider in the End room.
/// Shows a panel when the player enters with:
///   - Level complete header
///   - Stages cleared counter
///   - Next Level button (advances difficulty, regenerates in same scene)
///   - Main Menu button (resets and loads main menu)
/// </summary>
public class EndRoomUI : MonoBehaviour
{
    [Header("Main Menu")]
    [Tooltip("Scene name to load when the player clicks Main Menu.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("UI References (wire up in Inspector, or fallback UI auto-builds)")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button     nextLevelButton;
    [SerializeField] private Button     mainMenuButton;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI stagesClearedLabel;
    [SerializeField] private TextMeshProUGUI nextLevelLabel;

    [Header("Trigger")]
    [SerializeField] private string playerTag = "Player";

    private bool _triggered = false;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (panelRoot == null)
            BuildFallbackUI();

        HidePanel();
    }

    // ── Trigger ────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (!other.CompareTag(playerTag)) return;
        _triggered = true;
        ShowPanel();
    }

    // ── Panel ──────────────────────────────────────────────────────────────

    private void ShowPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);

        int current       = WaveManager.Instance != null ? WaveManager.Instance.CurrentLevel  : 1;
        int stagesCleared = WaveManager.Instance != null ? WaveManager.Instance.StagesCleared : 0;

        if (levelLabel != null)
            levelLabel.text = $"Level {current} Complete!";

        if (stagesClearedLabel != null)
            stagesClearedLabel.text = $"Stages Cleared: {stagesCleared}";

        if (nextLevelLabel != null)
            nextLevelLabel.text = $"Next: Level {current + 1}";

        Time.timeScale = 0f;
        Debug.Log($"[EndRoomUI] Stage complete. Level={current} StagesCleared={stagesCleared}");
    }

    private void HidePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ── Buttons ────────────────────────────────────────────────────────────

    public void OnNextLevelClicked()
    {
        Time.timeScale = 1f;
        HidePanel();
        _triggered = false;

        WaveManager.Instance?.AdvanceLevel();

        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen != null)
            levelGen.GenerateLevel();
        else
            Debug.LogError("[EndRoomUI] No LevelGenerator found!");
    }

    public void OnMainMenuClicked()
    {
        Time.timeScale = 1f;
        WaveManager.Instance?.ResetToLevel1();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ── Fallback UI ────────────────────────────────────────────────────────

    private void BuildFallbackUI()
    {
        GameObject canvasGO = new GameObject("EndRoomCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        panelRoot = new GameObject("EndRoomPanel");
        panelRoot.transform.SetParent(canvasGO.transform, false);
        RectTransform pr = panelRoot.AddComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.3f, 0.2f);
        pr.anchorMax = new Vector2(0.7f, 0.8f);
        pr.offsetMin = pr.offsetMax = Vector2.zero;
        panelRoot.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.96f);

        levelLabel        = MakeLabel(panelRoot, "LevelLabel",        new Vector2(0f, 0.78f), new Vector2(1f, 0.97f), 28, new Color(1f, 0.85f, 0.2f));
        stagesClearedLabel = MakeLabel(panelRoot, "StagesCleared",    new Vector2(0f, 0.62f), new Vector2(1f, 0.78f), 22, Color.white);
        nextLevelLabel     = MakeLabel(panelRoot, "NextLevelPreview", new Vector2(0f, 0.48f), new Vector2(1f, 0.62f), 18, new Color(0.7f, 0.7f, 0.7f));

        nextLevelButton = MakeButton(panelRoot, "Next Level",
            new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.46f),
            new Color(0.15f, 0.55f, 0.15f));
        nextLevelButton.onClick.AddListener(OnNextLevelClicked);

        mainMenuButton = MakeButton(panelRoot, "Main Menu",
            new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.24f),
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
        tmp.text = label; tmp.fontSize = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        return btn;
    }
}