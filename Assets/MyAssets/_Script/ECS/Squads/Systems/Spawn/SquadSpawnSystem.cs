using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpawnSquadRequestSystem))]
[UpdateBefore(typeof(StrikeGroupSummarySystem))]
[UpdateBefore(typeof(SquadDefaultsSystem))]
public partial struct SquadSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CreateSquadCommand>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        NativeList<Entity> requests = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<CreateSquadCommand> _, Entity requestEntity)
            in SystemAPI.Query<RefRO<CreateSquadCommand>>().WithEntityAccess())
        {
            requests.Add(requestEntity);
        }

        for (int r = 0; r < requests.Length; r++)
        {
            Entity requestEntity = requests[r];
            if (!em.Exists(requestEntity))
                continue;

            CreateSquadCommand command = em.GetComponentData<CreateSquadCommand>(requestEntity);

            if (!em.HasBuffer<CreateSquadMemberTemplate>(requestEntity))
            {
                em.DestroyEntity(requestEntity);
                continue;
            }

            DynamicBuffer<CreateSquadMemberTemplate> templates =
                em.GetBuffer<CreateSquadMemberTemplate>(requestEntity);

            Entity squadEntity = SquadronSpawner.SpawnFromTemplates(em, command, templates);

            if (squadEntity != Entity.Null)
            {
                if (command.targetStrikeGroupEntity != Entity.Null &&
                    em.Exists(command.targetStrikeGroupEntity) &&
                    em.HasComponent<StrikeGroupTag>(command.targetStrikeGroupEntity))
                {
                    SquadConfigurator.AttachSquadToStrikeGroup(em, command.targetStrikeGroupEntity, squadEntity);
                }

                if (command.origin == SquadOrigin.Carrier)
                {
                    AttachCarrierSlot(em, command, squadEntity);
                }

                if (command.requestTag != 0)
                {
                    if (em.HasComponent<SpawnedByRequest>(squadEntity))
                    {
                        em.SetComponentData(squadEntity, new SpawnedByRequest
                        {
                            requestTag = command.requestTag
                        });
                    }
                    else
                    {
                        em.AddComponentData(squadEntity, new SpawnedByRequest
                        {
                            requestTag = command.requestTag
                        });
                    }
                }
            }

            em.DestroyEntity(requestEntity);
        }

        requests.Dispose();
    }

    private static void AttachCarrierSlot(EntityManager em, in CreateSquadCommand command, Entity squadEntity)
    {
        Entity carrierEntity = command.originEntity;
        int slotIndex = command.carrierSlotIndex;

        if (carrierEntity == Entity.Null ||
            slotIndex < 0 ||
            !em.Exists(carrierEntity) ||
            !em.HasBuffer<CarrierSquadronSlotElement>(carrierEntity))
        {
            return;
        }

        DynamicBuffer<CarrierSquadronSlotElement> slots =
            em.GetBuffer<CarrierSquadronSlotElement>(carrierEntity);

        if (slotIndex >= slots.Length)
            return;

        CarrierSquadronSlotElement slot = slots[slotIndex];
        slot.squadronEntity = squadEntity;
        slot.state = CarrierSlotState.Launched;
        slot.timer = 0f;
        slots[slotIndex] = slot;
    }

}