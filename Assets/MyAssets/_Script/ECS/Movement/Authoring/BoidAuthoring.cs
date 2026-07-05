using Unity.Entities;
using UnityEngine;

class BoidAuthoring : MonoBehaviour
{
    public float separationWeight;
    public float alignmentWeight;
    public float cohesionWeight;
    public float flowFieldWeight;
    public float neighborRadius;

    class Baker : Baker<BoidAuthoring>
    {
        public override void Bake(BoidAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Boid
            {
                separationWeight = authoring.separationWeight,
                alignmentWeight = authoring.alignmentWeight,
                cohesionWeight = authoring.cohesionWeight,
                flowFieldWeight = authoring.flowFieldWeight,
                neighborRadius = authoring.neighborRadius,
            });
        }
    }
}

public struct Boid : IComponentData
{
    public int neighborCount;
    public float separationWeight;
    public float alignmentWeight;
    public float cohesionWeight;
    public float flowFieldWeight;
    public float neighborRadius;
    public float myRadius;
}

