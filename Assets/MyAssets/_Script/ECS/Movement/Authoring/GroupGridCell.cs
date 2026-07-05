using Unity.Entities;
using Unity.Mathematics;

public struct GroupGridCell : IComponentData
{
    public float2 TargetPosition;
    public bool NeedsUpdate;
    public bool ReadyToMove;
    public PathfindingSizeClass SizeClass;
    public int FailedAttempts;

    // cache of last built flow field so same target don't run BFS again
    public bool HasCachedFlowField;
    public int2 CachedTargetCell;
    public int2 CachedResolvedTargetCell;
    public PathfindingSizeClass CachedSizeClass;
    public uint CachedObstacleBakeVersion;
}
