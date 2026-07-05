using Unity.Entities;
using Unity.Mathematics;

public enum CombatVfxKind : byte
{
    MuzzleFlash = 0,
    ProjectileTracer = 1,
    Beam = 2,
    Impact = 3,
    Explosion = 4,
    DeathExplosion = 5,
    Status = 6,
}

public enum CombatVfxStyle : byte
{
    BallisticSmall = 0,
    BallisticMedium = 1,
    BallisticHeavy = 2,
    Flak = 3,
    RocketSmall = 4,
    MissileMedium = 5,
    TorpedoHeavy = 6,
    LaserThin = 7,
    RailgunHeavy = 8,
    Repair = 9,
    Emp = 10,
    ShipDeathSmall = 11,
    ShipDeathMedium = 12,
    ShipDeathBig = 13,
}

public struct CombatVfxRequest : IComponentData
{
    public byte kind;
    public byte style;
    public byte priority;
    public Faction faction;
    public float3 start;
    public float3 end;
    public float2 direction;
    public float radius;
    public float width;
    public float lifetime;
}

public struct CombatDeathVfxEmittedTag : IComponentData
{
}
