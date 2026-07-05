using Unity.Entities;

public struct MissionCommandTag : IComponentData { }

public struct MissionSpawnGroupCommand : IComponentData
{
    public Entity directorEntity;
    public int spawnPresetIndex;
}

public struct MissionOrderGroupCommand : IComponentData
{
    public Entity directorEntity;
    public int groupId;
    public Faction faction;
    public Stance stance;
    public int targetWaypointId;
    public Entity targetEntity;
    public float radius;
}

public struct MissionSetTacticsCommand : IComponentData
{
    public Entity directorEntity;
    public int groupId;
    public Faction faction;
    public Tactics tactics;
}

public struct MissionAttackFactionCommand : IComponentData
{
    public Entity directorEntity;
    public int groupId;
    public Faction faction;
    public Faction targetFaction;
    public Stance stance;
    public float radius;
}

public enum DestroyGroupMode : byte
{
    DetachSquadsOnly = 0,
    DestroySquads = 1,
    DestroySquadsAndShips = 2,
}

public struct MissionDestroyGroupCommand : IComponentData
{
    public Entity directorEntity;
    public int groupId;
    public Faction faction;
    public DestroyGroupMode mode;
}
