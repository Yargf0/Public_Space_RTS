using Unity.Entities;
using Unity.Mathematics;

public enum SquadRole : byte
{
    Interceptor = 0,
    Skirmisher = 1,
    Assault = 2,
    Escort = 3,
}

public enum SquadOrigin : byte
{
    ArmyPlan = 0,
    Carrier = 1,
}

public enum SquadCommandType : byte
{
    MoveToPoint = 0,
    AttackMoveToPoint = 1,
    AttackTarget = 2,
    FollowEntity = 3,
    Stop = 4,
    SetFireMode = 5,
    SetMoveMode = 6,
}

// Tag for the squad entity.
public struct SquadronTag : IComponentData { }

public struct SquadComponent : IComponentData
{
    public byte squadId;
    public Faction faction;
    public SquadRole role;

    public int maxMembers;
    public int aliveCount;

    public FormationType formation;
    public float spacing;

    public float2 anchorPosition;
    public Entity anchorEntity;

    // Soft target hint. Not a forced target.
    public Entity priorityTarget;

    public FireMode defaultFireMode;
    public MoveMode defaultMoveMode;

    public Tactics tactics;
    public Stance currentStance;
    public uint lastGroupOrderVersion;

    public SquadOrigin origin;
    public Entity originEntity;
    public int carrierSlotIndex;

    public float regenTimer;
    public float enduranceRemaining;
}

[InternalBufferCapacity(16)]
public struct SquadMember : IBufferElementData
{
    public Entity ship;

    // Template slot. Not for current formation layout.
    public int slotIndex;

    // Compact runtime formation slot among alive members.
    public int formationSlotIndex;

    // cache so we don't recalc formation math every tick
    public float2 formationOffset;
    public int cachedFormationSlotIndex;
    public byte formationOffsetValid;
}

// Template: which prefab goes to which slot in mixed squads.
[InternalBufferCapacity(16)]
public struct SquadSlotTemplate : IBufferElementData
{
    public int slotIndex;
    public Entity memberPrefab;
    public int memberPrefabIndex;
}

public struct ShipSquadRef : IComponentData
{
    public Entity squad;

    // Template slot.
    public int slotIndex;

    // Compact runtime formation slot among alive members.
    public int formationSlotIndex;
}

public struct ShipPriorityHint : IComponentData
{
    public Entity target;
    public float weight;
}

public struct SquadDefaultsSettings : IComponentData
{
    public float priorityHintWeight;
    public float reachedDistance;
}

public struct SquadDefaultsRuntime : IComponentData
{
    public float nextUpdateTime;
    public FormationType cachedFormation;
    public float cachedSpacing;
    public int cachedMemberCount;
    public byte cachedReturning;
    public byte formationCacheValid;
}

public struct CreateSquadCommand : IComponentData
{
    public Faction faction;
    public SquadRole role;

    public ShipState initialState;
    public float2 initialTargetPosition;

    public int memberCount;
    public FormationType formation;
    public float spacing;

    public float2 spawnAnchor;
    public Entity anchorEntity;

    public FireMode defaultFireMode;
    public MoveMode defaultMoveMode;

    public SquadOrigin origin;
    public Entity originEntity;
    public int carrierSlotIndex;
    public float initialEndurance;

    public Entity targetStrikeGroupEntity;
    public Tactics initialTactics;
    public int requestTag;
}

[InternalBufferCapacity(16)]
public struct CreateSquadMemberTemplate : IBufferElementData
{
    public int slotIndex;
    public Entity memberPrefab;
    public int memberPrefabIndex;
}

[InternalBufferCapacity(8)]
public struct SquadCommandElement : IBufferElementData
{
    public SquadCommandType type;
    public float2 targetPosition;
    public Entity targetEntity;
    public FireMode fireMode;
    public MoveMode moveMode;
}
