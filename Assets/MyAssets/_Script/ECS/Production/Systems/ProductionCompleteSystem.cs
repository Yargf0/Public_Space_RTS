using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ProductionProgressSystem))]
public partial struct ProductionCompleteSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach ((RefRW<ActiveProduction> activeProduction,
                  RefRO<ProducerOwner> owner,
                  RefRO<ProducerState> producerState,
                  RefRO<ProducerSpawnPoint> spawnPoint,
                  RefRO<ProducerRallyPoint> rallyPoint,
                  DynamicBuffer<ProducerEvent> events,
                  Entity entity)
                 in SystemAPI.Query<RefRW<ActiveProduction>,
                                    RefRO<ProducerOwner>,
                                    RefRO<ProducerState>,
                                    RefRO<ProducerSpawnPoint>,
                                    RefRO<ProducerRallyPoint>,
                                    DynamicBuffer<ProducerEvent>>().WithEntityAccess())
        {
            if (!activeProduction.ValueRO.isActive || activeProduction.ValueRO.timer > 0f)
                continue;

            if (!ProductionUtility.CanProducerWork(entityManager, entity, producerState.ValueRO, out _))
                continue;

            float3 spawnPosition = ResolveSpawnPosition(entityManager, entity, spawnPoint.ValueRO, out quaternion spawnRotation);
            float3 rallyPosition = ResolveRallyPosition(entityManager, spawnPosition, rallyPoint.ValueRO);

            bool completed = false;

            if (activeProduction.ValueRO.productKind == ProductionProductKind.Ship)
            {
                completed = CompleteShipProduction(
                    entityManager,
                    ecb,
                    activeProduction.ValueRO,
                    owner.ValueRO.faction,
                    spawnPosition,
                    spawnRotation,
                    rallyPoint.ValueRO,
                    events);
            }
            else if (activeProduction.ValueRO.productKind == ProductionProductKind.Squad)
            {
                completed = CompleteSquadProduction(
                    entityManager,
                    ecb,
                    activeProduction.ValueRO,
                    owner.ValueRO.faction,
                    spawnPosition.xy,
                    rallyPosition.xy,
                    events);
            }

            if (completed)
            {
                events.Add(new ProducerEvent
                {
                    type = (byte)ProducerEventType.Completed,
                    shipId = activeProduction.ValueRO.shipId,
                    reason = (byte)ProducerRejectReason.None,
                });
            }

            ResetActiveProduction(ref activeProduction.ValueRW);
        }
    }

    private static bool CompleteShipProduction(
        EntityManager entityManager,
        EntityCommandBuffer ecb,
        ActiveProduction activeProduction,
        Faction faction,
        float3 spawnPosition,
        quaternion spawnRotation,
        ProducerRallyPoint rallyPoint,
        DynamicBuffer<ProducerEvent> events)
    {
        if (activeProduction.prefab == Entity.Null)
        {
            events.Add(new ProducerEvent
            {
                type = (byte)ProducerEventType.QueueRejected,
                shipId = activeProduction.shipId,
                reason = (byte)ProducerRejectReason.InvalidPrefab,
            });

            return false;
        }

        Entity spawnedEntity = ecb.Instantiate(activeProduction.prefab);

        if (entityManager.HasComponent<LocalTransform>(activeProduction.prefab))
        {
            LocalTransform prefabTransform = entityManager.GetComponentData<LocalTransform>(activeProduction.prefab);
            LocalTransform spawnTransform = LocalTransform.FromPositionRotationScale(spawnPosition, spawnRotation, prefabTransform.Scale);
            SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
                ref ecb,
                spawnedEntity,
                spawnTransform,
                true,
                entityManager.HasComponent<LocalToWorld>(activeProduction.prefab));
        }

        ProductionUtility.ApplyFactionToSpawnedEntity(entityManager, activeProduction.prefab, spawnedEntity, faction, ecb);

        if (!entityManager.HasComponent<ShipCatalogId>(activeProduction.prefab))
        {
            ecb.AddComponent(spawnedEntity, new ShipCatalogId
            {
                Value = activeProduction.shipId,
            });
        }

        ShipRallyOrder rallyOrder = new ShipRallyOrder
        {
            mode = rallyPoint.mode,
            worldPoint = rallyPoint.worldPoint,
            followEntity = rallyPoint.followEntity,
        };

        if (entityManager.HasComponent<ShipRallyOrder>(activeProduction.prefab))
            ecb.SetComponent(spawnedEntity, rallyOrder);
        else
            ecb.AddComponent(spawnedEntity, rallyOrder);

        return true;
    }

    private static bool CompleteSquadProduction(
        EntityManager entityManager,
        EntityCommandBuffer ecb,
        ActiveProduction activeProduction,
        Faction faction,
        float2 spawnPosition,
        float2 targetPosition,
        DynamicBuffer<ProducerEvent> events)
    {
        if (!ShipCatalogApi.TryGetShip(entityManager, activeProduction.shipId, out ShipCatalogElement product))
        {
            events.Add(new ProducerEvent
            {
                type = (byte)ProducerEventType.QueueRejected,
                shipId = activeProduction.shipId,
                reason = (byte)ProducerRejectReason.ShipNotFound,
            });

            return false;
        }

        if (product.productKind != ProductionProductKind.Squad)
        {
            events.Add(new ProducerEvent
            {
                type = (byte)ProducerEventType.QueueRejected,
                shipId = activeProduction.shipId,
                reason = (byte)ProducerRejectReason.InvalidPrefab,
            });

            return false;
        }

        if (!ShipCatalogApi.TryGetSquadMemberBuffer(entityManager, out DynamicBuffer<ShipCatalogSquadMemberElement> sourceMembers))
        {
            events.Add(new ProducerEvent
            {
                type = (byte)ProducerEventType.QueueRejected,
                shipId = activeProduction.shipId,
                reason = (byte)ProducerRejectReason.InvalidPrefab,
            });

            return false;
        }

        Entity requestEntity = ecb.CreateEntity();

        ecb.AddComponent(requestEntity, new SpawnSquadRequest
        {
            mode = SpawnSquadRequestMode.SpawnNewSquad,

            targetSquadEntity = Entity.Null,
            targetStrikeGroupEntity = Entity.Null,

            singlePrefab = Entity.Null,
            singlePrefabCount = 0,
            singlePrefabMemberIndex = 0,

            spawnPosition = spawnPosition,
            targetPosition = targetPosition,
            faction = faction,

            initialState = ShipState.MovingToTarget,
            initialMoveMode = MoveMode.MoveAndEngage,
            initialFireMode = FireMode.FireAtWill,
            initialTactics = Tactics.Neutral,

            role = product.squadRole,

            createOrJoinStrikeGroup = false,
            groupId = 0,
            groupOwnership = StrikeGroupOwnership.Debug,
            ownerEntity = Entity.Null,

            requestTag = 0,
        });

        DynamicBuffer<SpawnSquadRequestMemberElement> requestMembers =
            ecb.AddBuffer<SpawnSquadRequestMemberElement>(requestEntity);

        int added = ShipCatalogApi.FillSquadRequestMembers(
            sourceMembers,
            requestMembers,
            activeProduction.shipId);

        if (added <= 0)
        {
            ecb.DestroyEntity(requestEntity);

            events.Add(new ProducerEvent
            {
                type = (byte)ProducerEventType.QueueRejected,
                shipId = activeProduction.shipId,
                reason = (byte)ProducerRejectReason.InvalidPrefab,
            });

            return false;
        }

        return true;
    }

    private static float3 ResolveSpawnPosition(
        EntityManager entityManager,
        Entity producerEntity,
        ProducerSpawnPoint spawnPoint,
        out quaternion spawnRotation)
    {
        spawnRotation = quaternion.identity;

        if (spawnPoint.spawnPointEntity != Entity.Null &&
            entityManager.HasComponent<LocalToWorld>(spawnPoint.spawnPointEntity))
        {
            LocalToWorld ltw = entityManager.GetComponentData<LocalToWorld>(spawnPoint.spawnPointEntity);
            spawnRotation = ltw.Rotation;
            return ltw.Position;
        }

        if (entityManager.HasComponent<LocalToWorld>(producerEntity))
        {
            LocalToWorld ltw = entityManager.GetComponentData<LocalToWorld>(producerEntity);
            spawnRotation = ltw.Rotation;
            return ltw.Position;
        }

        return float3.zero;
    }

    private static float3 ResolveRallyPosition(
        EntityManager entityManager,
        float3 fallbackPosition,
        ProducerRallyPoint rallyPoint)
    {
        RallyPointMode mode = (RallyPointMode)rallyPoint.mode;

        if (mode == RallyPointMode.FollowEntity &&
            rallyPoint.followEntity != Entity.Null &&
            entityManager.HasComponent<LocalToWorld>(rallyPoint.followEntity))
        {
            return entityManager.GetComponentData<LocalToWorld>(rallyPoint.followEntity).Position;
        }

        if (mode == RallyPointMode.FollowPoint)
        {
            return rallyPoint.worldPoint;
        }

        return fallbackPosition;
    }

    private static void ResetActiveProduction(ref ActiveProduction activeProduction)
    {
        activeProduction.isActive = false;
        activeProduction.shipId = -1;
        activeProduction.productKind = ProductionProductKind.Ship;
        activeProduction.prefab = Entity.Null;
        activeProduction.timer = 0f;
        activeProduction.totalTime = 0f;
    }
}