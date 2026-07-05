using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CarrierSquadStatusUi : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private Image background;
    [SerializeField] private Image progressFill;
    [SerializeField] private TextMeshProUGUI stateText;
    [SerializeField] private TextMeshProUGUI countText;

    public void Init(Sprite squadIcon, string stateLabel, string countLabel, float progress01, Color stateColor)
    {
        if (icon != null)
        {
            icon.sprite = squadIcon;
            icon.enabled = squadIcon != null;
        }

        if (background != null)
            background.color = stateColor;

        if (progressFill != null)
            progressFill.fillAmount = Mathf.Clamp01(progress01);

        if (stateText != null)
            stateText.text = stateLabel;

        if (countText != null)
            countText.text = countLabel;

        gameObject.SetActive(true);
    }

    public void Clear()
    {
        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (progressFill != null)
            progressFill.fillAmount = 0f;

        if (stateText != null)
            stateText.text = string.Empty;

        if (countText != null)
            countText.text = string.Empty;

        gameObject.SetActive(false);
    }
}