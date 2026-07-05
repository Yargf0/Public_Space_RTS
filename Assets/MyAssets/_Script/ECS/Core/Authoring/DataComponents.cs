using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct GridData : IComponentData
{
    public NativeParallelMultiHashMap<int2, Grid> EnemyEntityMap;
    public NativeParallelMultiHashMap<int2, Grid> EnemyEntityBigMap;

    public NativeParallelMultiHashMap<int2, Grid> FriendlyEntityMap;
    public NativeParallelMultiHashMap<int2, Grid> FriendlyEntityBigMap;
}

public struct Grid
{
    public Entity Entity;
    public float2 Position;
    public float2 CollisionRadius;
    public float2 Heading;
    public byte ShipSize;
}