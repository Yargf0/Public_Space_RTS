using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class UiUnitCell : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI unitNumberText;
    [SerializeField] private TextMeshProUGUI shipNameText;
    [SerializeField] private Button button;

    public int ShipId { get; private set; } = -1;

    private readonly List<Entity> cellEntityList = new List<Entity>();

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(OnClick);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(OnClick);
    }

    public void Init(int shipId, List<Entity> entities, Sprite sprite, string shipName)
    {
        ShipId = shipId;

        cellEntityList.Clear();
        if (entities != null)
            cellEntityList.AddRange(entities);

        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        if (unitNumberText != null)
            unitNumberText.text = cellEntityList.Count.ToString();

        if (shipNameText != null)
            shipNameText.text = shipName;

        if (button != null)
            button.interactable = cellEntityList.Count > 0;

        gameObject.SetActive(true);
    }

    public void Clear()
    {
        ShipId = -1;
        cellEntityList.Clear();

        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (unitNumberText != null)
            unitNumberText.text = string.Empty;

        if (shipNameText != null)
            shipNameText.text = string.Empty;

        if (button != null)
            button.interactable = false;

        gameObject.SetActive(false);
    }

    public void OnClick()
    {
        if (UnitSelectionManager.Instance == null || cellEntityList.Count == 0)
        {
            return;
        }

        UnitSelectionManager.Instance.ReplaceSelection(cellEntityList);
    }
}