using System;
using System.Collections.Generic;
using UnityEngine;

public enum ProductionTabCategory : byte
{
    Small,
    Medium,
    Big,
    Capital,
}

public enum ProductionProductKind : byte
{
    Ship = 0,
    Squad = 1,
}

[CreateAssetMenu(fileName = "ShipCatalogAsset", menuName = "Ship/Ship Catalog")]
public class ShipCatalogAsset : ScriptableObject
{
    [Header("Catalog Items")]
    public ShipCatalogAssetEntry[] ships;

    private Dictionary<int, ShipCatalogAssetEntry> shipById;
    private Dictionary<ShipType, int> firstShipIdByType;
    private Dictionary<int, int> sortOrderByShipId;

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();
    }

    private void RebuildCache()
    {
        shipById = new Dictionary<int, ShipCatalogAssetEntry>();
        firstShipIdByType = new Dictionary<ShipType, int>();
        sortOrderByShipId = new Dictionary<int, int>();

        if (ships == null || ships.Length == 0)
        {
            return;
        }

        for (int i = 0; i < ships.Length; i++)
        {
            ShipCatalogAssetEntry ship = ships[i];
            if (ship.id < 0)
                continue;

            shipById[ship.id] = ship;
            sortOrderByShipId[ship.id] = i;

            if (!firstShipIdByType.ContainsKey(ship.ShipType))
                firstShipIdByType.Add(ship.ShipType, ship.id);
        }
    }

    public bool TryGetShip(int shipId, out ShipCatalogAssetEntry ship)
    {
        if (shipById == null)
            RebuildCache();

        return shipById.TryGetValue(shipId, out ship);
    }

    public bool TryGetFirstShipIdByType(ShipType shipType, out int shipId)
    {
        if (firstShipIdByType == null)
            RebuildCache();

        return firstShipIdByType.TryGetValue(shipType, out shipId);
    }

    public Sprite GetIcon(int shipId)
    {
        if (TryGetShip(shipId, out ShipCatalogAssetEntry ship))
            return ship.Icon;

        return null;
    }

    public string GetName(int shipId)
    {
        if (TryGetShip(shipId, out ShipCatalogAssetEntry ship))
            return ship.Name;

        return string.Empty;
    }

    public ProductionTabCategory GetProductionTab(int shipId)
    {
        if (TryGetShip(shipId, out ShipCatalogAssetEntry ship))
            return ship.ProductionTab;

        return ProductionTabCategory.Small;
    }

    public int GetSortOrder(int shipId)
    {
        if (sortOrderByShipId == null)
            RebuildCache();

        if (sortOrderByShipId.TryGetValue(shipId, out int order))
            return order;

        return int.MaxValue;
    }
}

[Serializable]
public struct ShipCatalogAssetEntry
{
    [Header("Identity")]
    public int id;
    public string Name;
    public ShipType ShipType;
    public ProductionTabCategory ProductionTab;

    [Header("Production")]
    public ProductionProductKind productKind;
    public Cost Cost;
    public float BuildTime;

    [Header("Ship Product")]
    [Tooltip("Used when productKind = Ship.")]
    public GameObject prefab;

    [Header("Squad Product")]
    [Tooltip("Used when productKind = Squad.")]
    public SquadPlan squadPlan;

    [Header("UI")]
    public Sprite Icon;
}