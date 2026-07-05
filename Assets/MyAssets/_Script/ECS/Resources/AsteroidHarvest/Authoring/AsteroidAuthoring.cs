using Unity.Entities;
using UnityEngine;

public class AsteroidAuthoring : MonoBehaviour
{
    [Header("Resource")]
    public AsteroidResourceType resourceType = AsteroidResourceType.Metal;
    public float maxAmount = 500f;

    class Baker : Baker<AsteroidAuthoring>
    {
        public override void Bake(AsteroidAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new AsteroidTag());

            AddComponent(entity, new AsteroidData
            {
                resourceType = authoring.resourceType,
                currentAmount = authoring.maxAmount,
                maxAmount = authoring.maxAmount,
            });
        }
    }
}

// tag for queries - all asteroids
public struct AsteroidTag : IComponentData { }

// asteroid data: resource type and amount left
public struct AsteroidData : IComponentData
{
    public AsteroidResourceType resourceType;
    public float currentAmount;
    public float maxAmount;
}