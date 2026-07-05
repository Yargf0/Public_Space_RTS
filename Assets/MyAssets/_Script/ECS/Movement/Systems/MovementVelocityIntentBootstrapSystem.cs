using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(PathVelocitySystem))]
public partial struct MovementVelocityIntentBootstrapSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowFieldData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach ((RefRO<Velocity> velocity, Entity entity) in SystemAPI.Query<RefRO<Velocity>>()
                     .WithAll<ShipStateComponent, UnitMover, LocalTransform>()
                     .WithAbsent<MovementVelocityIntent>()
                     .WithEntityAccess())
        {
            ecb.AddComponent(entity, new MovementVelocityIntent());
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
