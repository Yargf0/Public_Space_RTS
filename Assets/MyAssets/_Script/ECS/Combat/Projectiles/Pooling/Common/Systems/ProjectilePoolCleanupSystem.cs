using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(BallisticFireRequestExecutionSystem))]
[UpdateBefore(typeof(RocketFireRequestExecutionSystem))]
public partial struct ProjectilePoolCleanupSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ProjectilePool>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        NativeList<Entity> invalidPoolEntities = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<ProjectilePool> pool, DynamicBuffer<ProjectilePoolAllElement> allProjectiles, Entity poolEntity)
            in SystemAPI.Query<RefRO<ProjectilePool>, DynamicBuffer<ProjectilePoolAllElement>>().WithEntityAccess())
        {
            Entity prefabEntity = pool.ValueRO.prefabEntity;
            if (prefabEntity != Entity.Null && entityManager.Exists(prefabEntity))
            {
                continue;
            }

            for (int i = 0; i < allProjectiles.Length; i++)
            {
                Entity projectileEntity = allProjectiles[i].entity;
                if (projectileEntity != Entity.Null && entityManager.Exists(projectileEntity))
                {
                    ecb.DestroyEntity(projectileEntity);
                }
            }

            invalidPoolEntities.Add(poolEntity);
            ecb.DestroyEntity(poolEntity);
        }

        if (invalidPoolEntities.Length > 0 && SystemAPI.TryGetSingletonEntity<ProjectilePoolRegistry>(out Entity registryEntity))
        {
            DynamicBuffer<ProjectilePoolPrefabLink> links = entityManager.GetBuffer<ProjectilePoolPrefabLink>(registryEntity);
            for (int i = links.Length - 1; i >= 0; i--)
            {
                ProjectilePoolPrefabLink link = links[i];
                if (link.poolEntity == Entity.Null || Contains(invalidPoolEntities, link.poolEntity) || !entityManager.Exists(link.poolEntity))
                {
                    links.RemoveAt(i);
                }
            }
        }

        invalidPoolEntities.Dispose();
        ecb.Playback(entityManager);
    }

    private static bool Contains(NativeList<Entity> entities, Entity entity)
    {
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i] == entity)
            {
                return true;
            }
        }

        return false;
    }
}
