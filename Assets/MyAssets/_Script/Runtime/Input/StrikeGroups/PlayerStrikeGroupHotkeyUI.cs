using UnityEngine;
using UnityEngine.UI;
using TMPro;

// UI buttons for strike groups 1-9, button shows number and unit count
// scene: Canvas -> StrikeGroupHotkeyPanel -> GroupButton_N
public class PlayerStrikeGroupHotkeyUI : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int groupCount = 9;

    [Header("Button Prefab")]
    [SerializeField] private GameObject groupButtonPrefab;

    [Header("Colors")]
    [SerializeField] private Color activeColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
    [SerializeField] private Color inactiveColor = new Color(0.15f, 0.15f, 0.15f, 0.4f);
    [SerializeField] private Color hoverColor = new Color(0.3f, 0.5f, 0.7f, 0.9f);

    // Cached button references.
    private GroupButtonRef[] buttons;

    private struct GroupButtonRef
    {
        public GameObject root;
        public Image background;
        public TextMeshProUGUI numberText;
        public TextMeshProUGUI countText;
        public Button button;
    }

    private void Start()
    {
        CreateButtons();

        // listen for group updates
        if (PlayerStrikeGroupHotkeyController.Instance != null)
        {
            PlayerStrikeGroupHotkeyController.Instance.OnGroupChanged += OnGroupChanged;
        }
    }

    private void OnDestroy()
    {
        if (PlayerStrikeGroupHotkeyController.Instance != null)
        {
            PlayerStrikeGroupHotkeyController.Instance.OnGroupChanged -= OnGroupChanged;
        }
    }

    private void CreateButtons()
    {
        buttons = new GroupButtonRef[groupCount];

        for (int i = 0; i < groupCount; i++)
        {
            int groupIndex = i + 1; // Groups 1-9.

            GameObject go;
            if (groupButtonPrefab != null)
            {
                go = Instantiate(groupButtonPrefab, transform);
            }
            else
            {
                // simple fallback button when no prefab
                go = CreateDefaultButton(groupIndex);
            }

            go.name = $"GroupButton_{groupIndex}";

            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();

            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();

            // find optional text fields in prefab
            TextMeshProUGUI numberText = null;
            TextMeshProUGUI countText = null;

            var texts = go.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length >= 2)
            {
                numberText = texts[0];
                countText = texts[1];
            }
            else if (texts.Length == 1)
            {
                numberText = texts[0];
            }

            if (numberText != null)
            {
                numberText.text = groupIndex.ToString();
            }

            // click selects group
            int capturedIndex = groupIndex;
            btn.onClick.AddListener(() =>
            {
                InputProvider.Instance?.SimulateGroupSelect(capturedIndex);
            });

            buttons[i] = new GroupButtonRef
            {
                root = go,
                background = img,
                numberText = numberText,
                countText = countText,
                button = btn,
            };

            // inactive until group has units
            UpdateButtonVisual(i, 0);
        }
    }

    private void OnGroupChanged(int groupIndex)
    {
        if (groupIndex < 1 || groupIndex > groupCount) return;

        int count = PlayerStrikeGroupHotkeyController.Instance.GetGroupCount(groupIndex);
        UpdateButtonVisual(groupIndex - 1, count);
    }

    // refresh counts sometimes, units can die
    private void Update()
    {
        if (Time.frameCount % 30 != 0) return;
        if (PlayerStrikeGroupHotkeyController.Instance == null) return;

        for (int i = 0; i < groupCount; i++)
        {
            int count = PlayerStrikeGroupHotkeyController.Instance.GetGroupCount(i + 1);
            UpdateButtonVisual(i, count);
        }
    }

    private void UpdateButtonVisual(int buttonIndex, int unitCount)
    {
        if (buttonIndex < 0 || buttonIndex >= buttons.Length) return;

        ref GroupButtonRef btn = ref buttons[buttonIndex];

        bool active = unitCount > 0;

        if (btn.background != null)
        {
            btn.background.color = active ? activeColor : inactiveColor;
        }

        if (btn.countText != null)
        {
            btn.countText.text = active ? unitCount.ToString() : "";
        }
    }

    // fallback button when no prefab
    private GameObject CreateDefaultButton(int index)
    {
        GameObject go = new GameObject($"GroupBtn_{index}",
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(transform, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(40, 40);

        // Group number.
        GameObject numGo = new GameObject("Number", typeof(RectTransform));
        numGo.transform.SetParent(go.transform, false);
        var numText = numGo.AddComponent<TextMeshProUGUI>();
        numText.text = index.ToString();
        numText.fontSize = 16;
        numText.alignment = TextAlignmentOptions.Top;
        numText.color = Color.white;
        var numRT = numGo.GetComponent<RectTransform>();
        numRT.anchorMin = Vector2.zero;
        numRT.anchorMax = Vector2.one;
        numRT.offsetMin = new Vector2(2, 2);
        numRT.offsetMax = new Vector2(-2, -2);

        // Unit count.
        GameObject countGo = new GameObject("Count", typeof(RectTransform));
        countGo.transform.SetParent(go.transform, false);
        var countText = countGo.AddComponent<TextMeshProUGUI>();
        countText.text = "";
        countText.fontSize = 11;
        countText.alignment = TextAlignmentOptions.Bottom;
        countText.color = new Color(0.7f, 1f, 0.7f, 1f);
        var countRT = countGo.GetComponent<RectTransform>();
        countRT.anchorMin = Vector2.zero;
        countRT.anchorMax = Vector2.one;
        countRT.offsetMin = new Vector2(2, 2);
        countRT.offsetMax = new Vector2(-2, -2);

        return go;
    }
}
