using Unity.Entities;
using Unity.Mathematics;

public struct WaypointTag : IComponentData { }

public struct Waypoint : IComponentData
{
    public int id;
    public float2 position;
    public float radius;
}

public struct ZoneVolumeTag : IComponentData { }
