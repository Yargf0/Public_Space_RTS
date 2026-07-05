using Unity.Entities;

public struct SquadMemberDeathEvent : IComponentData
{
    public Entity squad;
    public Entity ship;
    public int slotIndex;
}

// so ships staying at 0 hp don't emit death event again and again
public struct SquadMemberDeathEventEmittedTag : IComponentData
{
}
