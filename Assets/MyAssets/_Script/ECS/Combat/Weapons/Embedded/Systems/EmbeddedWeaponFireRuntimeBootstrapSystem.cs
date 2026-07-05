using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EmbeddedFindTargetSystem))]
public partial struct EmbeddedWeaponFireRuntimeBootstrapSystem : ISystem
{
    private EntityQuery missingRuntimeQuery;

    public void OnCreate(ref SystemState state)
    {
        missingRuntimeQuery = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<EmbeddedWeaponHost>(),
                ComponentType.ReadOnly<EmbeddedWeaponSlot>(),
            },
            None = new[]
            {
                ComponentType.ReadOnly<EmbeddedWeaponFireRuntime>(),
            },
        });
        state.RequireForUpdate(missingRuntimeQuery);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((DynamicBuffer<EmbeddedWeaponSlot> slots, Entity entity) in
                 SystemAPI.Query<DynamicBuffer<EmbeddedWeaponSlot>>()
                     .WithAll<EmbeddedWeaponHost>()
                     .WithNone<EmbeddedWeaponFireRuntime>()
                     .WithEntityAccess())
        {
            ecb.AddComponent(entity, EmbeddedWeaponFireRuntimeUtility.Build(slots));
        }

        ecb.Playback(state.EntityManager);
    }
}
