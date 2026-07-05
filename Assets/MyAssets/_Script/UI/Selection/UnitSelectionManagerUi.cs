using UnityEngine;

public class UnitSelectionManagerUi : MonoBehaviour
{
    [SerializeField] private RectTransform canvasRectTransform;
    [SerializeField] private RectTransform selectionAreaRectTransform;
    [SerializeField] private Canvas canvas;

    private Camera UiCamera
    {
        get
        {
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }
    }

    private void Awake()
    {
        selectionAreaRectTransform.anchorMin = Vector2.zero;
        selectionAreaRectTransform.anchorMax = Vector2.zero;
        selectionAreaRectTransform.pivot = Vector2.zero;
        selectionAreaRectTransform.gameObject.SetActive(false);
    }

    private void Start()
    {
        UnitSelectionManager.Instance.OnSelectionAreaStart += UnitSelectionManager_OnSelectionAreaStart;
        UnitSelectionManager.Instance.OnSelectionAreaEnd += UnitSelectionManager_OnSelectionAreaEnd;
    }

    private void OnDestroy()
    {
        if (UnitSelectionManager.Instance == null)
        {
            return;
        }

        UnitSelectionManager.Instance.OnSelectionAreaStart -= UnitSelectionManager_OnSelectionAreaStart;
        UnitSelectionManager.Instance.OnSelectionAreaEnd -= UnitSelectionManager_OnSelectionAreaEnd;
    }

    private void Update()
    {
        if (selectionAreaRectTransform.gameObject.activeSelf)
        {
            UpdateVisual();
        }
    }

    private void UnitSelectionManager_OnSelectionAreaStart(object sender, System.EventArgs e)
    {
        selectionAreaRectTransform.gameObject.SetActive(true);
        UpdateVisual();
    }

    private void UnitSelectionManager_OnSelectionAreaEnd(object sender, System.EventArgs e)
    {
        selectionAreaRectTransform.gameObject.SetActive(false);
    }

    private void UpdateVisual()
    {
        Rect selectionAreaRect = UnitSelectionManager.Instance.GetSelectionAreaRect();

        Vector2 screenMin = new Vector2(selectionAreaRect.xMin, selectionAreaRect.yMin);
        Vector2 screenMax = new Vector2(selectionAreaRect.xMax, selectionAreaRect.yMax);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRectTransform,
            screenMin,
            UiCamera,
            out Vector2 localMin);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRectTransform,
            screenMax,
            UiCamera,
            out Vector2 localMax);

        Vector2 canvasOffset = canvasRectTransform.rect.size * 0.5f;

        localMin += canvasOffset;
        localMax += canvasOffset;

        Vector2 min = Vector2.Min(localMin, localMax);
        Vector2 max = Vector2.Max(localMin, localMax);

        selectionAreaRectTransform.anchoredPosition = min;
        selectionAreaRectTransform.sizeDelta = max - min;
    }
}