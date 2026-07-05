using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ProducerAuthoring : MonoBehaviour
{
    public Faction faction;
    public ShipCatalogAsset catalog;

    [Header("Build")]
    public ProducerShipOption[] ships;
    public int queueCapacity = 8;
    public float buildSpeed = 1f;
    public bool startEnabled = true;

    [Header("Spawn")]
    public GameObject spawnPointObject;

    [Header("Squad Regeneration")]
    public float repairRadius = 30f;
    public float regenInterval = 5f;

    [Header("Rally Point")]
    public RallyPointMode defaultRallyMode = RallyPointMode.FollowPoint;
    public Vector3 defaultRallyPoint;
    public GameObject defaultRallyFollowTarget;

    [ContextMenu("Sync Ships From Catalog")]
    void SyncShipsFromCatalog()
    {
        if (catalog == null || catalog.ships == null)
        {
            ships = Array.Empty<ProducerShipOption>();
            return;
        }

        ProducerShipOption[] oldShips = ships;
        ships = new ProducerShipOption[catalog.ships.Length];

        for (int i = 0; i < catalog.ships.Length; i++)
        {
            ShipCatalogAssetEntry src = catalog.ships[i];
            bool canBuild = false;

            if (oldShips != null)
            {
                for (int j = 0; j < oldShips.Length; j++)
                {
                    if (oldShips[j].shipId != src.id)
                        continue;

                    canBuild = oldShips[j].CanBuild;
                    break;
                }
            }

            ships[i] = new ProducerShipOption
            {
                shipId = src.id,
                Name = src.Name,
                CanBuild = canBuild,
            };
        }
    }

    void OnValidate()
    {
        SyncShipsFromCatalog();
    }

    class Baker : Baker<ProducerAuthoring>
    {
        public override void Bake(ProducerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new ProducerTag());
            AddComponent(entity, new ProducerOwner
            {
                faction = authoring.faction,
            });
            AddComponent(entity, new ProducerConfig
            {
                queueCapacity = math.max(1, authoring.queueCapacity),
                buildSpeed = math.max(0f, authoring.buildSpeed),
                repairRadius = math.max(0f, authoring.repairRadius),
                regenInterval = math.max(0.05f, authoring.regenInterval),
            });
            AddComponent(entity, new ProducerState
            {
                isEnabled = authoring.startEnabled,
            });
            AddComponent(entity, new ActiveProduction
            {
                isActive = false,
                shipId = -1,
                productKind = ProductionProductKind.Ship,
                prefab = Entity.Null,
                timer = 0f,
                totalTime = 0f,
            });

            Entity spawnPointEntity = Entity.Null;
            if (authoring.spawnPointObject != null)
                spawnPointEntity = GetEntity(authoring.spawnPointObject, TransformUsageFlags.Dynamic);

            Entity followEntity = Entity.Null;
            if (authoring.defaultRallyFollowTarget != null)
                followEntity = GetEntity(authoring.defaultRallyFollowTarget, TransformUsageFlags.Dynamic);

            AddComponent(entity, new ProducerSpawnPoint
            {
                spawnPointEntity = spawnPointEntity,
            });
            AddComponent(entity, new ProducerRallyPoint
            {
                mode = (byte)authoring.defaultRallyMode,
                worldPoint = (float3)authoring.defaultRallyPoint,
                followEntity = followEntity,
            });

            DynamicBuffer<ProducerAllowedShipId> allowedShips = AddBuffer<ProducerAllowedShipId>(entity);
            AddBuffer<ProducerBuildRequest>(entity);
            AddBuffer<ProducerBuildQueueElement>(entity);
            AddBuffer<ProducerEvent>(entity);

            if (authoring.ships == null || authoring.ships.Length == 0)
                return;

            for (int i = 0; i < authoring.ships.Length; i++)
            {
                if (!authoring.ships[i].CanBuild)
                    continue;

                allowedShips.Add(new ProducerAllowedShipId
                {
                    shipId = authoring.ships[i].shipId,
                });
            }
        }
    }
}

[Serializable]
public struct ProducerShipOption
{
    public int shipId;
    public string Name;
    public bool CanBuild;
}