using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

public class AiDemoDpsLabelUi : MonoBehaviour
{
    // rounding modes for shown numbers
    public enum DpsRoundingMode
    {
        None, // as is
        Integer, // to integer
        Nearest5 // to nearest 5
    }

    private class LabelView
    {
        public GameObject rootObject;
        public RectTransform root;
        public TextMeshProUGUI text;

        // smoothed values for display
        public float displayedDps;
        public float displayedHps;
    }

    [Header("Camera")]
    [SerializeField] private Camera targetCamera;

    [Header("UI")]
    [SerializeField] private bool autoCreateCanvas = true;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Vector2 labelSize = new Vector2(190f, 72f);

    [Header("Position")]
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 24f);

    [Header("Visibility")]
    [SerializeField] private bool hideWhenOutsideScreen = true;
    [SerializeField] private float screenPadding = 32f;

    [Header("Style")]
    [SerializeField] private int fontSize = 18;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color textColor = Color.white;

    [Header("Formatting")]
    [SerializeField] private DpsRoundingMode dpsRounding = DpsRoundingMode.Integer; // integer by default

    [Header("Smoothing")]
    [SerializeField] private float uiSmoothSpeed = 5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private float debugLogInterval = 1f;

    private World world;
    private EntityManager entityManager;
    private EntityQuery labelQuery;
    private bool hasLabelQuery;
    private float debugTimer;

    private RectTransform canvasRect;

    private readonly Dictionary<Entity, LabelView> views = new Dictionary<Entity, LabelView>();
    private readonly HashSet<Entity> aliveThisFrame = new HashSet<Entity>();
    private readonly List<Entity> removeBuffer = new List<Entity>();

    private void Awake()
    {
        if (autoCreateCanvas && targetCanvas == null)
        {
            CreateCanvas();
        }

        if (targetCanvas != null)
        {
            canvasRect = targetCanvas.transform as RectTransform;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Start()
    {
        world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)
        {
            Debug.LogWarning("AiDemoDpsLabelUi: DOTS World was not found.");
            enabled = false;
            return;
        }

        entityManager = world.EntityManager;

        labelQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<AiDemoDpsLabel>(),
            ComponentType.ReadOnly<LocalToWorld>());

        hasLabelQuery = true;

        if (targetCamera == null)
        {
            Debug.LogWarning("AiDemoDpsLabelUi: Target Camera is null. Assign your gameplay camera manually.");
        }

        if (targetCanvas == null)
        {
            Debug.LogWarning("AiDemoDpsLabelUi: Target Canvas is null.");
        }
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCanvas != null && canvasRect == null)
        {
            canvasRect = targetCanvas.transform as RectTransform;
        }

        if (targetCamera == null || targetCanvas == null || canvasRect == null
            || world == null || !world.IsCreated || !hasLabelQuery)
        {
            HideAll();
            return;
        }

        aliveThisFrame.Clear();

        NativeArray<Entity> entities = labelQuery.ToEntityArray(Allocator.Temp);
        NativeArray<AiDemoDpsLabel> labels = labelQuery.ToComponentDataArray<AiDemoDpsLabel>(Allocator.Temp);
        NativeArray<LocalToWorld> transforms = labelQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

        if (debugLogs)
        {
            debugTimer -= Time.unscaledDeltaTime;

            if (debugTimer <= 0f)
            {
                debugTimer = debugLogInterval;
                Debug.Log($"AiDemoDpsLabelUi: found DPS label entities = {entities.Length}, camera = {targetCamera.name}");
            }
        }

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            AiDemoDpsLabel label = labels[i];
            LocalToWorld localToWorld = transforms[i];

            aliveThisFrame.Add(entity);

            if (!views.TryGetValue(entity, out LabelView view))
            {
                view = CreateLabelView(entity);
                views.Add(entity, view);
            }

            // smooth displayed values
            float t = 1f - Mathf.Exp(-uiSmoothSpeed * Time.deltaTime);
            view.displayedDps = Mathf.Lerp(view.displayedDps, label.damagePerSecond, t);
            view.displayedHps = Mathf.Lerp(view.displayedHps, label.healPerSecond, t);

            // visibility check
            bool isChanging =
                label.damagePerSecond > 0.001f ||
                label.healPerSecond > 0.001f;

            bool shouldShow =
                !label.showOnlyWhenChanging ||
                isChanging;

            if (!shouldShow)
            {
                view.rootObject.SetActive(false);
                continue;
            }

            Vector3 worldPosition = new Vector3(
                localToWorld.Value.c3.x + label.worldOffset.x,
                localToWorld.Value.c3.y + label.worldOffset.y,
                localToWorld.Value.c3.z + label.worldOffset.z);

            Vector3 screenPosition = targetCamera.WorldToScreenPoint(worldPosition);

            if (screenPosition.z <= 0f)
            {
                view.rootObject.SetActive(false);
                continue;
            }

            if (hideWhenOutsideScreen && IsOutsideScreen(screenPosition))
            {
                view.rootObject.SetActive(false);
                continue;
            }

            if (!TryScreenToCanvasPosition(screenPosition, out Vector2 canvasPosition))
            {
                view.rootObject.SetActive(false);
                continue;
            }

            view.rootObject.SetActive(true);
            view.root.anchoredPosition = canvasPosition + screenOffset;
            view.text.text = BuildText(label, view);
        }

        entities.Dispose();
        labels.Dispose();
        transforms.Dispose();

        CleanupDestroyedEntities();
    }

    private string BuildText(AiDemoDpsLabel label, LabelView view)
    {
        string prefix = label.labelPrefix.ToString();

        // apply rounding
        float dps = view.displayedDps;
        float hps = view.displayedHps;

        switch (dpsRounding)
        {
            case DpsRoundingMode.Integer:
                dps = Mathf.Round(dps);
                hps = Mathf.Round(hps);
                break;
            case DpsRoundingMode.Nearest5:
                dps = Mathf.Round(dps / 5f) * 5f;
                hps = Mathf.Round(hps / 5f) * 5f;
                break;
        }

        string result;

        if (dpsRounding == DpsRoundingMode.None)
            result = $"{prefix}: {dps:0.#}";
        else
            result = $"{prefix}: {dps:0}"; // integers

        if (label.showHealing && hps > 0.001f)
        {
            if (dpsRounding == DpsRoundingMode.None)
                result += $"\nHeal/s: {hps:0.#}";
            else
                result += $"\nHeal/s: {hps:0}";
        }

        if (label.showHealth)
        {
            result += $"\nHealth: {label.currentHealth:0}/{label.maxHealth:0}";
        }

        return result;
    }

    private bool IsOutsideScreen(Vector3 screenPosition)
    {
        if (screenPosition.x < -screenPadding) return true;
        if (screenPosition.x > Screen.width + screenPadding) return true;
        if (screenPosition.y < -screenPadding) return true;
        if (screenPosition.y > Screen.height + screenPadding) return true;
        return false;
    }

    private bool TryScreenToCanvasPosition(Vector3 screenPosition, out Vector2 canvasPosition)
    {
        Camera canvasCamera = null;

        if (targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            canvasCamera = targetCanvas.worldCamera;
            if (canvasCamera == null)
            {
                canvasCamera = targetCamera;
            }
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPosition,
            canvasCamera,
            out canvasPosition);
    }

    private LabelView CreateLabelView(Entity entity)
    {
        GameObject rootObject = new GameObject(
            $"DPS Label {entity.Index}:{entity.Version}",
            typeof(RectTransform),
            typeof(Image));

        rootObject.transform.SetParent(targetCanvas.transform, false);

        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = labelSize;
        root.anchoredPosition = Vector2.zero;

        Image image = rootObject.GetComponent<Image>();
        image.color = backgroundColor;
        image.raycastTarget = false;

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));

        textObject.transform.SetParent(rootObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 4f);
        textRect.offsetMax = new Vector2(-6f, -4f);

        TextMeshProUGUI tmp = textObject.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        return new LabelView
        {
            rootObject = rootObject,
            root = root,
            text = tmp,
            displayedDps = 0f,
            displayedHps = 0f
        };
    }

    private void CleanupDestroyedEntities()
    {
        removeBuffer.Clear();

        foreach (KeyValuePair<Entity, LabelView> pair in views)
        {
            if (!aliveThisFrame.Contains(pair.Key))
            {
                removeBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeBuffer.Count; i++)
        {
            Entity entity = removeBuffer[i];

            if (views.TryGetValue(entity, out LabelView view))
            {
                if (view.rootObject != null)
                {
                    Destroy(view.rootObject);
                }

                views.Remove(entity);
            }
        }
    }

    private void HideAll()
    {
        foreach (KeyValuePair<Entity, LabelView> pair in views)
        {
            if (pair.Value.rootObject != null)
            {
                pair.Value.rootObject.SetActive(false);
            }
        }
    }

    private void CreateCanvas()
    {
        GameObject canvasObject = new GameObject(
            "AI Demo DPS Label Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        targetCanvas = canvasObject.GetComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 9998;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasRect = targetCanvas.transform as RectTransform;
    }

    private void OnDestroy()
    {
        DestroyAllViews();
        ClearDotsReferencesWithoutDisposingQuery();
    }

    private void DestroyAllViews()
    {
        foreach (KeyValuePair<Entity, LabelView> pair in views)
        {
            if (pair.Value.rootObject != null)
            {
                Destroy(pair.Value.rootObject);
            }
        }

        views.Clear();
        aliveThisFrame.Clear();
        removeBuffer.Clear();
    }

    private void ClearDotsReferencesWithoutDisposingQuery()
    {
        // don't Dispose labelQuery in OnDestroy - on shutdown DOTS world can be already dead and it throws
        hasLabelQuery = false;
        labelQuery = default;
        entityManager = default;
        world = null;
    }
}