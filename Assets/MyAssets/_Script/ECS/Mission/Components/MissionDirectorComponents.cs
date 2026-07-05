using Unity.Entities;

public struct MissionDirectorTag : IComponentData { }

public struct MissionDirectorState : IComponentData
{
    public float missionTime;
    public bool initialSpawnsDone;
}

[InternalBufferCapacity(8)]
public struct MissionEventState : IBufferElementData
{
    public int eventIndex;
    public bool fired;
    public int fireCount;
    public float lastFireTime;
    public bool triggerHasEverExisted;
}
