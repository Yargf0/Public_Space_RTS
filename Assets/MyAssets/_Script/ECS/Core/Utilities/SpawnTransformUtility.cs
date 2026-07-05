using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class SpawnTransformUtility
{
    public static LocalToWorld ToLocalToWorld(LocalTransform transform)
    {
        return new LocalToWorld
        {
            Value = float4x4.TRS(
                transform.Position,
                transform.Rotation,
                new float3(transform.Scale, transform.Scale, transform.Scale))
        };
    }

    public static void SetLocalTransformAndLocalToWorld(
        ref EntityCommandBuffer ecb,
        Entity entity,
        LocalTransform transform,
        bool hasLocalTransform,
        bool hasLocalToWorld)
    {
        if (hasLocalTransform)
        {
            ecb.SetComponent(entity, transform);
        }

        if (hasLocalToWorld)
        {
            ecb.SetComponent(entity, ToLocalToWorld(transform));
        }
    }

    public static void SetLocalTransformAndLocalToWorld(
        EntityManager entityManager,
        Entity entity,
        LocalTransform transform)
    {
        if (entity == Entity.Null || !entityManager.Exists(entity))
        {
            return;
        }

        if (entityManager.HasComponent<LocalTransform>(entity))
        {
            entityManager.SetComponentData(entity, transform);
        }

        if (entityManager.HasComponent<LocalToWorld>(entity))
        {
            entityManager.SetComponentData(entity, ToLocalToWorld(transform));
        }
    }
}
