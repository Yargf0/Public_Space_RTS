using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
public partial struct GridSystemInit : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        Entity gridEntity = state.EntityManager.CreateEntity();
        //Debug.Log(gridEntity.Index);
        state.EntityManager.AddComponentData(gridEntity, new GridData
        {
            EnemyEntityMap = new NativeParallelMultiHashMap<int2, Grid>(1000, Allocator.Persistent),
            FriendlyEntityMap = new NativeParallelMultiHashMap<int2, Grid>(1000, Allocator.Persistent),
            EnemyEntityBigMap = new NativeParallelMultiHashMap<int2, Grid>(1000, Allocator.Persistent),
            FriendlyEntityBigMap = new NativeParallelMultiHashMap<int2, Grid>(1000, Allocator.Persistent)
        });
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        foreach (var gridData in SystemAPI.Query<RefRW<GridData>>())
        {
            gridData.ValueRW.EnemyEntityMap.Dispose();
            gridData.ValueRW.FriendlyEntityMap.Dispose();
            gridData.ValueRW.EnemyEntityBigMap.Dispose();
            gridData.ValueRW.FriendlyEntityBigMap.Dispose();
        }
    }
}