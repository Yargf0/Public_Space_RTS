using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class GlobalShipCatalogAuthoring : MonoBehaviour
{
    public ShipCatalogAsset catalog;

    class Baker : Baker<GlobalShipCatalogAuthoring>
    {
        public override void Bake(GlobalShipCatalogAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ShipCatalogTag());

            DynamicBuffer<ShipCatalogElement> catalogBuffer = AddBuffer<ShipCatalogElement>(entity);
            DynamicBuffer<ShipCatalogSquadMemberElement> squadMemberBuffer = AddBuffer<ShipCatalogSquadMemberElement>(entity);

            if (authoring.catalog == null || authoring.catalog.ships == null || authoring.catalog.ships.Length == 0)
                return;

            catalogBuffer.EnsureCapacity(authoring.catalog.ships.Length);

            HashSet<int> usedIds = new HashSet<int>();

            foreach (ShipCatalogAssetEntry src in authoring.catalog.ships)
            {
                if (src.id < 0)
                    continue;

                if (!usedIds.Add(src.id))
                {
                    Debug.LogWarning($"Duplicate production catalog id {src.id}. Skipping duplicate entry '{src.Name}'.", authoring);
                    continue;
                }

                if (src.productKind == ProductionProductKind.Ship && src.prefab == null)
                {
                    Debug.LogWarning($"Catalog item '{src.Name}' id={src.id} is Ship but has no prefab.", authoring);
                    continue;
                }

                if (src.productKind == ProductionProductKind.Squad && src.squadPlan == null)
                {
                    Debug.LogWarning($"Catalog item '{src.Name}' id={src.id} is Squad but has no SquadPlan.", authoring);
                    continue;
                }

                FixedString64Bytes itemName = default;
                string displayName = ResolveDisplayName(src);

                if (!string.IsNullOrEmpty(displayName))
                    itemName = displayName;

                Entity prefab = Entity.Null;

                if (src.productKind == ProductionProductKind.Ship && src.prefab != null)
                {
                    prefab = GetEntity(src.prefab, TransformUsageFlags.Dynamic);
                }

                SquadRole squadRole = SquadRole.Interceptor;

                if (src.productKind == ProductionProductKind.Squad && src.squadPlan != null)
                {
                    squadRole = src.squadPlan.role;

                    BakeSquadComposition(authoring, src, squadMemberBuffer);
                }

                catalogBuffer.Add(new ShipCatalogElement
                {
                    id = src.id,
                    name = itemName,
                    shipType = (int)src.ShipType,
                    cost = src.Cost,
                    buildTime = math.max(0f, src.BuildTime),
                    prefab = prefab,

                    productKind = src.productKind,

                    squadRole = squadRole,
                });
            }
        }

        private void BakeSquadComposition(
            GlobalShipCatalogAuthoring authoring,
            ShipCatalogAssetEntry catalogEntry,
            DynamicBuffer<ShipCatalogSquadMemberElement> squadMemberBuffer)
        {
            SquadPlan squadPlan = catalogEntry.squadPlan;

            if (squadPlan == null || squadPlan.composition == null || squadPlan.composition.Length == 0)
            {
                Debug.LogWarning($"Squad catalog item '{catalogEntry.Name}' id={catalogEntry.id} has empty composition.", authoring);
                return;
            }

            for (int i = 0; i < squadPlan.composition.Length; i++)
            {
                SquadCompositionEntry member = squadPlan.composition[i];

                if (member == null)
                    continue;

                if (member.count <= 0)
                    continue;

                if (member.prefab == null)
                {
                    Debug.LogWarning(
                        $"SquadPlan '{squadPlan.name}' member #{i} has no direct prefab reference. " +
                        $"Production baking uses SquadCompositionEntry.prefab, not prefabId.",
                        squadPlan);

                    continue;
                }

                squadMemberBuffer.Add(new ShipCatalogSquadMemberElement
                {
                    productId = catalogEntry.id,
                    prefab = GetEntity(member.prefab, TransformUsageFlags.Dynamic),
                    count = math.max(1, member.count),
                    memberPrefabIndex = member.memberPrefabIndex,
                });
            }
        }

        private static string ResolveDisplayName(ShipCatalogAssetEntry src)
        {
            if (!string.IsNullOrEmpty(src.Name))
                return src.Name;

            if (src.productKind == ProductionProductKind.Squad && src.squadPlan != null)
            {
                if (!string.IsNullOrEmpty(src.squadPlan.label))
                    return src.squadPlan.label;

                return src.squadPlan.name;
            }

            return string.Empty;
        }
    }
}

public struct ShipCatalogTag : IComponentData
{
}

[InternalBufferCapacity(16)]
public struct ShipCatalogElement : IBufferElementData
{
    public int id;
    public FixedString64Bytes name;
    public int shipType;
    public Cost cost;
    public float buildTime;

    // Used only for productKind = Ship.
    public Entity prefab;

    public ProductionProductKind productKind;

    // Used only for productKind = Squad.
    public SquadRole squadRole;
}

[InternalBufferCapacity(32)]
public struct ShipCatalogSquadMemberElement : IBufferElementData
{
    public int productId;
    public Entity prefab;
    public int count;
    public int memberPrefabIndex;
}

public struct ShipCatalogId : IComponentData
{
    public int Value;
}

[Serializable]
public struct Cost
{
    public float Energy;
    public float Mineral;
    public float Gas;
}
