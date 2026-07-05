using Unity.Entities;
using Unity.Mathematics;

public struct MovementVelocityIntent : IComponentData
{
    public float2 PathVelocity;
    public float2 CombatVelocity;
    public float2 AvoidanceVelocity;
    public byte InHardDanger;
    public byte ForceZeroVelocity;
}
