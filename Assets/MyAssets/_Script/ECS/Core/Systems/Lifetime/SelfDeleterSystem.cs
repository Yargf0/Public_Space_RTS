using Unity.Burst;
using Unity.Entities;

partial struct SelfDeleterSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {

        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(state.WorldUpdateAllocator);
        foreach ((
                    RefRW<SelfDeleter> selfDeleter,
                    Entity entity)
                    in SystemAPI.Query<
                        RefRW<SelfDeleter>>().WithEntityAccess())
        {
            selfDeleter.ValueRW.LifeTime -=SystemAPI.Time.DeltaTime;

            if (selfDeleter.ValueRO.LifeTime < 0)
            {
                entityCommandBuffer.DestroyEntity(entity);
            }

        }
        entityCommandBuffer.Playback(state.EntityManager);
    }
}
