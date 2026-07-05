using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
class UnitMoverAuthoring : MonoBehaviour
{
    public float maxSpeed;
    public float acceleration;
    public float rotationSpeed;
    public float followDistance = 8f;

    class Baker : Baker<UnitMoverAuthoring>
    {
        public override void Bake(UnitMoverAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitMover
            {
                maxSpeed = authoring.maxSpeed,
                acceleration = authoring.acceleration,
                rotationSpeed = authoring.rotationSpeed,
                followDistance = authoring.followDistance,
            });
        }
    }
}
public struct UnitMover : IComponentData
{
    public float2 fightTarget;
    public float maxSpeed;
    public float acceleration;
    public float rotationSpeed;
    public float2 targetPos;

    public float followDistance;
}
