using Unity.Entities;
using Unity.Mathematics;

public struct ProducerTag : IComponentData
{
}

public struct ProducerOwner : IComponentData
{
    public Faction faction;
}

public struct ProducerConfig : IComponentData
{
    public int queueCapacity;
    public float buildSpeed;

    // Squad regeneration radius. 0 = this producer does not regenerate squads.
    public float repairRadius;

    // Seconds per regenerated squad member.
    public float regenInterval;
}

public struct ProducerState : IComponentData
{
    public bool isEnabled;
}

public struct ProducerSpawnPoint : IComponentData
{
    public Entity spawnPointEntity;
}

public struct ProducerRallyPoint : IComponentData
{
    public byte mode;
    public float3 worldPoint;
    public Entity followEntity;
}

public struct ActiveProduction : IComponentData
{
    public bool isActive;
    public int shipId;
    public ProductionProductKind productKind;
    public Entity prefab;
    public float timer;
    public float totalTime;
}

[InternalBufferCapacity(16)]
public struct ProducerAllowedShipId : IBufferElementData
{
    public int shipId;
}

[InternalBufferCapacity(8)]
public struct ProducerBuildRequest : IBufferElementData
{
    public int shipId;
    public int count;
}

[InternalBufferCapacity(8)]
public struct ProducerBuildQueueElement : IBufferElementData
{
    public int shipId;
    public ProductionProductKind productKind;
    public Entity prefab;
    public float buildTime;
    public Cost cost;
}

[InternalBufferCapacity(16)]
public struct ProducerEvent : IBufferElementData
{
    public byte type;
    public int shipId;
    public byte reason;
}

public struct ShipRallyOrder : IComponentData
{
    public byte mode;
    public float3 worldPoint;
    public Entity followEntity;
}