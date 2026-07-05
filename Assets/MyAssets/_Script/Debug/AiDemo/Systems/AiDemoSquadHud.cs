using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

// squad HUD for demo, toggle with F3
public class AiDemoSquadHud : MonoBehaviour
{
    [Header("Camera / UI")]
    [SerializeField] private bool autoCreateCanvas = true;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Vector2 panelSize = new Vector2(420f, 360f);
    [SerializeField] private Vector2 panelOffset = new Vector2(20f, 20f);

    [Header("Style")]
    [SerializeField] private int fontSize = 13;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color titleColor = new Color(1f, 0.95f, 0.5f, 1f);

    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F3;
    [SerializeField] private bool startVisible = true;

    [Header("Refresh")]
    [SerializeField] private float refreshInterval = 0.25f;

    private World world;
    private EntityManager em;
    private EntityQuery squadQuery;
    private bool hasQuery;

    private GameObject panelRoot;
    private TextMeshProUGUI bodyText;
    private TextMeshProUGUI titleText;
    private bool visible;
    private float refreshTimer;

    private readonly StringBuilder sb = new StringBuilder(2048);

    private void Awake()
    {
        if (autoCreateCanvas && targetCanvas == null) CreateCanvas();
        BuildPanel();
        visible = startVisible;
        panelRoot.SetActive(visible);
    }

    private void Start()
    {
        world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            Debug.LogWarning("AiDemoSquadHud: DOTS World not found.");
            enabled = false;
            return;
        }
        em = world.EntityManager;
        squadQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<SquadComponent>(),
            ComponentType.ReadOnly<SquadronTag>());
        hasQuery = true;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
            panelRoot.SetActive(visible);
        }
        if (!visible || !hasQuery) return;

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = refreshInterval;

        sb.Clear();

        NativeArray<Entity> entities = squadQuery.ToEntityArray(Allocator.Temp);
        sb.AppendLine($"<b>Squads: {entities.Length}</b>");
        sb.AppendLine();

        // query order is ok for this small HUD
        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            if (!em.Exists(e)) continue;

            SquadComponent sq = em.GetComponentData<SquadComponent>(e);

            string factionTag = sq.faction == Faction.Friendly
                ? "<color=#4FB0FF>FR</color>"
                : "<color=#FF6E5C>EN</color>";

            // show first queued command if exists
            string cmdStr = "-";
            if (em.HasBuffer<SquadCommandElement>(e))
            {
                DynamicBuffer<SquadCommandElement> cmds = em.GetBuffer<SquadCommandElement>(e);
                if (cmds.Length > 0)
                {
                    cmdStr = cmds[0].type.ToString();
                    if (cmds.Length > 1) cmdStr += $" (+{cmds.Length - 1})";
                }
            }

            sb.AppendLine($"<b>S{sq.squadId}</b> {factionTag} <i>{sq.role}</i>");
            sb.AppendLine($"  Form:{sq.formation}  Alive:{sq.aliveCount}/{sq.maxMembers}");
            sb.AppendLine($"  Fire:{sq.defaultFireMode}  Move:{sq.defaultMoveMode}");
            sb.AppendLine($"  Cmd:{cmdStr}");
            sb.AppendLine();
        }
        entities.Dispose();

        bodyText.text = sb.ToString();
    }

    // UI build.

    private void CreateCanvas()
    {
        GameObject canvasGo = new GameObject("AiDemoSquadHudCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas c = canvasGo.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 5100;
        targetCanvas = c;
    }

    private void BuildPanel()
    {
        panelRoot = new GameObject("AiDemoSquadPanel", typeof(RectTransform), typeof(Image));
        panelRoot.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = panelRoot.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = panelSize;
        rect.anchoredPosition = panelOffset;

        Image bg = panelRoot.GetComponent<Image>();
        bg.color = backgroundColor;
        bg.raycastTarget = false;

        // Title.
        GameObject titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(panelRoot.transform, false);
        RectTransform titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.sizeDelta = new Vector2(0f, 24f);
        titleRect.anchoredPosition = new Vector2(8f, -4f);

        titleText = titleGo.AddComponent<TextMeshProUGUI>();
        titleText.text = "<b>SQUAD HUD</b>  <size=10>(F3 toggle)</size>";
        titleText.fontSize = 14;
        titleText.color = titleColor;
        titleText.alignment = TextAlignmentOptions.TopLeft;
        titleText.raycastTarget = false;

        // Body.
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
