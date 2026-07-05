using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StrikeGroupSummarySystem))]
[UpdateBefore(typeof(StrikeGroupOrderExecutionSystem))]
public partial struct TacticsSyncSystem : ISystem
{
    private ComponentLookup<SquadComponent> squadLookup;

    public void OnCreate(ref SystemState state)
    {
        squadLookup = state.GetComponentLookup<SquadComponent>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        squadLookup.Update(ref state);

        foreach ((RefRO<StrikeGroupData> data,
                  DynamicBuffer<StrikeGroupSquadElement> squads)
                 in SystemAPI.Query<RefRO<StrikeGroupData>, DynamicBuffer<StrikeGroupSquadElement>>()
                     .WithAll<StrikeGroupTag>())
        {
            Tactics tactics = data.ValueRO.tactics;

            for (int i = 0; i < squads.Length; i++)
            {
                Entity squadEntity = squads[i].squadEntity;
                if (squadEntity == Entity.Null || !squadLookup.HasComponent(squadEntity))
                    continue;

                SquadComponent squad = squadLookup[squadEntity];
                if (squad.tactics == tactics)
                    continue;

                squad.tactics = tactics;
                squadLookup[squadEntity] = squad;
            }
        }
    }
}
