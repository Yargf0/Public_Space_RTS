using Unity.Entities;
using Unity.Mathematics;

public struct StrikeGroupTag : IComponentData { }

public enum StrikeGroupOwnership : byte
{
    Director = 0,
    Player = 1,
    Debug = 2,
}

public struct StrikeGroupData : IComponentData
{
    public int groupId;
    public Faction faction;

    public StrikeGroupOwnership ownership;
    public Entity ownerEntity;

    public Tactics tactics;

    public float2 center;
    public float readiness;
    public int activeSquadCount;
    public int aliveMemberCount;
    public int totalMemberCount;

    public float summaryTimer;

    // Time since group was created. For cleanup grace.
    public float lifetime;

    // How long the group has no squads.
    public float emptyTimer;
}

public struct StrikeGroupOrder : IComponentData
{
    public Stance stance;
    public Entity targetEntity;
    public float2 targetPosition;
    public float radius;
    public uint version;
}

public struct StrikeGroupOrderRuntime : IComponentData
{
    public uint appliedVersion;
}

[InternalBufferCapacity(8)]
public struct StrikeGroupSquadElement : IBufferElementData
{
    public Entity squadEntity;
}

public struct StrikeGroupMember : IComponentData
{
    public Entity groupEntity;
    public int groupId;
}

public struct StrikeGroupIdAllocator : IComponentData
{
    public int nextGroupId;
}
