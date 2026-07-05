using Unity.Entities;

// disabled. statuses are baked into prefabs now, fallback is in FlushStatuses()
// reason: WithNone + IEnableable sometimes matches disabled components and this scans all units every frame
[DisableAutoCreation]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct EmbeddedActionStatusBootstrapSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Unit>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((RefRO<Unit> unit, Entity entity) in
                 SystemAPI.Query<RefRO<Unit>>()
                     .WithNone<EmpStatus>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                     .WithEntityAccess())
        {
            ecb.AddComponent(entity, new EmpStatus
            {
                timer = 0f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
                disableWeapons = false,
            });
            ecb.SetComponentEnabled<EmpStatus>(entity, false);
        }

        foreach ((RefRO<Unit> unit, Entity entity) in
                 SystemAPI.Query<RefRO<Unit>>()
                     .WithNone<EmbeddedActionBuffStatus>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                     .WithEntityAccess())
        {
            ecb.AddComponent(entity, new EmbeddedActionBuffStatus
            {
                timer = 0f,
                effectMultiplier = 1f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
            });
            ecb.SetComponentEnabled<EmbeddedActionBuffStatus>(entity, false);
        }

        foreach ((RefRO<Unit> unit, Entity entity) in
                 SystemAPI.Query<RefRO<Unit>>()
                     .WithNone<EmbeddedActionDebuffStatus>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                     .WithEntityAccess())
        {
            ecb.AddComponent(entity, new EmbeddedActionDebuffStatus
            {
                timer = 0f,
                effectMultiplier = 1f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
                disableWeapons = false,
            });
            ecb.SetComponentEnabled<EmbeddedActionDebuffStatus>(entity, false);
        }

        ecb.Playback(state.EntityManager);
        state.Enabled = false;
    }
}
