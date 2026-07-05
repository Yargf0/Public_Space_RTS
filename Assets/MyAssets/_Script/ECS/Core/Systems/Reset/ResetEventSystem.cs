using Unity.Burst;
using Unity.Entities;
[UpdateInGroup(typeof(LateSimulationSystemGroup), OrderLast =true)]
partial struct ResetEventSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (RefRW<Selected> selected in SystemAPI.Query<RefRW<Selected>>().WithPresent<Selected>()) {

            if (!selected.ValueRO.OnSelected && !selected.ValueRO.OnDeselected)
            {
                continue;
            }

            selected.ValueRW.OnSelected = false;
            selected.ValueRW.OnDeselected = false;
        }
        foreach (RefRW<Health> health in SystemAPI.Query<RefRW<Health>>())
        {
            if (!health.ValueRO.onHealthChanged)
            {
                continue;
            }

            health.ValueRW.onHealthChanged = false;
        }
    }

}
