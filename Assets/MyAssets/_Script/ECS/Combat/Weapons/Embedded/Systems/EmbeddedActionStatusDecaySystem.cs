using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EmbeddedActionTargetSystem))]
[UpdateBefore(typeof(EmbeddedBeamActionSystem))]
[UpdateBefore(typeof(EmbeddedAuraActionSystem))]
public partial struct EmbeddedActionStatusDecaySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach ((RefRW<EmpStatus> emp, Entity entity) in
                 SystemAPI.Query<RefRW<EmpStatus>>()
                     .WithEntityAccess())
        {
            emp.ValueRW.timer -= dt;
            if (emp.ValueRO.timer > 0f)
            {
                continue;
            }

            emp.ValueRW.timer = 0f;
            emp.ValueRW.moveSpeedMultiplier = 1f;
            emp.ValueRW.accelerationMultiplier = 1f;
            emp.ValueRW.disableWeapons = false;
            SystemAPI.SetComponentEnabled<EmpStatus>(entity, false);
        }

        foreach ((RefRW<EmbeddedActionBuffStatus> buff, Entity entity) in
                 SystemAPI.Query<RefRW<EmbeddedActionBuffStatus>>()
                     .WithEntityAccess())
        {
            buff.ValueRW.timer -= dt;
            if (buff.ValueRO.timer > 0f)
            {
                continue;
            }

            buff.ValueRW.timer = 0f;
            buff.ValueRW.effectMultiplier = 1f;
            buff.ValueRW.moveSpeedMultiplier = 1f;
            buff.ValueRW.accelerationMultiplier = 1f;
            SystemAPI.SetComponentEnabled<EmbeddedActionBuffStatus>(entity, false);
        }

        foreach ((RefRW<EmbeddedActionDebuffStatus> debuff, Entity entity) in
                 SystemAPI.Query<RefRW<EmbeddedActionDebuffStatus>>()
                     .WithEntityAccess())
        {
            debuff.ValueRW.timer -= dt;
            if (debuff.ValueRO.timer > 0f)
            {
                continue;
            }

            debuff.ValueRW.timer = 0f;
            debuff.ValueRW.effectMultiplier = 1f;
            debuff.ValueRW.moveSpeedMultiplier = 1f;
            debuff.ValueRW.accelerationMultiplier = 1f;
            debuff.ValueRW.disableWeapons = false;
            SystemAPI.SetComponentEnabled<EmbeddedActionDebuffStatus>(entity, false);
        }
    }
}
