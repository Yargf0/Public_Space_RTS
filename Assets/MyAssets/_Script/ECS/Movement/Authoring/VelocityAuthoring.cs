using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
class VelocityAuthoring : MonoBehaviour
{
    class Baker : Baker<VelocityAuthoring>
    {
        public override void Bake(VelocityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Velocity());
        }
    }
}
public struct Velocity : IComponentData
{
    public float2 velocity;
    public float2 separationVelocity;
    public float2 alignmentVelocity;
    public float2 cohesionVelocity;
    public float2 flowFieldVelocity;
}
