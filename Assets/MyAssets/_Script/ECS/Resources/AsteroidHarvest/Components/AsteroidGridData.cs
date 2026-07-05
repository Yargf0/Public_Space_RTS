using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// spatial map of asteroids, singleton
// used by HarvesterFindTargetSystem to find nearest asteroid
public struct AsteroidGridData : IComponentData
{
    public NativeParallelMultiHashMap<int2, AsteroidGridEntry> Map;
}

// map entry: entity, position, resource type
public struct AsteroidGridEntry
{
    public Entity Entity;
    public float2 Position;
    public AsteroidResourceType ResourceType;
}