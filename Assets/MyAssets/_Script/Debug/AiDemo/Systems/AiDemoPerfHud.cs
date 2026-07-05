using System.Text;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

// perf HUD for demo: fps, frame time, entity counts
// F1 - this panel, F2 - all overlays
public class AiDemoPerfHud : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private bool autoCreateCanvas = true;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Vector2 panelSize = new Vector2(280f, 180f);
    [SerializeField] private Vector2 panelOffset = new Vector2(-20f, -20f);

    [Header("Style")]
    [SerializeField] private int fontSize = 13;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color titleColor = new Color(0.5f, 1f, 0.65f, 1f);

    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;
    [SerializeField] private bool startVisible = true;

    [Header("Scene UI Toggle")]
    [SerializeField] private bool toggleTargetCanvasWithGlobalDebug = true;
    [SerializeField] private KeyCode sceneUiToggleKey = KeyCode.F2;
    [SerializeField] private bool sceneUiStartsVisible = true;

    [Header("FPS")]
    [SerializeField] private float fpsSmoothing = 0.1f;

    private float smoothedFps;
    private float smoothedMs;

    private World world;
    private EntityQuery shipQuery;
    private EntityQuery squadQuery;
    private EntityQuery strikeGroupQuery;
    private EntityQuery missionDirectorQuery;
    private bool hasQueries;

    private GameObject panelRoot;
    private TextMeshProUGUI bodyText;
    private TextMeshProUGUI titleText;
    private GraphicRaycaster targetGraphicRaycaster;
    private bool visible;
    private bool localSceneUiVisible;
    private bool lastAppliedSceneUiVisible;
    private bool hasAppliedSceneUiVisibility;

    private readonly StringBuilder sb = new StringBuilder(512);

    private void Awake()
    {
        if (autoCreateCanvas && targetCanvas == null) CreateCanvas();

        if (targetCanvas != null)
            targetGraphicRaycaster = targetCanvas.GetComponent<GraphicRaycaster>();

        localSceneUiVisible = sceneUiStartsVisible;
        if (toggleTargetCanvasWithGlobalDebug)
            AiDemoDebugGlobalToggle.SetVisible(sceneUiStartsVisible);

        BuildPanel();
        visible = startVisible;
        panelRoot.SetActive(visible);
        ApplySceneUiVisibility(GetSceneUiVisible());
    }

    private void Start()
    {
        world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            EntityManager em = world.EntityManager;
            shipQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ShipStateComponent>());
            squadQuery = em.CreateEntityQuery(ComponentType.ReadOnly<SquadronTag>());
            strikeGroupQuery = em.CreateEntityQuery(ComponentType.ReadOnly<StrikeGroupTag>());
            missionDirectorQuery = em.CreateEntityQuery(ComponentType.ReadOnly<MissionDirectorTag>());
            hasQueries = true;
        }

        smoothedFps = 60f;
        smoothedMs = 16.67f;
    }

    private void Update()
    {
        HandleSceneUiToggle();

        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
            panelRoot.SetActive(visible);
        }

        HandleTimeScaleHotkeys();

        if (!visible || !GetSceneUiVisible()) return;

        // Smoothed FPS.
        float dt = Time.unscaledDeltaTime;
        float instFps = dt > 0f ? 1f / dt : 0f;
        smoothedFps = Mathf.Lerp(smoothedFps, instFps, fpsSmoothing);
        smoothedMs = Mathf.Lerp(smoothedMs, dt * 1000f, fpsSmoothing);

        sb.Clear();

        Color fpsColor = smoothedFps >= 55f ? new Color(0.5f, 1f, 0.65f) :
                         smoothedFps >= 30f ? new Color(1f, 0.85f, 0.4f) :
                                              new Color(1f, 0.4f, 0.4f);

        string fpsHex = ColorUtility.ToHtmlStringRGB(fpsColor);

        sb.AppendLine($"<color=#{fpsHex}><b>{smoothedFps:F0} FPS</b></color>  ({smoothedMs:F1} ms)");

        if (hasQueries)
        {
            int ships = shipQuery.CalculateEntityCount();
            int squads = squadQuery.CalculateEntityCount();
            int strikeGroups = strikeGroupQuery.CalculateEntityCount();
            int directors = missionDirectorQuery.CalculateEntityCount();
            int totalEntities = world.EntityManager.UniversalQuery.CalculateEntityCount();

            sb.AppendLine($"Entities: <b>{totalEntities}</b>");
            sb.AppendLine($"Ships: {ships}");
            sb.AppendLine($"Squads: {squads}  StrikeGroups: {strikeGroups}  Directors: {directors}");
        }
        else
        {
            sb.AppendLine("<color=#888>(DOTS world not ready)</color>");
        }

        sb.AppendLine();
        string tsTag = Mathf.Approximately(Time.timeScale, 0f) ? "PAUSE" : $"{Time.timeScale:F2}x";
        sb.AppendLine($"Time: <b>{tsTag}</b>  <size=10>[1=pause 2/3/4/5/6]</size>");

        bodyText.text = sb.ToString();
    }

    private void HandleSceneUiToggle()
    {
        if (toggleTargetCanvasWithGlobalDebug)
        {
            AiDemoDebugGlobalToggle.Update(sceneUiToggleKey, true);
        }
        else if (Input.GetKeyDown(sceneUiToggleKey))
        {
            localSceneUiVisible = !localSceneUiVisible;
        }

        ApplySceneUiVisibility(GetSceneUiVisible());
    }

    private bool GetSceneUiVisible()
    {
        return toggleTargetCanvasWithGlobalDebug
            ? AiDemoDebugGlobalToggle.Visible
            : localSceneUiVisible;
    }

    private void ApplySceneUiVisibility(bool isVisible)
    {
        if (hasAppliedSceneUiVisibility && lastAppliedSceneUiVisible == isVisible)
            return;

        hasAppliedSceneUiVisibility = true;
        lastAppliedSceneUiVisible = isVisible;

        if (targetCanvas != null)
            targetCanvas.enabled = isVisible;

        if (targetGraphicRaycaster != null)
            targetGraphicRaycaster.enabled = isVisible;
    }

    private void HandleTimeScaleHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) Time.timeScale = 0f;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) Time.timeScale = 0.25f;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) Time.timeScale = 0.5f;
        else if (Input.GetKeyDown(KeyCode.Alpha4)) Time.timeScale = 1f;
        else if (Input.GetKeyDown(KeyCode.Alpha5)) Time.timeScale = 2f;
        else if (Input.GetKeyDown(KeyCode.Alpha6)) Time.timeScale = 4f;
    }

    // UI build.

    private void CreateCanvas()
    {
        GameObject canvasGo = new GameObject("AiDemoPerfHudCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas c = canvasGo.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 5300;
        targetCanvas = c;
    }

    private void BuildPanel()
    {
        panelRoot = new GameObject("AiDemoPerfPanel", typeof(RectTransform), typeof(Image));
        panelRoot.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = panelRoot.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = panelSize;
        rect.anchoredPosition = panelOffset;

        Image bg = panelRoot.GetComponent<Image>();
        bg.color = backgroundColor;
        bg.raycastTarget = false;

        GameObject titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(panelRoot.transform, false);
        RectTransform titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.sizeDelta = new Vector2(0f, 24f);
        titleRect.anchoredPosition = new Vector2(8f, -4f);

        titleText = titleGo.AddComponent<TextMeshProUGUI>();
        titleText.text = "<b>PERF HUD</b>  <size=10>(F1 toggle, F2 UI)</size>";
        titleText.fontSize = 14;
        titleText.color = titleColor;
        titleText.alignment = TextAlignmentOptions.TopLeft;
        titleText.raycastTarget = false;

        GameObject bodyGo = new GameObject("Body", typeof(RectTransform));
        bodyGo.transform.SetParent(panelRoot.transform, false);
        RectTransform bodyRect = bodyGo.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(8f, 8f);
        bodyRect.offsetMax = new Vector2(-8f, -28f);

        bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
        bodyText.text = "";
        bodyText.fontSize = fontSize;
        bodyText.color = Color.white;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.raycastTarget = false;
        bodyText.richText = true;
    }
}
