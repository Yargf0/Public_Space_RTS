using Unity.Entities;
using Unity.Mathematics;

public enum SpawnSquadRequestMode : byte
{
    SpawnNewSquad = 0,
    ReinforceExistingSquad = 1,
}

public struct SpawnSquadRequest : IComponentData
{
    public SpawnSquadRequestMode mode;

    public Entity targetSquadEntity;
    public Entity targetStrikeGroupEntity;

    public Entity singlePrefab;
    public int singlePrefabCount;
    public int singlePrefabMemberIndex;

    public float2 spawnPosition;
    public float2 targetPosition;

    public Faction faction;
    public SquadRole role;

    public ShipState initialState;
    public MoveMode initialMoveMode;
    public FireMode initialFireMode;
    public Tactics initialTactics;

    public bool createOrJoinStrikeGroup;
    public int groupId;
    public StrikeGroupOwnership groupOwnership;
    public Entity ownerEntity;

    public int requestTag;
}

public struct SpawnSquadRequestMemberElement : IBufferElementData
{
    public Entity prefab;
    public int count;
    public int memberPrefabIndex;
}

public struct SpawnedByRequest : IComponentData
{
    public int requestTag;
}
