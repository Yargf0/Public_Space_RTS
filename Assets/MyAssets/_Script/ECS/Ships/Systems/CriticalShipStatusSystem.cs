using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(CriticalObjectivesStateSystem))]
partial struct CriticalShipStatusSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach ((RefRO<LocalToWorld> localToWorld, RefRO<Health> health, RefRW<CriticalShipStatus> criticalShipStatus) in SystemAPI.Query<RefRO<LocalToWorld>, RefRO<Health>, RefRW<CriticalShipStatus>>())
        {
            float healthNormalized = 0f;
            if (health.ValueRO.healthAmountMax > 0f)
                healthNormalized = math.saturate(health.ValueRO.healthAmount / health.ValueRO.healthAmountMax);

            criticalShipStatus.ValueRW.position = localToWorld.ValueRO.Position;
            criticalShipStatus.ValueRW.healthNormalized = healthNormalized;
            criticalShipStatus.ValueRW.isAlive = health.ValueRO.healthAmount > 0f;
        }
    }
}