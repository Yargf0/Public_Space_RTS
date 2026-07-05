using Unity.Collections;
using Unity.Entities;

public struct UiSelectionSnapshot
{
    public bool hasSelection;
    public int selectedCount;

    public bool hasShipStateSelection;
    public bool mixedFireMode;
    public FireMode fireMode;
    public bool mixedMoveMode;
    public MoveMode moveMode;

    public int producerCount;
    public Entity singleProducerEntity;

    public int carrierCount;
    public Entity singleCarrierEntity;
    public bool hasCarrierStanceSelection;
    public bool mixedCarrierStance;
    public CarrierStance carrierStance;
}

public static class UiSelectionQueryUtility
{
    public static UiSelectionSnapshot BuildSelectionSnapshot(EntityManager entityManager)
    {
        UiSelectionSnapshot snapshot = default;

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .Build(entityManager);

        NativeArray<Entity> selectedEntities = query.ToEntityArray(Allocator.Temp);

        snapshot.hasSelection = selectedEntities.Length > 0;

        bool fireInitialized = false;
        bool moveInitialized = false;
        bool carrierInitialized = false;

        Entity firstProducer = Entity.Null;
        Entity firstCarrier = Entity.Null;

        NativeHashSet<Entity> processedStateEntities =
            new NativeHashSet<Entity>(selectedEntities.Length, Allocator.Temp);

        NativeHashSet<Entity> processedProducerEntities =
            new NativeHashSet<Entity>(selectedEntities.Length, Allocator.Temp);

        NativeHashSet<Entity> processedCarrierEntities =
            new NativeHashSet<Entity>(selectedEntities.Length, Allocator.Temp);

        for (int i = 0; i < selectedEntities.Length; i++)
        {
            Entity selectedEntity = selectedEntities[i];

            if (!entityManager.Exists(selectedEntity))
            {
                continue;
            }

            Entity stateEntity = ResolveSquadStateEntity(entityManager, selectedEntity);

            if (stateEntity != Entity.Null &&
                entityManager.Exists(stateEntity) &&
                processedStateEntities.Add(stateEntity))
            {
                bool hasSquad = entityManager.HasComponent<SquadComponent>(stateEntity);
                bool hasShipState = entityManager.HasComponent<ShipStateComponent>(stateEntity);

                if (hasSquad || hasShipState)
                {
                    snapshot.selectedCount++;

                    FireMode fireMode;
                    MoveMode moveMode;

                    if (hasSquad)
                    {
                        SquadComponent squad = entityManager.GetComponentData<SquadComponent>(stateEntity);
                        fireMode = squad.defaultFireMode;
                        moveMode = squad.defaultMoveMode;
                    }
                    else
                    {
                        ShipStateComponent shipState = entityManager.GetComponentData<ShipStateComponent>(stateEntity);
                        fireMode = shipState.mode;
                        moveMode = shipState.moveMode;
                    }

                    snapshot.hasShipStateSelection = true;

                    if (!fireInitialized)
                    {
                        snapshot.fireMode = fireMode;
                        fireInitialized = true;
                    }
                    else if (snapshot.fireMode != fireMode)
                    {
                        snapshot.mixedFireMode = true;
                    }

                    if (!moveInitialized)
                    {
                        snapshot.moveMode = moveMode;
                        moveInitialized = true;
                    }
                    else if (snapshot.moveMode != moveMode)
                    {
                        snapshot.mixedMoveMode = true;
                    }
                }
            }

            Entity uiEntity = stateEntity != Entity.Null ? stateEntity : selectedEntity;

            if (entityManager.Exists(uiEntity) &&
                entityManager.HasComponent<ProducerTag>(uiEntity) &&
                processedProducerEntities.Add(uiEntity))
            {
                snapshot.producerCount++;

                if (snapshot.producerCount == 1)
                {
                    firstProducer = uiEntity;
                }
            }

            if (entityManager.Exists(uiEntity) &&
                entityManager.HasComponent<CarrierTag>(uiEntity) &&
                entityManager.HasComponent<CarrierHangarState>(uiEntity) &&
                processedCarrierEntities.Add(uiEntity))
            {
                snapshot.carrierCount++;

                if (snapshot.carrierCount == 1)
                {
                    firstCarrier = uiEntity;
                }

                CarrierHangarState hangar =
                    entityManager.GetComponentData<CarrierHangarState>(uiEntity);

                snapshot.hasCarrierStanceSelection = true;

                if (!carrierInitialized)
                {
                    snapshot.carrierStance = hangar.stance;
                    carrierInitialized = true;
                }
                else if (snapshot.carrierStance != hangar.stance)
                {
                    snapshot.mixedCarrierStance = true;
                }
            }
        }

        snapshot.singleProducerEntity = snapshot.producerCount == 1 ? firstProducer : Entity.Null;
        snapshot.singleCarrierEntity = snapshot.carrierCount == 1 ? firstCarrier : Entity.Null;

        processedCarrierEntities.Dispose();
        processedProducerEntities.Dispose();
        processedStateEntities.Dispose();

        selectedEntities.Dispose();
        query.Dispose();

        return snapshot;
    }

    private static Entity ResolveSquadStateEntity(EntityManager entityManager, Entity selectedEntity)
    {
        if (!entityManager.Exists(selectedEntity))
        {
            return Entity.Null;
        }

        if (entityManager.HasComponent<ShipSquadRef>(selectedEntity))
        {
            ShipSquadRef squadRef = entityManager.GetComponentData<ShipSquadRef>(selectedEntity);
            Entity squadEntity = squadRef.squad;

            if (squadEntity != Entity.Null &&
                entityManager.Exists(squadEntity) &&
                (entityManager.HasComponent<SquadComponent>(squadEntity) ||
                 entityManager.HasComponent<ShipStateComponent>(squadEntity)))
            {
                return squadEntity;
            }
        }

        return selectedEntity;
    }
}