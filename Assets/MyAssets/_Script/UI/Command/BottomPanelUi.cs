using UnityEngine;

public class BottomPanelUi : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private UiUnitContainerGroup unitContainerGroup;
    [SerializeField] private CommandModesPanelUi commandModesPanelUi;
    [SerializeField] private ProductionPanelUi productionPanelUi;
    [SerializeField] private CarrierPanelUi carrierPanelUi;

    [Header("Update")]
    [SerializeField] private float runtimeRefreshInterval = 0.01f;

    private float runtimeRefreshTimer;

    private void Start()
    {
        if (UnitSelectionManager.Instance != null)
            UnitSelectionManager.Instance.OnSelectionChanged += OnSelectionChanged;

        RefreshAll();
    }

    private void OnDestroy()
    {
        if (UnitSelectionManager.Instance != null)
            UnitSelectionManager.Instance.OnSelectionChanged -= OnSelectionChanged;
    }

    private void Update()
    {
        runtimeRefreshTimer -= Time.unscaledDeltaTime;
        if (runtimeRefreshTimer > 0f)
        {
            return;
        }

        runtimeRefreshTimer = runtimeRefreshInterval;
        RefreshRuntimePanels();
    }

    private void OnSelectionChanged(object sender, System.EventArgs e)
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        runtimeRefreshTimer = 0f;

        if (unitContainerGroup != null)
            unitContainerGroup.Refresh();

        RefreshRuntimePanels();
    }

    private void RefreshRuntimePanels()
    {
        if (commandModesPanelUi != null)
            commandModesPanelUi.Refresh();

        if (productionPanelUi != null)
            productionPanelUi.Refresh();

        if (carrierPanelUi != null)
            carrierPanelUi.Refresh();
    }
}