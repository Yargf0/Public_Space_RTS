using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProductionQueuePlateUi : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI shipNameText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private Button removeButton;

    private Action removeAction;

    private void Awake()
    {
        if (removeButton != null)
            removeButton.onClick.AddListener(HandleRemoveClick);
    }

    private void OnDestroy()
    {
        if (removeButton != null)
            removeButton.onClick.RemoveListener(HandleRemoveClick);
    }

    public void Init(Sprite shipIcon, string shipName, float buildTime, Action onRemove)
    {
        removeAction = onRemove;

        if (icon != null)
        {
            icon.sprite = shipIcon;
            icon.enabled = shipIcon != null;
        }

        if (shipNameText != null)
            shipNameText.text = shipName;

        if (timeText != null)
            timeText.text = FormatTime(buildTime);

        if (removeButton != null)
            removeButton.interactable = onRemove != null;

        gameObject.SetActive(true);
    }

    public void Clear()
    {
        removeAction = null;

        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (shipNameText != null)
            shipNameText.text = string.Empty;

        if (timeText != null)
            timeText.text = string.Empty;

        if (removeButton != null)
            removeButton.interactable = false;

        gameObject.SetActive(false);
    }

    private void HandleRemoveClick()
    {
        removeAction?.Invoke();
    }

    private string FormatTime(float time)
    {
        TimeSpan span = TimeSpan.FromSeconds(Mathf.Max(0f, time));
        return $"{span.Minutes:00}:{span.Seconds:00}";
    }
}