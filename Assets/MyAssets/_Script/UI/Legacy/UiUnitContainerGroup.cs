using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class UiUnitContainerGroup : MonoBehaviour
{
    [Header("Prefabs and data")]
    [SerializeField] private UiUnitCell cellPrefab;
    [SerializeField] private ShipCatalogAsset catalog;
    [SerializeField] private int startPoolSize = 8;

    private readonly List<UiUnitCell> pool = new List<UiUnitCell>();

    private void Awake()
    {
        EnsurePool(startPoolSize);
        ClearPool();
    }

    private void Start()
    {
        if (UnitSelectionManager.Instance != null)
            UnitSelectionManager.Instance.OnSelectionChanged += OnSelectionChanged;

        Refresh();
    }

    private void OnDestroy()
    {
        if (UnitSelectionManager.Instance != null)
            UnitSelectionManager.Instance.OnSelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged(object sender, System.EventArgs e)
    {
        Refresh();
    }

    public void Refresh()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ClearPool();
            return;
        }

        EntityManager entityManager = world.EntityManager;

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, CanControl>()
            .Build(entityManager);

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        Dictionary<int, List<Entity>> groups = new Dictionary<int, List<Entity>>();

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            if (!TryGetShipId(entityManager, entity, out int shipId))
                continue;

            if (!groups.TryGetValue(shipId, out List<Entity> list))
            {
                list = new List<Entity>();
                groups.Add(shipId, list);
            }

            list.Add(entity);
        }

        entities.Dispose();
        query.Dispose();

        List<KeyValuePair<int, List<Entity>>> orderedGroups = new List<KeyValuePair<int, List<Entity>>>(groups);
        orderedGroups.Sort(CompareGroups);

        EnsurePool(orderedGroups.Count);
        ClearPool();

        for (int i = 0; i < orderedGroups.Count; i++)
        {
            KeyValuePair<int, List<Entity>> group = orderedGroups[i];
            Sprite sprite = catalog != null ? catalog.GetIcon(group.Key) : null;
            string shipName = catalog != null ? catalog.GetName(group.Key) : string.Empty;
            pool[i].Init(group.Key, group.Value, sprite, shipName);
        }
    }

    private int CompareGroups(KeyValuePair<int, List<Entity>> a, KeyValuePair<int, List<Entity>> b)
    {
        int orderA = catalog != null ? catalog.GetSortOrder(a.Key) : a.Key;
        int orderB = catalog != null ? catalog.GetSortOrder(b.Key) : b.Key;
        return orderA.CompareTo(orderB);
    }

    private bool TryGetShipId(EntityManager entityManager, Entity entity, out int shipId)
    {
        if (entityManager.HasComponent<ShipCatalogId>(entity))
        {
            shipId = entityManager.GetComponentData<ShipCatalogId>(entity).Value;
            return shipId >= 0;
        }

        if (catalog != null && entityManager.HasComponent<ShipTypeIndex>(entity))
        {
            ShipType shipType = (ShipType)entityManager.GetComponentData<ShipTypeIndex>(entity).Value;
            if (catalog.TryGetFirstShipIdByType(shipType, out shipId))
                return true;
        }

        shipId = -1;
        return false;
    }

    private void EnsurePool(int targetCount)
    {
        if (cellPrefab == null)
        {
            return;
        }

        while (pool.Count < targetCount)
        {
            UiUnitCell cell = Instantiate(cellPrefab, transform);
            cell.Clear();
            pool.Add(cell);
        }
    }

    private void ClearPool()
    {
        for (int i = 0; i < pool.Count; i++)
            pool[i].Clear();
    }
}