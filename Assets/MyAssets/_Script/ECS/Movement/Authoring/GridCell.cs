using Unity.Entities;
using Unity.Mathematics;

public struct GridCell : IBufferElementData
{
    public int2 Index;       
    public int Cost; // move cost (1 = normal, 256 = wall)
    public bool Walkable; // can pass or not
}
