using Unity.Entities;
using Unity.Mathematics;

public struct GroupCell : IBufferElementData
{
    public int IntegrationValue;
    public float2 Direction;
}
