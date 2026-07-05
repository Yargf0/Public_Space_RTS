using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
partial struct CriticalObjectivesStateSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<CriticalObjectivesState>(out Entity stateEntity))
            return;

        int totalCount = 0;
        int aliveCount = 0;

        foreach ((RefRO<CriticalShipRole> role, RefRO<CriticalShipStatus> criticalShipStatus) in SystemAPI.Query<RefRO<CriticalShipRole>, RefRO<CriticalShipStatus>>())
        {
            if (!role.ValueRO.includeInAllAliveCheck)
                continue;

            totalCount++;
            if (criticalShipStatus.ValueRO.isAlive)
                aliveCount++;
        }

        SystemAPI.SetComponent(stateEntity, new CriticalObjectivesState
        {
            totalCount = totalCount,
            aliveCount = aliveCount,
            allAlive = totalCount > 0 && aliveCount == totalCount,
        });
    }
}