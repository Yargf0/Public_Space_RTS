using Unity.Burst;
using Unity.Entities;

public struct VisibilityHitRequest : IComponentData
{
    public Entity targetEntity;
    public Faction observerFaction;
    public float duration;
}

[BurstCompile]
[UpdateAfter(typeof(BulletMoverSystem))]
[UpdateAfter(typeof(RocketMoverSystem))]
[UpdateAfter(typeof(HitscanFireRequestExecutionSystem))]
public partial struct VisibilityHitRequestSystem : ISystem
{
    private ComponentLookup<Visibility> visibilityLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        visibilityLookup = state.GetComponentLookup<Visibility>(isReadOnly: false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        visibilityLookup.Update(ref state);
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((RefRO<VisibilityHitRequest> request, Entity entity)
            in SystemAPI.Query<RefRO<VisibilityHitRequest>>().WithEntityAccess())
        {
            VisibilityUtility.RefreshVisibilityForFaction(
                request.ValueRO.targetEntity,
                request.ValueRO.observerFaction,
                request.ValueRO.duration,
                ref visibilityLookup);

            ecb.DestroyEntity(entity);
        }

        ecb.Playback(state.EntityManager);
    }
}
