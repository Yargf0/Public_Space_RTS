using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

// debug HUD for AI demo. ECS scans are throttled, label positions update every frame
public class AiDemoShipDebugHud : MonoBehaviour
{
    private class LabelView
    {
        public GameObject root;
        public RectTransform rect;
        public Image image;
        public TextMeshProUGUI text;
        public RectTransform textRect;
        public string lastText = string.Empty;
        public Color lastColor;
    }

    [Header("Camera")]
    [SerializeField] private Camera targetCamera;

    [Header("UI")]
    [SerializeField] private bool autoCreateCanvas = true;
    [SerializeField] private Canvas targetCanvas;
    [Tooltip("Used only as offscreen safety padding. Runtime label background is auto-sized by content.")]
    [SerializeField] private Vector2 labelSize = new Vector2(260f, 92f);
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 36f);
    [SerializeField] private int labelPaddingLeft = 6;
    [SerializeField] private int labelPaddingRight = 6;
    [SerializeField] private int labelPaddingTop = 4;
    [SerializeField] private int labelPaddingBottom = 4;
    [SerializeField] private bool wrapText = true;
    [SerializeField] private float minLabelTextWidth = 80f;
    [SerializeField] private float maxLabelTextWidth = 340f;

    [Header("Performance")]
    [Tooltip("Full ECS scan rate. 0.1 = 10 times per second. Positions of already visible labels are still updated every frame.")]
    [SerializeField] private float refreshInterval = 0.1f;
    [Tooltip("Hard cap for debug labels. Keeps accidental full-army HUD from killing Main Thread.")]
    [SerializeField] private int maxVisibleLabels = 50;
    [Tooltip("Disable this debug HUD in non-editor builds.")]
    [SerializeField] private bool disableInPlayerBuild = true;

    [Header("Style")]
    [SerializeField] private int fontSize = 14;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.7f);
    [SerializeField] private Color textColor = Color.white;

    [Header("Filters")]
    [SerializeField] private bool showFriendly = true;
    [SerializeField] private bool showEnemy = true;
    [SerializeField] private bool hideOffscreen = true;
    [SerializeField] private bool showOnlyShipsWithDebugHudMarker = true;

    [Header("Toggle")]
    [SerializeField] private bool useGlobalToggle = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.F2;
    [SerializeField] private bool startVisible = true;

    private World world;
    private EntityManager em;
    private EntityQuery allShipQuery;
    private EntityQuery markedShipQuery;
    private bool hasQuery;

    private RectTransform canvasRect;
    private bool localVisible;
    private float nextRefreshTime;

    private readonly Dictionary<Entity, LabelView> views = new Dictionary<Entity, LabelView>();
    private readonly HashSet<Entity> aliveThisRefresh = new HashSet<Entity>();
    private readonly List<Entity> removeBuffer = new List<Entity>();
    private readonly StringBuilder hudBuilder = new StringBuilder(128);
    private readonly StringBuilder lineBuilder = new StringBuilder(64);

    private void Awake()
    {
#if UNITY_EDITOR
        _ = disableInPlayerBuild;
#endif

#if !UNITY_EDITOR
        if (disableInPlayerBuild)
        {
            enabled = false;
            return;
        }
#endif

        if (targetCamera == null) { targetCamera = Camera.main; }
        if (autoCreateCanvas && targetCanvas == null) { CreateCanvas(); }
        if (targetCanvas != null) { canvasRect = targetCanvas.transform as RectTransform; }

        localVisible = startVisible;
        if (useGlobalToggle) { AiDemoDebugGlobalToggle.SetVisible(startVisible); }
    }

    private void Start()
    {
        world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            Debug.LogWarning("AiDemoShipDebugHud: DOTS World not found.");
            enabled = false;
            return;
        }

        em = world.EntityManager;

        allShipQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<ShipStateComponent>(),
            ComponentType.ReadOnly<LocalToWorld>(),
            ComponentType.ReadOnly<Unit>());

        markedShipQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<ShipStateComponent>(),
            ComponentType.ReadOnly<LocalToWorld>(),
            ComponentType.ReadOnly<Unit>(),
            ComponentType.ReadOnly<FightPatternDebugMarker>());

        hasQuery = true;
        nextRefreshTime = 0f;
    }

    private void Update()
    {
        UpdateToggle();

        if (!IsVisible())
        {
            HideAll();
            return;
        }

        if (targetCamera == null) { targetCamera = Camera.main; }
        if (!hasQuery || targetCamera == null || targetCanvas == null || canvasRect == null) { return; }

        UpdateVisibleLabelPositions();

        float now = Time.unscaledTime;
        float interval = Mathf.Max(0.02f, refreshInterval);
        if (now < nextRefreshTime) { return; }
        nextRefreshTime = now + interval;

        RefreshLabels();
    }

    private void RefreshLabels()
    {
        aliveThisRefresh.Clear();

        EntityQuery query = showOnlyShipsWithDebugHudMarker ? markedShipQuery : allShipQuery;
        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

        int visibleCount = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            if (visibleCount >= maxVisibleLabels) { break; }

            Entity e = entities[i];
            if (!em.Exists(e)) { continue; }

            FightPatternDebugMarker marker;
            if (showOnlyShipsWithDebugHudMarker)
            {
                marker = em.GetComponentData<FightPatternDebugMarker>(e);
                if (marker.DrawHud == 0) { continue; }
            }
            else
            {
                marker = GetHudMarkerOrDefault(e);
                if (marker.DrawHud == 0) { continue; }
            }

            Unit unit = em.GetComponentData<Unit>(e);
            Faction faction = unit.faction;

            if (faction == Faction.Friendly && !showFriendly) { continue; }
            if (faction == Faction.Enemy && !showEnemy) { continue; }

            LocalToWorld ltw = em.GetComponentData<LocalToWorld>(e);
            Vector3 screenPos = targetCamera.WorldToScreenPoint(ltw.Position);
            if (IsOffscreen(screenPos)) { continue; }

            ShipStateComponent ship = em.GetComponentData<ShipStateComponent>(e);

            FightLogicType fightType = default;
            string phaseStr = "-";
            bool hasFight = em.HasComponent<FightLogic>(e);
            if (hasFight)
            {
                FightLogic fl = em.GetComponentData<FightLogic>(e);
                fightType = fl.movementType;
            }

            if (em.HasComponent<FightPatternState>(e))
            {
                FightPatternState ps = em.GetComponentData<FightPatternState>(e);
                if ((byte)ps.activePattern != byte.MaxValue)
                {
                    fightType = ps.activePattern;
                    phaseStr = PhaseToText(ps.activePattern, ps.phase);
                }
            }

            int squadId = -1;
            if (em.HasComponent<ShipSquadRef>(e))
            {
                ShipSquadRef sref = em.GetComponentData<ShipSquadRef>(e);
                if (sref.squad != Entity.Null && em.Exists(sref.squad) && em.HasComponent<SquadComponent>(sref.squad))
                {
                    squadId = em.GetComponentData<SquadComponent>(sref.squad).squadId;
                }
            }

            string targetStr = "-";
            if (em.HasComponent<Target>(e))
            {
                Target t = em.GetComponentData<Target>(e);
                if (t.targetEntity != Entity.Null && em.Exists(t.targetEntity))
                {
                    targetStr = "#" + t.targetEntity.Index;
                }
            }

            LabelView view;
            if (!views.TryGetValue(e, out view))
            {
                view = CreateLabelView();
                views.Add(e, view);
            }

            Color factionTint = faction == Faction.Friendly
                ? new Color(0.10f, 0.45f, 0.95f, backgroundColor.a)
                : new Color(0.85f, 0.20f, 0.20f, backgroundColor.a);

            if (view.lastColor != factionTint)
            {
                view.image.color = factionTint;
                view.lastColor = factionTint;
            }

            string sizeName = ((ShipSize)unit.shipSize).ToString();
            string fightStr = hasFight ? fightType.ToString() : "-";
            string infoText = BuildHudText(marker, sizeName, ship, fightStr, phaseStr, squadId, targetStr);
            if (string.IsNullOrEmpty(infoText)) { continue; }

            if (view.lastText != infoText)
            {
                view.text.text = infoText;
                view.lastText = infoText;
                ResizeTextToContent(view, infoText);
            }

            view.root.SetActive(true);
            view.rect.anchoredPosition = ScreenToCanvas(screenPos) + screenOffset;
            aliveThisRefresh.Add(e);
            visibleCount++;
        }

        entities.Dispose();
        RemoveDeadLabels();
    }

    private void UpdateVisibleLabelPositions()
    {
        if (views.Count == 0) { return; }

        foreach (var kv in views)
        {
            Entity e = kv.Key;
            LabelView view = kv.Value;
            if (view == null || view.root == null) { continue; }

            if (!em.Exists(e) || !em.HasComponent<LocalToWorld>(e))
            {
                view.root.SetActive(false);
                continue;
            }

            LocalToWorld ltw = em.GetComponentData<LocalToWorld>(e);
            Vector3 screenPos = targetCamera.WorldToScreenPoint(ltw.Position);

            if (IsOffscreen(screenPos))
            {
                view.root.SetActive(false);
                continue;
            }

            if (!view.root.activeSelf) { view.root.SetActive(true); }
            view.rect.anchoredPosition = ScreenToCanvas(screenPos) + screenOffset;
        }
    }

    private void UpdateToggle()
    {
        if (useGlobalToggle)
        {
            AiDemoDebugGlobalToggle.Update(toggleKey, true);
            return;
        }

        if (Input.GetKeyDown(toggleKey)) { localVisible = !localVisible; }
    }

    private bool IsVisible()
    {
        return useGlobalToggle ? AiDemoDebugGlobalToggle.Visible : localVisible;
    }

    private FightPatternDebugMarker GetHudMarkerOrDefault(Entity e)
    {
        if (em.HasComponent<FightPatternDebugMarker>(e)) { return em.GetComponentData<FightPatternDebugMarker>(e); }
        return DefaultMarker();
    }

    private static FightPatternDebugMarker DefaultMarker()
    {
        return new FightPatternDebugMarker
        {
            DrawHud = 1,
            HudShowShipSize = 1,
            HudShowShipState = 1,
            HudShowMoveMode = 1,
            HudShowFireMode = 1,
            HudShowFightPattern = 1,
            HudShowFightPhase = 1,
            HudShowSquad = 1,
            HudShowTarget = 1,
        };
    }

    private string BuildHudText(FightPatternDebugMarker marker, string sizeName, ShipStateComponent ship, string fightStr, string phaseStr, int squadId, string targetStr)
    {
        hudBuilder.Length = 0;

        lineBuilder.Length = 0;
        if (marker.HudShowShipSize != 0) { AppendToken(lineBuilder, sizeName); }
        if (marker.HudShowShipState != 0) { AppendToken(lineBuilder, $"[{ship.currentState}]"); }
        AppendLineIfNotEmpty(hudBuilder, lineBuilder);

        lineBuilder.Length = 0;
        if (marker.HudShowMoveMode != 0) { AppendToken(lineBuilder, $"Move:{ship.moveMode}"); }
        if (marker.HudShowFireMode != 0) { AppendToken(lineBuilder, $"Fire:{ship.mode}"); }
        AppendLineIfNotEmpty(hudBuilder, lineBuilder);

        lineBuilder.Length = 0;
        if (marker.HudShowFightPattern != 0) { AppendToken(lineBuilder, $"Fight:{fightStr}"); }
        AppendLineIfNotEmpty(hudBuilder, lineBuilder);

        lineBuilder.Length = 0;
        if (marker.HudShowFightPhase != 0 && phaseStr != "-") { AppendToken(lineBuilder, $"Phase:{phaseStr}"); }
        AppendLineIfNotEmpty(hudBuilder, lineBuilder);

        lineBuilder.Length = 0;
        if (marker.HudShowSquad != 0 && squadId >= 0) { AppendToken(lineBuilder, $"Squad:S{squadId}"); }
        if (marker.HudShowTarget != 0 && targetStr != "-") { AppendToken(lineBuilder, $"Target:{targetStr}"); }
        AppendLineIfNotEmpty(hudBuilder, lineBuilder);

        return hudBuilder.ToString();
    }

    private static void AppendToken(StringBuilder builder, string token)
    {
        if (string.IsNullOrEmpty(token)) { return; }
        if (builder.Length > 0) { builder.Append("  "); }
        builder.Append(token);
    }

    private static void AppendLineIfNotEmpty(StringBuilder target, StringBuilder line)
    {
        if (line.Length == 0) { return; }
        if (target.Length > 0) { target.Append('\n'); }
        target.Append(line);
    }

    private void ResizeTextToContent(LabelView view, string content)
    {
        if (view == null || view.text == null || view.textRect == null) { return; }

        float minWidth = Mathf.Max(1f, minLabelTextWidth);
        float widthLimit = Mathf.Max(minWidth, maxLabelTextWidth);

        view.text.textWrappingMode = TextWrappingModes.NoWrap;
        Vector2 preferredNoWrap = view.text.GetPreferredValues(content, 0f, 0f);

        view.text.textWrappingMode = wrapText ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        float targetWidth = wrapText ? Mathf.Min(widthLimit, preferredNoWrap.x) : preferredNoWrap.x;
        targetWidth = wrapText ? Mathf.Clamp(targetWidth, minWidth, widthLimit) : Mathf.Max(minWidth, targetWidth);

        Vector2 preferredWrapped = view.text.GetPreferredValues(content, targetWidth, 0f);
        float textHeight = Mathf.Max(fontSize + 4f, preferredWrapped.y);

        view.textRect.sizeDelta = new Vector2(targetWidth, textHeight);
        view.rect.sizeDelta = new Vector2(
            targetWidth + labelPaddingLeft + labelPaddingRight,
            textHeight + labelPaddingTop + labelPaddingBottom);
    }

    private bool IsOffscreen(Vector3 screenPos)
    {
        if (!hideOffscreen) { return false; }
        if (screenPos.z < 0f) { return true; }

        float padX = labelSize.x;
        float padY = labelSize.y;
        return screenPos.x < -padX || screenPos.x > Screen.width + padX ||
               screenPos.y < -padY || screenPos.y > Screen.height + padY;
    }

    private void RemoveDeadLabels()
    {
        removeBuffer.Clear();
        foreach (var kv in views)
        {
            if (!aliveThisRefresh.Contains(kv.Key)) { removeBuffer.Add(kv.Key); }
        }

        foreach (var e in removeBuffer)
        {
            if (views[e].root != null) { Destroy(views[e].root); }
            views.Remove(e);
        }
    }

    private static string PhaseToText(FightLogicType pattern, byte phase)
    {
        switch (pattern)
        {
            case FightLogicType.AttackRun:
            case FightLogicType.InterceptorPass:
                switch (phase)
                {
                    case 0: return "Approach";
                    case 1: return "Firing";
                    case 2: return "Breakaway";
                    case 3: return "Reposition";
                    default: return phase.ToString();
                }

            case FightLogicType.MissileAttackRun:
                switch (phase)
                {
                    case 0: return "Approach";
                    case 1: return "Launch";
                    case 2: return "Retreat";
                    case 3: return "Reload";
                    default: return phase.ToString();
                }

            default:
                return phase.ToString();
        }
    }

    private void HideAll()
    {
        foreach (var kv in views)
        {
            if (kv.Value.root != null) { kv.Value.root.SetActive(false); }
        }
    }

    private LabelView CreateLabelView()
    {
        GameObject root = new GameObject("AiDemoShipDebugLabel", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);

        Image bg = root.GetComponent<Image>();
        bg.color = backgroundColor;
        bg.raycastTarget = false;

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(root.transform, false);

        TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = "";
        text.fontSize = fontSize;
        text.color = textColor;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = wrapText ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.margin = Vector4.zero;

        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = new Vector2(labelPaddingLeft, -labelPaddingTop);
        textRect.sizeDelta = new Vector2(maxLabelTextWidth, fontSize + 4f);

        root.SetActive(false);
        return new LabelView { root = root, rect = rect, image = bg, text = text, textRect = textRect, lastColor = bg.color };
    }

    private void CreateCanvas()
    {
        GameObject canvasGo = new GameObject("AiDemoShipDebugHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas c = canvasGo.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 5000;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        targetCanvas = c;
    }

    private Vector2 ScreenToCanvas(Vector3 screenPos)
    {
        if (canvasRect == null) { return new Vector2(screenPos.x, screenPos.y); }

        Camera canvasCamera = null;
        if (targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            canvasCamera = targetCanvas.worldCamera != null ? targetCanvas.worldCamera : targetCamera;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            new Vector2(screenPos.x, screenPos.y),
            canvasCamera,
            out Vector2 pos);
        return pos;
    }
}
