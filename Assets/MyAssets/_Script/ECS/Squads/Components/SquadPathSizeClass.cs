using Unity.Entities;

public struct SquadPathSizeClass : IComponentData
{
    public PathfindingSizeClass Value;
    public bool Valid;
}
