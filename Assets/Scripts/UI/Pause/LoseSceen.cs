using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Shows when the player dies. Call LoseScreen.Show() from GameStateManager.
/// Place on any persistent GameObject in the game scene.
/// </summary>
public class LoseScreen : MonoBehaviour
{
    public static LoseScreen Instance { get; private set; }

    [Header("UI References (leave empty — auto-built at runtime)")]
    [SerializeField] private GameObject      panelRoot;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI stagesClearedLabel;
    [SerializeField] private TextMeshProUGUI levelReachedLabel;
    [SerializeField] private Button          retryButton;
    [SerializeField] private Button          mainMenuButton;

    [Header("Settings")]
    [SerializeField] private string mainMenuSceneName   = "MainMenu";
    [SerializeField] private bool   retryResetsProgress = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        EnsureEventSystem();
        if (panelRoot == null) BuildFallbackUI();
        HidePanel();
    }

    public static void Show()
    {
        if (Instance != null) Instance.ShowPanel();
        else Debug.LogWarning("[LoseScreen] No LoseScreen instance in scene!");
    }

    private void ShowPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        int stages = WaveManager.Instance != null ? WaveManager.Instance.StagesCleared : 0;
        int level  = WaveManager.Instance != null ? WaveManager.Instance.CurrentLevel  : 1;
        if (titleLabel         != null) titleLabel.text         = "You Died";
        if (stagesClearedLabel != null) stagesClearedLabel.text = stages == 0 ? "No stages cleared" : stages == 1 ? "1 Stage Cleared" : $"{stages} Stages Cleared";
        if (levelReachedLabel  != null) levelReachedLabel.text  = $"Reached Level {level}";
        Time.timeScale = 0f;
    }

    private void HidePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void OnRetryClicked()
    {
        Time.timeScale = 1f;
        HidePanel();
        GameStateManager.Instance?.RestartGame(retryResetsProgress);
    }

    public void OnMainMenuClicked()
    {
        Time.timeScale = 1f;
        WaveManager.Instance?.ResetToLevel1();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void BuildFallbackUI()
    {
        var cgo = new GameObject("LoseScreenCanvas");
        var c = cgo.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 200;
        cgo.AddComponent<CanvasScaler>(); cgo.AddComponent<GraphicRaycaster>();

        panelRoot = new GameObject("LosePanel");
        panelRoot.transform.SetParent(cgo.transform, false);
        var pr = panelRoot.AddComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.25f,0.2f); pr.anchorMax = new Vector2(0.75f,0.8f);
        pr.offsetMin = pr.offsetMax = Vector2.zero;
        panelRoot.AddComponent<Image>().color = new Color(0.05f,0.05f,0.05f,0.97f);

        titleLabel         = ML(panelRoot,"Title",        new Vector2(0f,0.78f),new Vector2(1f,0.97f),36,new Color(0.9f,0.2f,0.2f));
        stagesClearedLabel = ML(panelRoot,"StagesCleared",new Vector2(0f,0.60f),new Vector2(1f,0.78f),24,Color.white);
        levelReachedLabel  = ML(panelRoot,"LevelReached", new Vector2(0f,0.46f),new Vector2(1f,0.60f),20,new Color(0.7f,0.7f,0.7f));

        retryButton    = MB(panelRoot,"Retry",    new Vector2(0.08f,0.26f),new Vector2(0.92f,0.44f),new Color(0.2f,0.4f,0.7f));
        retryButton.onClick.AddListener(OnRetryClicked);
        mainMenuButton = MB(panelRoot,"Main Menu",new Vector2(0.08f,0.04f),new Vector2(0.92f,0.22f),new Color(0.55f,0.15f,0.15f));
        mainMenuButton.onClick.AddListener(OnMainMenuClicked);
    }

    private TextMeshProUGUI ML(GameObject p,string n,Vector2 a0,Vector2 a1,float s,Color col)
    {
        var go=new GameObject(n); go.transform.SetParent(p.transform,false);
        var rt=go.AddComponent<RectTransform>(); rt.anchorMin=a0; rt.anchorMax=a1; rt.offsetMin=rt.offsetMax=Vector2.zero;
        var t=go.AddComponent<TextMeshProUGUI>(); t.fontSize=s; t.alignment=TextAlignmentOptions.Center; t.color=col; return t;
    }

    private Button MB(GameObject p,string lbl,Vector2 a0,Vector2 a1,Color col)
    {
        var go=new GameObject(lbl+"Btn"); go.transform.SetParent(p.transform,false);
        var rt=go.AddComponent<RectTransform>(); rt.anchorMin=a0; rt.anchorMax=a1; rt.offsetMin=rt.offsetMax=Vector2.zero;
        go.AddComponent<Image>().color=col;
        var btn=go.AddComponent<Button>();
        var cb=btn.colors; cb.highlightedColor=col*1.4f; cb.pressedColor=col*0.6f; btn.colors=cb;
        var tgo=new GameObject("Text"); tgo.transform.SetParent(go.transform,false);
        var tmp=tgo.AddComponent<TextMeshProUGUI>(); tmp.text=lbl; tmp.fontSize=22; tmp.alignment=TextAlignmentOptions.Center; tmp.color=Color.white;
        var trt=tgo.GetComponent<RectTransform>(); trt.anchorMin=Vector2.zero; trt.anchorMax=Vector2.one; trt.offsetMin=trt.offsetMax=Vector2.zero;
        return btn;
    }
}