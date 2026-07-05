using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class ProductionPanelUi : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Data")]
    [SerializeField] private ShipCatalogAsset catalog;

    [Header("Tabs")]
    [SerializeField] private Button smallButton;
    [SerializeField] private Image smallButtonImage;
    [SerializeField] private Button mediumButton;
    [SerializeField] private Image mediumButtonImage;
    [SerializeField] private Button bigButton;
    [SerializeField] private Image bigButtonImage;
    [SerializeField] private Button capitalButton;
    [SerializeField] private Image capitalButtonImage;
    [SerializeField] private ProductionTabCategory defaultTab = ProductionTabCategory.Medium;

    [Header("Build list")]
    [SerializeField] private Transform buildContainer;
    [SerializeField] private ShipBuildPlateUi buildPlatePrefab;
    [SerializeField] private int startBuildPoolSize = 12;

    [Header("Queue")]
    [SerializeField] private Transform queueContainer;
    [SerializeField] private ProductionQueuePlateUi queuePlatePrefab;
    [SerializeField] private int startQueuePoolSize = 8;
    [SerializeField] private ProductionProgressUi productionProgressUi;

    [Header("Tab colors")]
    [SerializeField] private Color activeTabColor = new Color(0.92f, 0.88f, 0.65f, 1f);
    [SerializeField] private Color inactiveTabColor = new Color(0.82f, 0.82f, 0.82f, 1f);

    private readonly List<ShipBuildPlateUi> buildPool = new List<ShipBuildPlateUi>();
    private readonly List<ProductionQueuePlateUi> queuePool = new List<ProductionQueuePlateUi>();

    private ProductionTabCategory currentTab;
    private Entity currentProducerEntity = Entity.Null;

    private void Awake()
    {
        currentTab = defaultTab;

        if (smallButton != null)
            smallButton.onClick.AddListener(() => SetTab(ProductionTabCategory.Small));

        if (mediumButton != null)
            mediumButton.onClick.AddListener(() => SetTab(ProductionTabCategory.Medium));

        if (bigButton != null)
            bigButton.onClick.AddListener(() => SetTab(ProductionTabCategory.Big));

        if (capitalButton != null)
            capitalButton.onClick.AddListener(() => SetTab(ProductionTabCategory.Capital));

        EnsureBuildPool(startBuildPoolSize);
        EnsureQueuePool(startQueuePoolSize);
        ClearBuildPool();
        ClearQueuePool();
    }

    public void Refresh()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || catalog == null)
        {
            HidePanel();
            return;
        }

        EntityManager entityManager = world.EntityManager;
        UiSelectionSnapshot snapshot = UiSelectionQueryUtility.BuildSelectionSnapshot(entityManager);

        currentProducerEntity = snapshot.singleProducerEntity;
        bool hasSingleProducer = currentProducerEntity != Entity.Null;

        SetRootActive(hasSingleProducer);
        if (!hasSingleProducer)
        {
            ClearBuildPool();
            ClearQueuePool();

            if (productionProgressUi != null)
                productionProgressUi.Hide();

            return;
        }

        RefreshTabVisuals();
        RefreshBuildList(entityManager);
        RefreshQueue(entityManager);
        RefreshActiveProduction(entityManager);
    }

    private void SetTab(ProductionTabCategory tab)
    {
        currentTab = tab;
        Refresh();
    }

    private void RefreshBuildList(EntityManager entityManager)
    {
        if (!entityManager.Exists(currentProducerEntity) || !entityManager.HasBuffer<ProducerAllowedShipId>(currentProducerEntity))
        {
            ClearBuildPool();
            return;
        }

        DynamicBuffer<ProducerAllowedShipId> allowedShips = entityManager.GetBuffer<ProducerAllowedShipId>(currentProducerEntity);

        bool canAffordResource = false;
        ResourceData resourceData = default;

        EntityQuery resourceQuery = entityManager.CreateEntityQuery(typeof(ResourceData));
        if (!resourceQuery.IsEmpty)
        {
            resourceData = resourceQuery.GetSingleton<ResourceData>();
            canAffordResource = true;
        }

        resourceQuery.Dispose();

        List<ShipCatalogAssetEntry> visibleShips = new List<ShipCatalogAssetEntry>();

        for (int i = 0; i < allowedShips.Length; i++)
        {
            int shipId = allowedShips[i].shipId;
            if (!catalog.TryGetShip(shipId, out ShipCatalogAssetEntry ship))
                continue;

            if (ship.ProductionTab != currentTab)
                continue;

            visibleShips.Add(ship);
        }

        visibleShips.Sort((a, b) => catalog.GetSortOrder(a.id).CompareTo(catalog.GetSortOrder(b.id)));

        EnsureBuildPool(visibleShips.Count);
        ClearBuildPool();

        for (int i = 0; i < visibleShips.Count; i++)
        {
            ShipCatalogAssetEntry ship = visibleShips[i];
            bool canAfford = !canAffordResource || ProductionUtility.CanAfford(resourceData, ship.Cost);
            int requestedShipId = ship.id;

            buildPool[i].Init(ship, canAfford, () =>
            {
                World world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated)
                    return;

                ProducerCommandUtility.TryAddBuildRequest(world.EntityManager, currentProducerEntity, requestedShipId, 1);
            });
        }
    }

    private void RefreshQueue(EntityManager entityManager)
    {
        if (!entityManager.Exists(currentProducerEntity) || !entityManager.HasBuffer<ProducerBuildQueueElement>(currentProducerEntity))
        {
            ClearQueuePool();
            return;
        }

        DynamicBuffer<ProducerBuildQueueElement> queue = entityManager.GetBuffer<ProducerBuildQueueElement>(currentProducerEntity);

        EnsureQueuePool(queue.Length);
        ClearQueuePool();

        for (int i = 0; i < queue.Length; i++)
        {
            ProducerBuildQueueElement queueElement = queue[i];
            Sprite icon = catalog.GetIcon(queueElement.shipId);
            string shipName = catalog.GetName(queueElement.shipId);
            int queueIndex = i;

            queuePool[i].Init(icon, shipName, queueElement.buildTime, () =>
            {
                World world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated)
                    return;

                ProducerCommandUtility.TryRemoveQueueAtIndex(world.EntityManager, currentProducerEntity, queueIndex);
            });
        }
    }

    private void RefreshActiveProduction(EntityManager entityManager)
    {
        if (productionProgressUi == null)
        {
            return;
        }

        if (!entityManager.Exists(currentProducerEntity) || !entityManager.HasComponent<ActiveProduction>(currentProducerEntity))
        {
            productionProgressUi.Hide();
            return;
        }

        ActiveProduction activeProduction = entityManager.GetComponentData<ActiveProduction>(currentProducerEntity);
        if (!activeProduction.isActive || activeProduction.shipId < 0)
        {
            productionProgressUi.Hide();
            return;
        }

        Sprite icon = catalog.GetIcon(activeProduction.shipId);
        string shipName = catalog.GetName(activeProduction.shipId);
        productionProgressUi.Show(icon, shipName, activeProduction.timer, activeProduction.totalTime);
    }

    private void RefreshTabVisuals()
    {
        SetTabVisual(smallButtonImage, currentTab == ProductionTabCategory.Small);
        SetTabVisual(mediumButtonImage, currentTab == ProductionTabCategory.Medium);
        SetTabVisual(bigButtonImage, currentTab == ProductionTabCategory.Big);
        SetTabVisual(capitalButtonImage, currentTab == ProductionTabCategory.Capital);
    }

    private void SetTabVisual(Image image, bool isActive)
    {
        if (image != null)
            image.color = isActive ? activeTabColor : inactiveTabColor;
    }

    private void EnsureBuildPool(int targetCount)
    {
        if (buildPlatePrefab == null || buildContainer == null)
        {
            return;
        }

        while (buildPool.Count < targetCount)
        {
            ShipBuildPlateUi plate = Instantiate(buildPlatePrefab, buildContainer);
            plate.Clear();
            buildPool.Add(plate);
        }
    }

    private void EnsureQueuePool(int targetCount)
    {
        if (queuePlatePrefab == null || queueContainer == null)
        {
            return;
        }

        while (queuePool.Count < targetCount)
        {
            ProductionQueuePlateUi plate = Instantiate(queuePlatePrefab, queueContainer);
            plate.Clear();
            queuePool.Add(plate);
        }
    }

    private void ClearBuildPool()
    {
        for (int i = 0; i < buildPool.Count; i++)
            buildPool[i].Clear();
    }

    private void ClearQueuePool()
    {
        for (int i = 0; i < queuePool.Count; i++)
            queuePool[i].Clear();
    }

    private void HidePanel()
    {
        SetRootActive(false);
        ClearBuildPool();
        ClearQueuePool();

        if (productionProgressUi != null)
            productionProgressUi.Hide();
    }

    private void SetRootActive(bool value)
    {
        if (root != null)
            root.SetActive(value);
        else
            gameObject.SetActive(value);
    }
}