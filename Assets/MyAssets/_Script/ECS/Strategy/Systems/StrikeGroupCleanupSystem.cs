using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StrikeGroupSummarySystem))]
[UpdateBefore(typeof(TacticsSyncSystem))]
[UpdateBefore(typeof(StrikeGroupOrderExecutionSystem))]
public partial struct StrikeGroupCleanupSystem : ISystem
{
    private const float EmptyGroupCleanupDelay = 2f;

    public void OnUpdate(ref SystemState state)
    {
        NativeList<Entity> groupsToDestroy = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<StrikeGroupData> data, Entity groupEntity)
            in SystemAPI.Query<RefRO<StrikeGroupData>>()
                .WithAll<StrikeGroupTag>()
                .WithEntityAccess())
        {
            // grace window so MissionDirector sees group disappeared and new groups can spawn squads
            if (data.ValueRO.activeSquadCount == 0 && data.ValueRO.emptyTimer >= EmptyGroupCleanupDelay)
                groupsToDestroy.Add(groupEntity);
        }

        EntityManager em = state.EntityManager;
        for (int i = 0; i < groupsToDestroy.Length; i++)
        {
            Entity groupEntity = groupsToDestroy[i];
            if (groupEntity == Entity.Null || !em.Exists(groupEntity))
                continue;

            em.DestroyEntity(groupEntity);
        }

        groupsToDestroy.Dispose();
    }
}
