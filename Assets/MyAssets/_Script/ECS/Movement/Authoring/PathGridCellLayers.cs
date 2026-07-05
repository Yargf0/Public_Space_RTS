using Unity.Entities;

public struct PathGridCellSmall : IBufferElementData
{
    public int Cost;
    public bool Walkable;
}

public struct PathGridCellMedium : IBufferElementData
{
    public int Cost;
    public bool Walkable;
}

public struct PathGridCellLarge : IBufferElementData
{
    public int Cost;
    public bool Walkable;
}
