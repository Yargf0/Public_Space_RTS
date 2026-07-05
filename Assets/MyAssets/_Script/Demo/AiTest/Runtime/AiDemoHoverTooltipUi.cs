using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AiDemoHoverTooltipUi : MonoBehaviour
{
    [Header("Pick")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool ignoreWhenPointerOverUi = true;

    [Header("UI")]
    [SerializeField] private bool autoCreateUi = true;
    [SerializeField] private RectTransform tooltipRoot;
    [SerializeField] private TextMeshProUGUI tooltipText;

    [Header("Position")]
    [SerializeField] private Vector2 screenOffset = new Vector2(24f, -24f);
    [SerializeField] private Vector2 tooltipSize = new Vector2(360f, 140f);

    private World world;
    private EntityManager entityManager;
    private EntityQuery gridDataQuery;
    private bool hasGridDataQuery;
    private Canvas canvas;
    private bool initialized;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (autoCreateUi && (tooltipRoot == null || tooltipText == null))
        {
            CreateUi();
        }

        Hide();
    }

    private void Start()
    {
        TryInitializeWorld();
    }

    private void Update()
    {
        if (!EnsureReady())
        {
            Hide();
            return;
        }

        if (ignoreWhenPointerOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            Hide();
            return;
        }

        if (InputProvider.Instance == null)
        {
            Hide();
            return;
        }

        if (!hasGridDataQuery || gridDataQuery.IsEmpty)
        {
            Hide();
            return;
        }

        GridData gridData = gridDataQuery.GetSingleton<GridData>();
        Vector3 worldPointer = InputProvider.Instance.GetWorldPointerPosition();
        float2 worldPoint = new float2(worldPointer.x, worldPointer.y);

        if (!GridPickUtility.TryPickShipAtWorldPoint(
                entityManager,
                in gridData,
                worldPoint,
                out Entity hit))
        {
            Hide();
            return;
        }

        Entity tooltipEntity = ResolveTooltipEntity(hit);

        if (tooltipEntity == Entity.Null)
        {
            Hide();
            return;
        }

        if (!entityManager.Exists(tooltipEntity) || !entityManager.HasComponent<AiDemoHoverTooltip>(tooltipEntity))
        {
            Hide();
            return;
        }

        AiDemoHoverTooltip tooltip = entityManager.GetComponentData<AiDemoHoverTooltip>(tooltipEntity);

        if (tooltip.text.IsEmpty)
        {
            Hide();
            return;
        }

        Show(tooltip.text.ToString());
    }

    private bool EnsureReady()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null || tooltipRoot == null || tooltipText == null)
        {
            return false;
        }

        if (!initialized)
        {
            TryInitializeWorld();
        }

        if (!initialized)
        {
            return false;
        }

        if (world == null || !world.IsCreated || world != World.DefaultGameObjectInjectionWorld)
        {
            DisposeGridDataQuery();
            initialized = false;
            return false;
        }

        return true;
    }

    private void TryInitializeWorld()
    {
        world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)
        {
            return;
        }

        DisposeGridDataQuery();
        entityManager = world.EntityManager;
        gridDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<GridData>());
        hasGridDataQuery = true;
        initialized = true;
    }

    private void OnDestroy()
    {
        DisposeGridDataQuery();
    }

    private void DisposeGridDataQuery()
    {
        if (!hasGridDataQuery)
        {
            return;
        }

        if (world != null && world.IsCreated)
        {
            gridDataQuery.Dispose();
        }

        hasGridDataQuery = false;
    }

    private Entity ResolveTooltipEntity(Entity hitEntity)
    {
        Entity current = hitEntity;

        // hit can be child entity, climb up parents to find the ship
        for (int i = 0; i < 8; i++)
        {
            if (current == Entity.Null || !entityManager.Exists(current))
            {
                return Entity.Null;
            }

            if (entityManager.HasComponent<AiDemoHoverTooltip>(current))
            {
                return current;
            }

            if (!entityManager.HasComponent<Parent>(current))
            {
                return Entity.Null;
            }

            current = entityManager.GetComponentData<Parent>(current).Value;
        }

        return Entity.Null;
    }

    private void Show(string text)
    {
        tooltipText.text = text;

        if (!tooltipRoot.gameObject.activeSelf)
        {
            tooltipRoot.gameObject.SetActive(true);
        }

        UpdateTooltipPosition();
    }

    private void Hide()
    {
        if (tooltipRoot != null && tooltipRoot.gameObject.activeSelf)
        {
            tooltipRoot.gameObject.SetActive(false);
        }
    }

    private void UpdateTooltipPosition()
    {
        Vector2 screenPosition = (Vector2)Input.mousePosition + screenOffset;

        float pivotX = screenPosition.x > Screen.width * 0.65f ? 1f : 0f;
        float pivotY = screenPosition.y < Screen.height * 0.35f ? 0f : 1f;
        tooltipRoot.pivot = new Vector2(pivotX, pivotY);

        screenPosition.x = Mathf.Clamp(screenPosition.x, 8f, Screen.width - 8f);
        screenPosition.y = Mathf.Clamp(screenPosition.y, 8f, Screen.height - 8f);

        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            tooltipRoot.position = screenPosition;
            return;
        }

        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null)
        {
            tooltipRoot.position = screenPosition;
            return;
        }

        Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition,
                canvasCamera,
                out Vector2 localPoint))
        {
            tooltipRoot.anchoredPosition = localPoint;
        }
    }

    private void CreateUi()
    {
        GameObject canvasObject = new GameObject(
            "AI Demo Tooltip Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject panelObject = new GameObject(
            "AI Demo Tooltip Panel",
            typeof(RectTransform),
            typeof(Image));

        panelObject.transform.SetParent(canvasObject.transform, false);

        tooltipRoot = panelObject.GetComponent<RectTransform>();
        tooltipRoot.sizeDelta = tooltipSize;

        Image image = panelObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.78f);
        image.raycastTarget = false;

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));

        textObject.transform.SetParent(panelObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 8f);
        textRect.offsetMax = new Vector2(-12f, -8f);

        tooltipText = textObject.GetComponent<TextMeshProUGUI>();
        tooltipText.text = string.Empty;
        tooltipText.fontSize = 22f;
        tooltipText.textWrappingMode = TextWrappingModes.Normal;
        tooltipText.raycastTarget = false;
    }
}
