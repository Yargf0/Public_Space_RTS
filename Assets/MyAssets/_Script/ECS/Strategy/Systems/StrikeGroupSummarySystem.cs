using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SquadSpawnSystem))]
[UpdateAfter(typeof(SquadronHealthSystem))]
public partial struct StrikeGroupSummarySystem : ISystem
{
    private const float SummaryInterval = 0.25f;

    private ComponentLookup<SquadComponent> squadLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private BufferLookup<SquadMember> memberLookup;

    public void OnCreate(ref SystemState state)
    {
        squadLookup = state.GetComponentLookup<SquadComponent>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        memberLookup = state.GetBufferLookup<SquadMember>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        squadLookup.Update(ref state);
        transformLookup.Update(ref state);
        memberLookup.Update(ref state);

        float dt = SystemAPI.Time.DeltaTime;
        EntityManager em = state.EntityManager;

        foreach ((RefRW<StrikeGroupData> data,
                  RefRW<LocalTransform> transform,
                  DynamicBuffer<StrikeGroupSquadElement> squads)
                 in SystemAPI.Query<RefRW<StrikeGroupData>, RefRW<LocalTransform>, DynamicBuffer<StrikeGroupSquadElement>>()
                     .WithAll<StrikeGroupTag>())
        {
            data.ValueRW.lifetime += dt;
            data.ValueRW.summaryTimer -= dt;
            if (data.ValueRO.summaryTimer > 0f)
                continue;

            data.ValueRW.summaryTimer = SummaryInterval;

            float2 shipPositionSum = float2.zero;
            int activeSquads = 0;
            int aliveMembers = 0;
            int totalMembers = 0;
            int positionedShips = 0;

            for (int i = squads.Length - 1; i >= 0; i--)
            {
                Entity squadEntity = squads[i].squadEntity;
                if (squadEntity == Entity.Null ||
                    !em.Exists(squadEntity) ||
                    !squadLookup.HasComponent(squadEntity))
                {
                    squads.RemoveAt(i);
                    continue;
                }

                SquadComponent squad = squadLookup[squadEntity];
                activeSquads++;
                aliveMembers += math.max(0, squad.aliveCount);
                totalMembers += math.max(0, squad.maxMembers);

                if (!memberLookup.HasBuffer(squadEntity))
                    continue;

                DynamicBuffer<SquadMember> members = memberLookup[squadEntity];
                for (int m = 0; m < members.Length; m++)
                {
                    Entity ship = members[m].ship;
                    if (ship == Entity.Null || !em.Exists(ship))
                        continue;

                    if (!transformLookup.HasComponent(ship))
                        continue;

                    shipPositionSum += transformLookup[ship].Position.xy;
                    positionedShips++;
                }
            }

            float2 center = positionedShips > 0 ? shipPositionSum / positionedShips : data.ValueRO.center;

            data.ValueRW.center = center;
            data.ValueRW.readiness = totalMembers > 0 ? (float)aliveMembers / totalMembers : 0f;
            data.ValueRW.activeSquadCount = activeSquads;
            data.ValueRW.aliveMemberCount = aliveMembers;
            data.ValueRW.totalMemberCount = totalMembers;
            data.ValueRW.emptyTimer = activeSquads > 0 ? 0f : data.ValueRO.emptyTimer + SummaryInterval;

            transform.ValueRW.Position = new float3(center.x, center.y, transform.ValueRW.Position.z);
        }
    }
}
