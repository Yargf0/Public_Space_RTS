using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
public partial struct GridEntityDatabaseSystemInit : ISystem
{
    private Entity databaseEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        databaseEntity = state.EntityManager.CreateEntity();

#if UNITY_EDITOR
        state.EntityManager.SetName(databaseEntity, "GridEntityDatabase");
#endif

        state.EntityManager.AddComponentData(databaseEntity, new GridEntityDatabase());
        state.EntityManager.AddBuffer<GridEntityElement>(databaseEntity);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (state.EntityManager.Exists(databaseEntity))
            state.EntityManager.DestroyEntity(databaseEntity);
    }
}

public struct GridEntityElement : IBufferElementData
{
    public int2 Key;
    public byte SizeClass;
    public Entity Value;
}

public struct GridEntityDatabase : IComponentData
{
}