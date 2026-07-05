using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(InitializationSystemGroup))]
partial struct CriticalObjectivesStateInitSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        Entity entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new CriticalObjectivesState
        {
            totalCount = 0,
            aliveCount = 0,
            allAlive = false,
        });
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
    }
}