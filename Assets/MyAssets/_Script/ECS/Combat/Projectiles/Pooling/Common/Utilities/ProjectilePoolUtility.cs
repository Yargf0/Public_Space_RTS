using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class ProjectilePoolUtility
{
    public static Entity AcquireOrDrop(
        ref EntityCommandBuffer ecb,
        EntityManager entityManager,
        Entity prefabEntity,
        Entity poolEntity,
        ref ProjectilePool pool)
    {
        bool triedEmergencyGrow = false;

        while (true)
        {
            DynamicBuffer<ProjectilePoolFreeElement> freeProjectiles = entityManager.GetBuffer<ProjectilePoolFreeElement>(poolEntity);

            while (freeProjectiles.Length > 0)
            {
                int lastIndex = freeProjectiles.Length - 1;
                Entity projectileEntity = freeProjectiles[lastIndex].entity;
                freeProjectiles.RemoveAt(lastIndex);

                if (!IsValidFreeProjectile(entityManager, projectileEntity, poolEntity, pool.kind))
                {
                    pool.duplicateFreeEntryCount++;
                    continue;
                }

                MarkAcquired(ref ecb, entityManager, projectileEntity, poolEntity, prefabEntity, ref pool);
                return projectileEntity;
            }

            if (!triedEmergencyGrow && pool.allowRuntimeExpand && pool.totalSpawned < pool.hardCap)
            {
                triedEmergencyGrow = true;
                int growCount = math.min(math.max(1, pool.growChunk), pool.hardCap - pool.totalSpawned);
                GrowFreeProjectiles(entityManager, prefabEntity, poolEntity, ref pool, growCount);
                continue;
            }

            pool.droppedShotCount++;
            return Entity.Null;
        }
    }

    public static int CleanInvalidFreeEntries(
        EntityManager entityManager,
        Entity poolEntity,
        ref ProjectilePool pool)
    {
        DynamicBuffer<ProjectilePoolFreeElement> freeProjectiles = entityManager.GetBuffer<ProjectilePoolFreeElement>(poolEntity);

        for (int i = freeProjectiles.Length - 1; i >= 0; i--)
        {
            Entity projectileEntity = freeProjectiles[i].entity;
            if (!IsValidFreeProjectile(entityManager, projectileEntity, poolEntity, pool.kind))
            {
                freeProjectiles.RemoveAt(i);
                pool.duplicateFreeEntryCount++;
            }
        }

        return freeProjectiles.Length;
    }

    public static void GrowFreeProjectiles(
        EntityManager entityManager,
        Entity prefabEntity,
        Entity poolEntity,
        ref ProjectilePool pool,
        int spawnCount)
    {
        if (spawnCount <= 0)
        {
            return;
        }

        NativeArray<Entity> projectileEntities = new NativeArray<Entity>(spawnCount, Allocator.Temp);
        entityManager.Instantiate(prefabEntity, projectileEntities);

        LocalTransform inactiveTransform = entityManager.GetComponentData<LocalTransform>(prefabEntity);
        inactiveTransform.Position = pool.inactivePosition;
        if (inactiveTransform.Scale <= 0f)
        {
            inactiveTransform.Scale = 1f;
        }

        DynamicBuffer<ProjectilePoolFreeElement> freeProjectiles = entityManager.GetBuffer<ProjectilePoolFreeElement>(poolEntity);
        DynamicBuffer<ProjectilePoolAllElement> allProjectiles = entityManager.GetBuffer<ProjectilePoolAllElement>(poolEntity);

        for (int i = 0; i < projectileEntities.Length; i++)
        {
            Entity projectileEntity = projectileEntities[i];
            entityManager.SetComponentData(projectileEntity, new ProjectilePoolMember
            {
                poolEntity = poolEntity,
                prefabEntity = prefabEntity,
                kind = pool.kind,
                inPool = 1,
            });

            ResetAfterGrow(entityManager, projectileEntity, pool.kind, inactiveTransform);

            freeProjectiles.Add(new ProjectilePoolFreeElement { entity = projectileEntity });
            allProjectiles.Add(new ProjectilePoolAllElement { entity = projectileEntity });
        }

        pool.totalSpawned += spawnCount;
        pool.runtimeExpandCount += spawnCount;
        projectileEntities.Dispose();
    }

    public static void ReleaseBulletOrDestroy(
        ref EntityCommandBuffer ecb,
        Entity bulletEntity,
        LocalTransform currentTransform,
        ref ComponentLookup<ProjectilePoolMember> poolMemberLookup,
        ref ComponentLookup<ProjectilePool> poolLookup,
        ref BufferLookup<ProjectilePoolFreeElement> freeLookup)
    {
        if (!TryGetPoolForRelease(
                bulletEntity,
                ProjectilePoolKind.Bullet,
                ref poolMemberLookup,
                ref poolLookup,
                ref freeLookup,
                out ProjectilePoolMember member,
                out Entity poolEntity,
                out ProjectilePool pool))
        {
            ecb.DestroyEntity(bulletEntity);
            return;
        }

        if (member.inPool != 0)
        {
            pool.doubleReleaseCount++;
            poolLookup[poolEntity] = pool;
            ecb.SetComponentEnabled<BulletActive>(bulletEntity, false);
            currentTransform.Position = pool.inactivePosition;
            SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
                ref ecb,
                bulletEntity,
                currentTransform,
                true,
                true);
            return;
        }

        ecb.SetComponentEnabled<BulletActive>(bulletEntity, false);
        currentTransform.Position = pool.inactivePosition;
        SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
            ref ecb,
            bulletEntity,
            currentTransform,
            true,
            true);

        member.inPool = 1;
        poolMemberLookup[bulletEntity] = member;

        pool.activeCount = math.max(0, pool.activeCount - 1);
        poolLookup[poolEntity] = pool;

        ecb.AppendToBuffer(poolEntity, new ProjectilePoolFreeElement
        {
            entity = bulletEntity,
        });
    }

    public static void ReleaseRocketOrDestroy(
        ref EntityCommandBuffer ecb,
        Entity rocketEntity,
        ref ComponentLookup<ProjectilePoolMember> poolMemberLookup,
        ref ComponentLookup<ProjectilePool> poolLookup,
        ref BufferLookup<ProjectilePoolFreeElement> freeLookup,
        ref ComponentLookup<Velocity> velocityLookup,
        ref ComponentLookup<LastKnownTarget> lastKnownTargetLookup,
        ref ComponentLookup<RocketTrailResetRequest> trailResetLookup)
    {
        if (!TryGetPoolForRelease(
                rocketEntity,
                ProjectilePoolKind.Rocket,
                ref poolMemberLookup,
                ref poolLookup,
                ref freeLookup,
                out ProjectilePoolMember member,
                out Entity poolEntity,
                out ProjectilePool pool))
        {
            ecb.DestroyEntity(rocketEntity);
            return;
        }

        if (member.inPool != 0)
        {
            pool.doubleReleaseCount++;
            poolLookup[poolEntity] = pool;
            ResetRocketRuntimeState(ref ecb, rocketEntity, pool.inactivePosition, ref velocityLookup, ref lastKnownTargetLookup, ref trailResetLookup);
            return;
        }

        ResetRocketRuntimeState(ref ecb, rocketEntity, pool.inactivePosition, ref velocityLookup, ref lastKnownTargetLookup, ref trailResetLookup);

        member.inPool = 1;
        poolMemberLookup[rocketEntity] = member;

        pool.activeCount = math.max(0, pool.activeCount - 1);
        poolLookup[poolEntity] = pool;

        ecb.AppendToBuffer(poolEntity, new ProjectilePoolFreeElement
        {
            entity = rocketEntity,
        });
    }

    private static void ResetAfterGrow(
        EntityManager entityManager,
        Entity projectileEntity,
        ProjectilePoolKind kind,
        LocalTransform inactiveTransform)
    {
        if (kind == ProjectilePoolKind.Bullet)
        {
            entityManager.SetComponentEnabled<BulletActive>(projectileEntity, false);
            SpawnTransformUtility.SetLocalTransformAndLocalToWorld(entityManager, projectileEntity, inactiveTransform);
            return;
        }

        if (kind == ProjectilePoolKind.Rocket)
        {
            entityManager.SetComponentEnabled<RocketActive>(projectileEntity, false);

            if (entityManager.HasComponent<LastKnownTarget>(projectileEntity))
            {
                entityManager.SetComponentEnabled<LastKnownTarget>(projectileEntity, false);
            }

            if (entityManager.HasComponent<Velocity>(projectileEntity))
            {
                entityManager.SetComponentData(projectileEntity, new Velocity { velocity = float2.zero });
            }

            SpawnTransformUtility.SetLocalTransformAndLocalToWorld(entityManager, projectileEntity, inactiveTransform);

            if (entityManager.HasComponent<RocketTrailResetRequest>(projectileEntity))
            {
                entityManager.SetComponentData(projectileEntity, new RocketTrailResetRequest
                {
                    pending = 1,
                    showAfterClear = 0,
                    emitAfterClear = 0,
                    activateAfterClear = 0,
                    moveAfterClear = 0,
                    movePosition = float3.zero,
                    delayFramesBeforeEmit = 0,
                });
                entityManager.SetComponentEnabled<RocketTrailResetRequest>(projectileEntity, true);
            }
        }
    }

    private static bool TryGetPoolForRelease(
        Entity projectileEntity,
        ProjectilePoolKind expectedKind,
        ref ComponentLookup<ProjectilePoolMember> poolMemberLookup,
        ref ComponentLookup<ProjectilePool> poolLookup,
        ref BufferLookup<ProjectilePoolFreeElement> freeLookup,
        out ProjectilePoolMember member,
        out Entity poolEntity,
        out ProjectilePool pool)
    {
        member = default;
        poolEntity = Entity.Null;
        pool = default;

        if (!poolMemberLookup.HasComponent(projectileEntity))
        {
            return false;
        }

        member = poolMemberLookup[projectileEntity];
        poolEntity = member.poolEntity;
        if (poolEntity == Entity.Null ||
            member.kind != expectedKind ||
            !poolLookup.HasComponent(poolEntity) ||
            !freeLookup.HasBuffer(poolEntity))
        {
            return false;
        }

        pool = poolLookup[poolEntity];
        return pool.kind == expectedKind;
    }

    private static void ResetRocketRuntimeState(
        ref EntityCommandBuffer ecb,
        Entity rocketEntity,
        float3 inactivePosition,
        ref ComponentLookup<Velocity> velocityLookup,
        ref ComponentLookup<LastKnownTarget> lastKnownTargetLookup,
        ref ComponentLookup<RocketTrailResetRequest> trailResetLookup)
    {
        ecb.SetComponentEnabled<RocketActive>(rocketEntity, false);

        if (velocityLookup.HasComponent(rocketEntity))
        {
            ecb.SetComponent(rocketEntity, new Velocity { velocity = float2.zero });
        }

        if (lastKnownTargetLookup.HasComponent(rocketEntity))
        {
            ecb.SetComponent(rocketEntity, new LastKnownTarget
            {
                target = Entity.Null,
                lastKnownPosition = float2.zero,
                searchTimer = 0f,
            });
            ecb.SetComponentEnabled<LastKnownTarget>(rocketEntity, false);
        }

        LocalTransform inactiveTransform = LocalTransform.FromPosition(inactivePosition);
        SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
            ref ecb,
            rocketEntity,
            inactiveTransform,
            true,
            true);

        if (trailResetLookup.HasComponent(rocketEntity))
        {
            ecb.SetComponent(rocketEntity, new RocketTrailResetRequest
            {
                pending = 1,
                showAfterClear = 0,
                emitAfterClear = 0,
                activateAfterClear = 0,
                moveAfterClear = 0,
                movePosition = float3.zero,
                delayFramesBeforeEmit = 0,
            });
            ecb.SetComponentEnabled<RocketTrailResetRequest>(rocketEntity, true);
        }
    }

    private static bool IsValidFreeProjectile(
        EntityManager entityManager,
        Entity projectileEntity,
        Entity expectedPoolEntity,
        ProjectilePoolKind expectedKind)
    {
        if (projectileEntity == Entity.Null ||
            !entityManager.Exists(projectileEntity) ||
            !entityManager.HasComponent<ProjectilePoolMember>(projectileEntity))
        {
            return false;
        }

        ProjectilePoolMember member = entityManager.GetComponentData<ProjectilePoolMember>(projectileEntity);
        if (member.poolEntity != expectedPoolEntity || member.kind != expectedKind || member.inPool == 0)
        {
            return false;
        }

        if (expectedKind == ProjectilePoolKind.Bullet)
        {
            return entityManager.HasComponent<BulletActive>(projectileEntity) &&
                   !entityManager.IsComponentEnabled<BulletActive>(projectileEntity);
        }

        if (expectedKind == ProjectilePoolKind.Rocket)
        {
            return entityManager.HasComponent<RocketActive>(projectileEntity) &&
                   !entityManager.IsComponentEnabled<RocketActive>(projectileEntity);
        }

        return false;
    }

    private static void MarkAcquired(
        ref EntityCommandBuffer ecb,
        EntityManager entityManager,
        Entity projectileEntity,
        Entity poolEntity,
        Entity prefabEntity,
        ref ProjectilePool pool)
    {
        entityManager.SetComponentData(projectileEntity, new ProjectilePoolMember
        {
            poolEntity = poolEntity,
            prefabEntity = prefabEntity,
            kind = pool.kind,
            inPool = 0,
        });

        pool.activeCount++;
        pool.peakActiveCount = math.max(pool.peakActiveCount, pool.activeCount);

        if (pool.kind == ProjectilePoolKind.Bullet)
        {
            ecb.SetComponentEnabled<BulletActive>(projectileEntity, true);
            return;
        }

        if (pool.kind == ProjectilePoolKind.Rocket)
        {
            bool hasTrailReset = entityManager.HasComponent<RocketTrailResetRequest>(projectileEntity);
            ecb.SetComponentEnabled<RocketActive>(projectileEntity, !hasTrailReset);
        }
    }
}
