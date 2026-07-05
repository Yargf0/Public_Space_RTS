using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SetTotalVelocitySystem))]
public partial struct MoveVelocitySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowFieldData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach ((
            RefRW<LocalTransform> localTransform,
            RefRO<Velocity> velocity,
            RefRO<Unit> unit)
            in SystemAPI.Query<RefRW<LocalTransform>, RefRO<Velocity>, RefRO<Unit>>()
                .WithAbsent<Rocket>()
                .WithAbsent<Bullet>())
        {
            float2 v = velocity.ValueRO.velocity;

            if (!math.isfinite(v.x) || !math.isfinite(v.y))
            {
                continue;
            }

            float3 pos = localTransform.ValueRO.Position;
            pos.x += v.x * dt;
            pos.y += v.y * dt;
            pos.z = GameConstants.GetShipZ(unit.ValueRO.shipSize);

            localTransform.ValueRW.Position = pos;
        }
    }
}
