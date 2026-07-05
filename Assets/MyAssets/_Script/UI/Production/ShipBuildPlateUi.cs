using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShipBuildPlateUi : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI shipNameText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI metalText;
    [SerializeField] private TextMeshProUGUI crystalText;
    [SerializeField] private TextMeshProUGUI timeText;

    private Action clickAction;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    public void Init(ShipCatalogAssetEntry ship, bool canAfford, Action onClick)
    {
        clickAction = onClick;

        if (icon != null)
        {
            icon.sprite = ship.Icon;
            icon.enabled = ship.Icon != null;
        }

        if (shipNameText != null)
            shipNameText.text = ship.Name;

        if (energyText != null)
            energyText.text = Mathf.RoundToInt(ship.Cost.Energy).ToString();

        if (metalText != null)
            metalText.text = Mathf.RoundToInt(ship.Cost.Mineral).ToString();

        if (crystalText != null)
            crystalText.text = Mathf.RoundToInt(ship.Cost.Gas).ToString();

        if (timeText != null)
            timeText.text = FormatTime(ship.BuildTime);

        if (button != null)
            button.interactable = canAfford;

        gameObject.SetActive(true);
    }

    public void Clear()
    {
        clickAction = null;

        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (shipNameText != null)
            shipNameText.text = string.Empty;

        if (energyText != null)
            energyText.text = string.Empty;

        if (metalText != null)
            metalText.text = string.Empty;

        if (crystalText != null)
            crystalText.text = string.Empty;

        if (timeText != null)
            timeText.text = string.Empty;

        if (button != null)
            button.interactable = false;

        gameObject.SetActive(false);
    }

    private void HandleClick()
    {
        clickAction?.Invoke();
    }

    private string FormatTime(float time)
    {
        TimeSpan span = TimeSpan.FromSeconds(Mathf.Max(0f, time));
        return $"{span.Minutes:00}:{span.Seconds:00}";
    }
}