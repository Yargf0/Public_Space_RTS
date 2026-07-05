using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class CarrierPanelUi : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("")]
    [SerializeField] private ShipCatalogAsset catalog;
    [SerializeField] private Sprite fallbackSquadronIcon;

    [Header("")]
    [SerializeField] private CarrierCommandUI carrierCommandUi;
    [SerializeField] private Button launchModeButton;
    [SerializeField] private Image launchModeButtonImage;
    [SerializeField] private TextMeshProUGUI launchModeText;
    [SerializeField] private Button recallAllButton;

    [Header("")]
    [SerializeField] private Transform slotContainer;
    [SerializeField] private CarrierSquadStatusUi slotPrefab;
    [SerializeField] private int startPoolSize = 8;

    [Header("")]
    [SerializeField] private Color autoLaunchColor = new Color(0.75f, 0.9f, 0.8f, 1f);
    [SerializeField] private Color holdDeckColor = new Color(0.92f, 0.88f, 0.65f, 1f);
    [SerializeField] private Color recallColor = new Color(0.92f, 0.75f, 0.75f, 1f);
    [SerializeField] private Color readyColor = new Color(0.82f, 0.93f, 0.82f, 1f);
    [SerializeField] private Color launchedColor = new Color(0.8f, 0.88f, 0.98f, 1f);
    [SerializeField] private Color returningColor = new Color(0.93f, 0.9f, 0.75f, 1f);
    [SerializeField] private Color servicingColor = new Color(0.88f, 0.88f, 0.88f, 1f);
    [SerializeField] private Color rebuildingColor = new Color(0.94f, 0.8f, 0.8f, 1f);
    [SerializeField] private Color disabledColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    private readonly System.Collections.Generic.List<CarrierSquadStatusUi> pool =
        new System.Collections.Generic.List<CarrierSquadStatusUi>();

    private Entity currentCarrierEntity = Entity.Null;

    private void Awake()
    {
        if (launchModeButton != null)
            launchModeButton.onClick.AddListener(HandleLaunchModeClick);

        if (recallAllButton != null)
            recallAllButton.onClick.AddListener(HandleRecallAllClick);

        EnsurePool(startPoolSize);
        ClearPool();
    }

    public void Refresh()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            HidePanel();
            return;
        }

        EntityManager entityManager = world.EntityManager;
        UiSelectionSnapshot snapshot = UiSelectionQueryUtility.BuildSelectionSnapshot(entityManager);

        currentCarrierEntity = snapshot.singleCarrierEntity;
        bool hasSingleCarrier = currentCarrierEntity != Entity.Null;

        SetRootActive(hasSingleCarrier);
        if (!hasSingleCarrier)
        {
            ClearPool();
            return;
        }

        RefreshLaunchMode(entityManager);
        RefreshSlots(entityManager);
    }

    private void HandleLaunchModeClick()
    {
        if (carrierCommandUi != null)
            carrierCommandUi.ToggleLaunchModeOnSelected();
    }

    private void HandleRecallAllClick()
    {
        if (carrierCommandUi != null)
            carrierCommandUi.SetRecallAllOnSelected();
    }

    private void RefreshLaunchMode(EntityManager entityManager)
    {
        if (!entityManager.Exists(currentCarrierEntity) ||
            !entityManager.HasComponent<CarrierHangarState>(currentCarrierEntity))
        {
            return;
        }

        CarrierHangarState hangar =
            entityManager.GetComponentData<CarrierHangarState>(currentCarrierEntity);

        string label;
        Color color;

        switch (hangar.stance)
        {
            case CarrierStance.AutoLaunch:
                label = "Auto Launch";
                color = autoLaunchColor;
                break;

            case CarrierStance.HoldDeck:
                label = "Hold Deck";
                color = holdDeckColor;
                break;

            case CarrierStance.RecallAll:
                label = "Recall...";
                color = recallColor;
                break;

            default:
                label = "Launch Mode";
                color = holdDeckColor;
                break;
        }

        if (launchModeText != null)
            launchModeText.text = label;

        if (launchModeButtonImage != null)
            launchModeButtonImage.color = color;
    }

    private void RefreshSlots(EntityManager entityManager)
    {
        if (!entityManager.Exists(currentCarrierEntity) ||
            !entityManager.HasBuffer<CarrierSquadronTemplateElement>(currentCarrierEntity) ||
            !entityManager.HasBuffer<CarrierSquadronSlotElement>(currentCarrierEntity))
        {
            ClearPool();
            return;
        }

        DynamicBuffer<CarrierSquadronTemplateElement> templates =
            entityManager.GetBuffer<CarrierSquadronTemplateElement>(currentCarrierEntity);

        DynamicBuffer<CarrierSquadronSlotElement> slots =
            entityManager.GetBuffer<CarrierSquadronSlotElement>(currentCarrierEntity);

        EnsurePool(slots.Length);
        ClearPool();

        for (int i = 0; i < slots.Length; i++)
        {
            CarrierSquadronSlotElement slot = slots[i];
            if (slot.templateIndex < 0 || slot.templateIndex >= templates.Length)
                continue;

            CarrierSquadronTemplateElement template = templates[slot.templateIndex];

            Sprite icon = GetSquadIcon(entityManager, template.memberPrefab);
            string stateLabel = GetSlotStateLabel(slot.state);
            Color stateColor = GetSlotStateColor(slot.state);
            string countLabel = GetCountLabel(entityManager, slot, template);
            float progress = GetSlotProgress(entityManager, slot, template);

            pool[i].Init(icon, stateLabel, countLabel, progress, stateColor);
        }
    }

    private string GetCountLabel(
        EntityManager entityManager,
        CarrierSquadronSlotElement slot,
        CarrierSquadronTemplateElement template)
    {
        int maxMembers = Mathf.Max(1, template.membersPerSquadron);
        int aliveCount = maxMembers;

        if (slot.squadronEntity != Entity.Null &&
            entityManager.Exists(slot.squadronEntity) &&
            entityManager.HasComponent<SquadComponent>(slot.squadronEntity))
        {
            SquadComponent squadData =
                entityManager.GetComponentData<SquadComponent>(slot.squadronEntity);

            aliveCount = Mathf.Clamp(squadData.aliveCount, 0, squadData.maxMembers);
            maxMembers = Mathf.Max(1, squadData.maxMembers);
        }
        else if (slot.state == CarrierSlotState.QueuedForRebuild ||
                 slot.state == CarrierSlotState.Rebuilding)
        {
            aliveCount = 0;
        }

        return $"{aliveCount}/{maxMembers}";
    }

    private float GetSlotProgress(
        EntityManager entityManager,
        CarrierSquadronSlotElement slot,
        CarrierSquadronTemplateElement template)
    {
        switch (slot.state)
        {
            case CarrierSlotState.Ready:
                return 1f;

            case CarrierSlotState.Launched:
                return GetSquadronEnduranceProgress(entityManager, slot, template);

            case CarrierSlotState.Returning:
                return 0f;

            case CarrierSlotState.Servicing:
                if (template.serviceTime <= 0f)
                    return 1f;
                return 1f - Mathf.Clamp01(slot.timer / template.serviceTime);

            case CarrierSlotState.Rebuilding:
                if (template.rebuildTime <= 0f)
                    return 1f;
                return 1f - Mathf.Clamp01(slot.timer / template.rebuildTime);

            case CarrierSlotState.QueuedForRebuild:
                return 0f;

            case CarrierSlotState.Disabled:
            default:
                return 0f;
        }
    }

    private float GetSquadronEnduranceProgress(
        EntityManager entityManager,
        CarrierSquadronSlotElement slot,
        CarrierSquadronTemplateElement template)
    {
        if (template.endurance <= 0f)
            return 1f;

        if (slot.squadronEntity == Entity.Null || !entityManager.Exists(slot.squadronEntity))
            return 0f;

        if (!entityManager.HasComponent<SquadComponent>(slot.squadronEntity))
            return 0f;

        SquadComponent squad =
            entityManager.GetComponentData<SquadComponent>(slot.squadronEntity);

        return Mathf.Clamp01(squad.enduranceRemaining / template.endurance);
    }

    private Sprite GetSquadIcon(EntityManager entityManager, Entity memberPrefab)
    {
        if (catalog != null &&
            memberPrefab != Entity.Null &&
            entityManager.Exists(memberPrefab) &&
            entityManager.HasComponent<ShipCatalogId>(memberPrefab))
        {
            int shipId = entityManager.GetComponentData<ShipCatalogId>(memberPrefab).Value;
            Sprite shipIcon = catalog.GetIcon(shipId);
            if (shipIcon != null)
                return shipIcon;
        }

        return fallbackSquadronIcon;
    }

    private string GetSlotStateLabel(CarrierSlotState state)
    {
        return state switch
        {
            CarrierSlotState.Ready => "Ready",
            CarrierSlotState.Launched => "Fight",
            CarrierSlotState.Returning => "Return",
            CarrierSlotState.Servicing => "Repair",
            CarrierSlotState.QueuedForRebuild => "Queued",
            CarrierSlotState.Rebuilding => "Rebuild",
            CarrierSlotState.Disabled => "Disabled",
            _ => state.ToString(),
        };
    }

    private Color GetSlotStateColor(CarrierSlotState state)
    {
        return state switch
        {
            CarrierSlotState.Ready => readyColor,
            CarrierSlotState.Launched => launchedColor,
            CarrierSlotState.Returning => returningColor,
            CarrierSlotState.Servicing => servicingColor,
            CarrierSlotState.QueuedForRebuild => rebuildingColor,
            CarrierSlotState.Rebuilding => rebuildingColor,
            CarrierSlotState.Disabled => disabledColor,
            _ => disabledColor,
        };
    }

    private void EnsurePool(int targetCount)
    {
        if (slotPrefab == null || slotContainer == null)
        {
            return;
        }

        while (pool.Count < targetCount)
        {
            CarrierSquadStatusUi slotUi = Instantiate(slotPrefab, slotContainer);
            slotUi.Clear();
            pool.Add(slotUi);
        }
    }

    private void ClearPool()
    {
        for (int i = 0; i < pool.Count; i++)
            pool[i].Clear();
    }

    private void HidePanel()
    {
        SetRootActive(false);
        ClearPool();
    }

    private void SetRootActive(bool value)
    {
        if (root != null)
            root.SetActive(value);
        else
            gameObject.SetActive(value);
    }
}