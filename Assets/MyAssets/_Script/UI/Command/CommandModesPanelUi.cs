using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class CommandModesPanelUi : MonoBehaviour
{
    [Header("Link")]
    [SerializeField] private GameObject root;
    [SerializeField] private CommandInputHandler commandInputHandler;

    [Header("Fire Mode")]
    [SerializeField] private Button fireModeButton;
    [SerializeField] private Image fireModeImage;
    [SerializeField] private TextMeshProUGUI fireModeText;

    [Header("Move Mode")]
    [SerializeField] private Button moveModeButton;
    [SerializeField] private Image moveModeImage;
    [SerializeField] private TextMeshProUGUI moveModeText;

    [Header("Stop")]
    [SerializeField] private Button stopButton;

    [Header("Formation")]
    [SerializeField] private Button wedgeButton;
    [SerializeField] private Button lineButton;
    [SerializeField] private Button ringButton;
    [SerializeField] private Button columnButton;

    [Header("Colors")]
    [SerializeField] private Color fireAtWillColor = new Color(0.75f, 0.9f, 0.8f, 1f);
    [SerializeField] private Color returnFireColor = new Color(0.92f, 0.88f, 0.65f, 1f);
    [SerializeField] private Color holdFireColor = new Color(0.92f, 0.75f, 0.75f, 1f);
    [SerializeField] private Color holdPositionColor = new Color(0.75f, 0.82f, 0.95f, 1f);
    [SerializeField] private Color moveAndEngageColor = new Color(0.85f, 0.9f, 0.7f, 1f);
    [SerializeField] private Color attackMoveColor = new Color(0.75f, 0.9f, 0.8f, 1f);
    [SerializeField] private Color mixedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
    [SerializeField] private Color disabledColor = new Color(0.68f, 0.68f, 0.68f, 1f);

    private void Awake()
    {
        if (fireModeButton != null)
            fireModeButton.onClick.AddListener(HandleFireModeClick);

        if (moveModeButton != null)
            moveModeButton.onClick.AddListener(HandleMoveModeClick);

        if (stopButton != null)
            stopButton.onClick.AddListener(HandleStopClick);

        if (wedgeButton != null)
            wedgeButton.onClick.AddListener(() => InputProvider.Instance?.SimulateFormation(FormationType.Wedge));

        if (lineButton != null)
            lineButton.onClick.AddListener(() => InputProvider.Instance?.SimulateFormation(FormationType.Line));

        if (ringButton != null)
            ringButton.onClick.AddListener(() => InputProvider.Instance?.SimulateFormation(FormationType.Ring));

        if (columnButton != null)
            columnButton.onClick.AddListener(() => InputProvider.Instance?.SimulateFormation(FormationType.Column));
    }

    private void OnDestroy()
    {
        if (fireModeButton != null)
            fireModeButton.onClick.RemoveListener(HandleFireModeClick);

        if (moveModeButton != null)
            moveModeButton.onClick.RemoveListener(HandleMoveModeClick);

        if (stopButton != null)
            stopButton.onClick.RemoveListener(HandleStopClick);
    }

    public void Refresh()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            SetRootActive(false);
            return;
        }

        UiSelectionSnapshot snapshot = UiSelectionQueryUtility.BuildSelectionSnapshot(world.EntityManager);
        bool hasCommandableSelection = snapshot.hasSelection && snapshot.hasShipStateSelection;

        SetRootActive(hasCommandableSelection);
        if (!hasCommandableSelection)
        {
            return;
        }

        RefreshFireMode(snapshot);
        RefreshMoveMode(snapshot);

        if (stopButton != null)
            stopButton.interactable = true;
    }

    private void HandleFireModeClick()
    {
        if (commandInputHandler == null)
        {
            return;
        }

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        UiSelectionSnapshot snapshot = UiSelectionQueryUtility.BuildSelectionSnapshot(world.EntityManager);
        FireMode currentMode = snapshot.mixedFireMode ? FireMode.FireAtWill : snapshot.fireMode;
        commandInputHandler.SetFireModeOnSelected(CommandInputHandler.GetNextFireMode(currentMode));
    }

    private void HandleMoveModeClick()
    {
        if (commandInputHandler == null)
        {
            return;
        }

        // cycle each squad/ship from its own mode, mixed selection stays mixed
        commandInputHandler.CycleMoveModeOnSelected();
    }

    private void HandleStopClick()
    {
        if (commandInputHandler != null)
            commandInputHandler.StopSelected();
    }

    private void RefreshFireMode(UiSelectionSnapshot snapshot)
    {
        if (snapshot.mixedFireMode)
        {
            SetButtonVisual(fireModeButton, fireModeImage, fireModeText, "Mixed Fire", mixedColor, true);
            return;
        }

        switch (snapshot.fireMode)
        {
            case FireMode.FireAtWill:
                SetButtonVisual(fireModeButton, fireModeImage, fireModeText, "Fire At Will", fireAtWillColor, true);
                break;

            case FireMode.ReturnFire:
                SetButtonVisual(fireModeButton, fireModeImage, fireModeText, "Return Fire", returnFireColor, true);
                break;

            case FireMode.HoldFire:
                SetButtonVisual(fireModeButton, fireModeImage, fireModeText, "Hold Fire", holdFireColor, true);
                break;
        }
    }

    private void RefreshMoveMode(UiSelectionSnapshot snapshot)
    {
        if (snapshot.mixedMoveMode)
        {
            SetButtonVisual(moveModeButton, moveModeImage, moveModeText, "Mixed Move", mixedColor, true);
            return;
        }

        switch (snapshot.moveMode)
        {
            case MoveMode.HoldPosition:
                SetButtonVisual(moveModeButton, moveModeImage, moveModeText, "Hold Position", holdPositionColor, true);
                break;

            case MoveMode.MoveAndEngage:
                SetButtonVisual(moveModeButton, moveModeImage, moveModeText, "Move + Engage", moveAndEngageColor, true);
                break;

            case MoveMode.AttackMove:
                SetButtonVisual(moveModeButton, moveModeImage, moveModeText, "Attack Move", attackMoveColor, true);
                break;
        }
    }

    private void SetButtonVisual(Button button, Image image, TextMeshProUGUI text, string label, Color color, bool interactable)
    {
        if (button != null)
            button.interactable = interactable;

        if (image != null)
            image.color = interactable ? color : disabledColor;

        if (text != null)
            text.text = label;
    }

    private void SetRootActive(bool value)
    {
        if (root != null)
            root.SetActive(value);
        else
            gameObject.SetActive(value);
    }
}