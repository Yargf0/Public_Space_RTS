using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// rebuilds asteroid map every frame
// asteroids are few, full rebuild is cheaper than dirty flags
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(HarvesterFindTargetSystem))]
public partial struct AsteroidGridRegisterSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AsteroidGridData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        RefRW<AsteroidGridData> gridData = SystemAPI.GetSingletonRW<AsteroidGridData>();
        gridData.ValueRW.Map.Clear();

        foreach ((RefRO<AsteroidData> asteroid, RefRO<LocalTransform> transform, Entity entity)
                 in SystemAPI.Query<RefRO<AsteroidData>, RefRO<LocalTransform>>()
                     .WithAll<AsteroidTag>()
                     .WithEntityAccess())
        {
            // skip empty asteroids, nothing to mine
            if (asteroid.ValueRO.currentAmount <= 0f)
            {
                continue;
            }

            float2 pos = transform.ValueRO.Position.xy;
            int2 cell = GridUtility.WorldToSmallCell(pos);

            gridData.ValueRW.Map.Add(cell, new AsteroidGridEntry
            {
                Entity = entity,
                Position = pos,
                ResourceType = asteroid.ValueRO.resourceType,
            });
        }
    }
}