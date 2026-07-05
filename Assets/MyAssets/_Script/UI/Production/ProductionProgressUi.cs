using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProductionProgressUi : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image icon;
    [SerializeField] private Image progressFill;
    [SerializeField] private TextMeshProUGUI shipNameText;
    [SerializeField] private TextMeshProUGUI timeText;

    public void Show(Sprite shipIcon, string shipName, float remainingTime, float totalTime)
    {
        SetRootActive(true);

        if (icon != null)
        {
            icon.sprite = shipIcon;
            icon.enabled = shipIcon != null;
        }

        if (shipNameText != null)
            shipNameText.text = shipName;

        if (timeText != null)
            timeText.text = FormatTime(remainingTime);

        if (progressFill != null)
        {
            float progress = totalTime > 0f ? 1f - Mathf.Clamp01(remainingTime / totalTime) : 0f;
            progressFill.fillAmount = progress;
        }
    }

    public void Hide()
    {
        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (shipNameText != null)
            shipNameText.text = string.Empty;

        if (timeText != null)
            timeText.text = string.Empty;

        if (progressFill != null)
            progressFill.fillAmount = 0f;

        SetRootActive(false);
    }

    private void SetRootActive(bool value)
    {
        if (root != null)
            root.SetActive(value);
        else
            gameObject.SetActive(value);
    }

    private string FormatTime(float time)
    {
        TimeSpan span = TimeSpan.FromSeconds(Mathf.Max(0f, time));
        return $"{span.Minutes:00}:{span.Seconds:00}";
    }
}