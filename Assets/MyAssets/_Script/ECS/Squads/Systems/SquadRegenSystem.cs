using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SquadronHealthSystem))]
[UpdateBefore(typeof(SquadronCleanupSystem))]
public partial struct SquadRegenSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        EntityManager em = state.EntityManager;

        EntityQuery shipyardQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<ProducerTag, ProducerOwner, ProducerConfig, LocalTransform>()
            .Build(em);

        NativeArray<Entity> shipyards = shipyardQuery.ToEntityArray(Allocator.Temp);
        NativeArray<ProducerOwner> owners = shipyardQuery.ToComponentDataArray<ProducerOwner>(Allocator.Temp);
        NativeArray<ProducerConfig> configs = shipyardQuery.ToComponentDataArray<ProducerConfig>(Allocator.Temp);
        NativeArray<LocalTransform> transforms = shipyardQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        // no ReinforceFirstEmptySlot inside Query, it makes structural changes
        // collect squads first, do structural stuff after loop
        NativeList<Entity> squadsToReinforce = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRW<SquadComponent> squad, Entity squadEntity)
            in SystemAPI.Query<RefRW<SquadComponent>>()
                .WithAll<SquadronTag>()
                .WithEntityAccess())
        {
            if (squad.ValueRO.origin != SquadOrigin.ArmyPlan)
                continue;

            if (squad.ValueRO.aliveCount <= 0)
                continue;

            if (squad.ValueRO.aliveCount >= squad.ValueRO.maxMembers)
            {
                squad.ValueRW.regenTimer = 0f;
                continue;
            }

            int chosen = -1;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < shipyards.Length; i++)
            {
                if (owners[i].faction != squad.ValueRO.faction)
                    continue;

                float radius = configs[i].repairRadius;
                if (radius <= 0f)
                    continue;

                float2 shipyardPos = transforms[i].Position.xy;
                float distSq = math.distancesq(squad.ValueRO.anchorPosition, shipyardPos);

                if (distSq > radius * radius)
                    continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    chosen = i;
                }
            }

            if (chosen < 0)
            {
                squad.ValueRW.regenTimer = 0f;
                continue;
            }

            float interval = math.max(0.05f, configs[chosen].regenInterval);
            squad.ValueRW.regenTimer += dt;

            if (squad.ValueRO.regenTimer < interval)
                continue;

            squad.ValueRW.regenTimer = 0f;
            squadsToReinforce.Add(squadEntity);
        }

        shipyards.Dispose();
        owners.Dispose();
        configs.Dispose();
        transforms.Dispose();
        shipyardQuery.Dispose();

        for (int i = 0; i < squadsToReinforce.Length; i++)
        {
            Entity squadEntity = squadsToReinforce[i];

            if (squadEntity == Entity.Null || !em.Exists(squadEntity))
                continue;

            SquadronSpawner.ReinforceFirstEmptySlot(em, squadEntity);
        }

        squadsToReinforce.Dispose();
    }
}