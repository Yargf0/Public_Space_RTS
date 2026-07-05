using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedWeaponFireRequestBuildSystem))]
[UpdateBefore(typeof(BulletMoverSystem))]
public partial struct BallisticFireRequestExecutionSystem : ISystem
{
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private EntityQuery ballisticQueueQuery;

    private struct PoolFrameDemand
    {
        public Entity poolEntity;
        public Entity prefabEntity;
        public int requestCount;
    }

    private struct AmmoFrameData
    {
        public byte ready;
        public LocalTransform prefabTransform;
    }

    public void OnCreate(ref SystemState state)
    {
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(true);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(true);

        ballisticQueueQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<WeaponFireRequestQueueSingleton>(),
            ComponentType.ReadWrite<BallisticWeaponFireRequestElement>());

        state.RequireForUpdate<WeaponProfileDatabase>();
    }

    public void OnUpdate(ref SystemState state)
    {
        bool hasQueuedRequests = ballisticQueueQuery.TryGetSingletonBuffer<BallisticWeaponFireRequestElement>(out DynamicBuffer<BallisticWeaponFireRequestElement> queuedRequests) && queuedRequests.Length > 0;
        if (!hasQueuedRequests)
        {
            return;
        }

        if (!SystemAPI.TryGetSingleton<WeaponProfileDatabase>(out WeaponProfileDatabase database))
        {
            return;
        }

        EntityManager entityManager = state.EntityManager;
        buffStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);

        ref WeaponProfileDatabaseBlob root = ref database.Value.Value;

        NativeList<WeaponFireRequest> requests = new NativeList<WeaponFireRequest>(queuedRequests.Length, Allocator.Temp);
        for (int i = 0; i < queuedRequests.Length; i++)
        {
            requests.Add(queuedRequests[i].Value);
        }
        queuedRequests.Clear();

        if (requests.Length == 0)
        {
            requests.Dispose();
            return;
        }

        bool hasPoolRegistry = SystemAPI.TryGetSingletonEntity<ProjectilePoolRegistry>(out Entity registryEntity);
        NativeParallelHashMap<Entity, Entity> poolByPrefab = default;
        bool hasPoolLookup = hasPoolRegistry && TryBuildReadyPoolLookup(entityManager, registryEntity, out poolByPrefab);

        NativeParallelHashMap<Entity, AmmoFrameData> ammoDataCache =
            new NativeParallelHashMap<Entity, AmmoFrameData>(math.max(1, requests.Length), Allocator.Temp);

        if (hasPoolLookup)
        {
            PreGrowPoolsForFrame(entityManager, requests.AsArray(), ref root, poolByPrefab, ref ammoDataCache);
        }

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        for (int i = 0; i < requests.Length; i++)
        {
            WeaponFireRequest request = requests[i];
            // Just in case, this queue should have only ballistic requests.
            if ((WeaponRequestKind)request.requestKind != WeaponRequestKind.Ballistic)
            {
                continue;
            }

            int profileIndex = request.profileIndex;
            if ((uint)profileIndex >= (uint)root.Profiles.Length)
            {
                continue;
            }

            Entity ammoEntity = request.ammoEntity;
            if (!TryGetAmmoFrameData(entityManager, ammoEntity, ref ammoDataCache, out AmmoFrameData ammoData))
            {
                continue;
            }

            WeaponProfileBlob profile = root.Profiles[profileIndex];
            float2 baseDirection = math.normalizesafe(request.direction, new float2(0f, 1f));
            Entity projectileEntity = CreateProjectileEntity(ref ecb, entityManager, ammoEntity, hasPoolLookup, poolByPrefab, out bool fromPool);

            if (projectileEntity == Entity.Null)
            {
                continue;
            }

            LocalTransform spawnTransform = CreateSpawnTransform(ammoData.prefabTransform, request.spawnPosition, baseDirection);
            bool hasLocalToWorld = fromPool
                ? entityManager.HasComponent<LocalToWorld>(projectileEntity)
                : entityManager.HasComponent<LocalToWorld>(ammoEntity);

            SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
                ref ecb,
                projectileEntity,
                spawnTransform,
                true,
                hasLocalToWorld);

            float inheritedSpeed = WeaponAimUtility.ResolvePositiveInheritedSpeed(request.ownerVelocity, baseDirection);
            float shotSpeed = profile.projectileSpeed + inheritedSpeed;

            ecb.SetComponent(projectileEntity, new Bullet
            {
                movementVector = baseDirection,
                timer = profile.lifetime,
                distance = profile.attackDistance,
                speed = shotSpeed,
            });

            ecb.SetComponent(projectileEntity, new Target
            {
                targetEntity = Entity.Null,
                targetFaction = request.targetFaction,
                targetPosition = float2.zero,
            });

            ecb.SetComponent(projectileEntity, new WeaponPayloadRuntime
            {
                payloadKind = profile.payloadKind,
                damageAmount = profile.damageAmount * EmbeddedActionStatusUtility.GetOutgoingEffectMultiplier(request.ownerEntity, ref buffStatusLookup, ref debuffStatusLookup),
                splashRadius = profile.splashRadius,
                ownerFaction = request.ownerFaction,
                statusEffectKind = profile.statusEffectKind,
                statusDuration = profile.statusDuration,
                moveSpeedMultiplier = profile.moveSpeedMultiplier,
                accelerationMultiplier = profile.accelerationMultiplier,
                disableWeapons = profile.disableWeapons,
            });

            CombatVfxRequestUtility.EnqueueProjectileTracer(ref ecb, in request, in profile);

        }

        ecb.Playback(entityManager);

        ammoDataCache.Dispose();

        if (hasPoolLookup)
        {
            poolByPrefab.Dispose();
        }

        requests.Dispose();
    }


    private static void PreGrowPoolsForFrame(
        EntityManager entityManager,
        NativeArray<WeaponFireRequest> requests,
        ref WeaponProfileDatabaseBlob root,
        NativeParallelHashMap<Entity, Entity> poolByPrefab,
        ref NativeParallelHashMap<Entity, AmmoFrameData> ammoDataCache)
    {
        NativeList<PoolFrameDemand> demands = new NativeList<PoolFrameDemand>(Allocator.Temp);
        NativeParallelHashMap<Entity, int> demandIndexByPool =
            new NativeParallelHashMap<Entity, int>(math.max(1, requests.Length), Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            WeaponFireRequest request = requests[i];
            if ((WeaponRequestKind)request.requestKind != WeaponRequestKind.Ballistic)
            {
                continue;
            }

            if ((uint)request.profileIndex >= (uint)root.Profiles.Length)
            {
                continue;
            }

            if (!TryGetAmmoFrameData(entityManager, request.ammoEntity, ref ammoDataCache, out _))
            {
                continue;
            }

            if (!poolByPrefab.TryGetValue(request.ammoEntity, out Entity poolEntity))
            {
                continue;
            }

            AddDemand(ref demands, ref demandIndexByPool, poolEntity, request.ammoEntity);
        }

        for (int i = 0; i < demands.Length; i++)
        {
            PoolFrameDemand demand = demands[i];
            // poolEntity comes from poolByPrefab, so the pool is ready.
            ProjectilePool pool = entityManager.GetComponentData<ProjectilePool>(demand.poolEntity);
            int freeCount = entityManager.GetBuffer<ProjectilePoolFreeElement>(demand.poolEntity).Length;
            int deficit = demand.requestCount - freeCount;
            if (deficit <= 0 || !pool.allowRuntimeExpand || pool.totalSpawned >= pool.hardCap)
            {
                continue;
            }

            int growChunk = math.max(1, pool.growChunk);
            int requestedGrow = ((deficit + growChunk - 1) / growChunk) * growChunk;
            int spawnCount = math.min(requestedGrow, pool.hardCap - pool.totalSpawned);
            if (spawnCount <= 0)
            {
                continue;
            }

            ProjectilePoolUtility.GrowFreeProjectiles(entityManager, demand.prefabEntity, demand.poolEntity, ref pool, spawnCount);
            entityManager.SetComponentData(demand.poolEntity, pool);
        }

        demandIndexByPool.Dispose();
        demands.Dispose();
    }

    private static void AddDemand(
        ref NativeList<PoolFrameDemand> demands,
        ref NativeParallelHashMap<Entity, int> demandIndexByPool,
        Entity poolEntity,
        Entity prefabEntity)
    {
        if (demandIndexByPool.TryGetValue(poolEntity, out int demandIndex))
        {
            PoolFrameDemand demand = demands[demandIndex];
            demand.requestCount++;
            demands[demandIndex] = demand;
            return;
        }

        demandIndexByPool.TryAdd(poolEntity, demands.Length);
        demands.Add(new PoolFrameDemand
        {
            poolEntity = poolEntity,
            prefabEntity = prefabEntity,
            requestCount = 1,
        });
    }

    private static bool TryGetAmmoFrameData(
        EntityManager entityManager,
        Entity ammoEntity,
        ref NativeParallelHashMap<Entity, AmmoFrameData> ammoDataCache,
        out AmmoFrameData ammoData)
    {
        ammoData = default;

        if (ammoEntity == Entity.Null)
        {
            return false;
        }

        if (ammoDataCache.TryGetValue(ammoEntity, out ammoData))
        {
            return ammoData.ready != 0;
        }

        bool ready = entityManager.Exists(ammoEntity) &&
                     entityManager.HasComponent<Bullet>(ammoEntity) &&
                     entityManager.HasComponent<BulletActive>(ammoEntity) &&
                     entityManager.HasComponent<ProjectilePoolMember>(ammoEntity) &&
                     entityManager.HasComponent<WeaponPayloadRuntime>(ammoEntity) &&
                     entityManager.HasComponent<Target>(ammoEntity) &&
                     entityManager.HasComponent<LocalTransform>(ammoEntity);

        ammoData = new AmmoFrameData
        {
            ready = (byte)(ready ? 1 : 0),
            prefabTransform = ready ? entityManager.GetComponentData<LocalTransform>(ammoEntity) : default,
        };

        ammoDataCache.TryAdd(ammoEntity, ammoData);
        return ready;
    }

    private static LocalTransform CreateSpawnTransform(LocalTransform prefabTransform, float3 spawnPosition, float2 direction)
    {
        LocalTransform spawnTransform = prefabTransform;
        float2 normalizedDirection = math.normalizesafe(direction, new float2(0f, 1f));
        float angle = math.atan2(normalizedDirection.y, normalizedDirection.x) - math.PI * 0.5f;

        spawnTransform.Position = spawnPosition;
        spawnTransform.Rotation = quaternion.RotateZ(angle);

        if (spawnTransform.Scale <= 0f)
        {
            spawnTransform.Scale = 1f;
        }

        return spawnTransform;
    }

    private static Entity CreateProjectileEntity(
        ref EntityCommandBuffer ecb,
        EntityManager entityManager,
        Entity ammoEntity,
        bool hasPoolLookup,
        NativeParallelHashMap<Entity, Entity> poolByPrefab,
        out bool fromPool)
    {
        fromPool = false;
        if (!hasPoolLookup || !poolByPrefab.TryGetValue(ammoEntity, out Entity poolEntity))
        {
            Entity fallbackEntity = ecb.Instantiate(ammoEntity);
            ecb.SetComponentEnabled<BulletActive>(fallbackEntity, true);
            return fallbackEntity;
        }

        ProjectilePool pool = entityManager.GetComponentData<ProjectilePool>(poolEntity);
        Entity projectileEntity = ProjectilePoolUtility.AcquireOrDrop(ref ecb, entityManager, ammoEntity, poolEntity, ref pool);
        entityManager.SetComponentData(poolEntity, pool);
        fromPool = projectileEntity != Entity.Null;
        return projectileEntity;
    }

    private static bool IsPoolRuntimeReady(EntityManager entityManager, Entity poolEntity)
    {
        return poolEntity != Entity.Null &&
               entityManager.Exists(poolEntity) &&
               entityManager.HasComponent<ProjectilePool>(poolEntity) &&
               entityManager.HasBuffer<ProjectilePoolFreeElement>(poolEntity) &&
               entityManager.HasBuffer<ProjectilePoolAllElement>(poolEntity);
    }

    private static bool TryBuildReadyPoolLookup(
        EntityManager entityManager,
        Entity registryEntity,
        out NativeParallelHashMap<Entity, Entity> poolByPrefab)
    {
        poolByPrefab = default;
        if (registryEntity == Entity.Null ||
            !entityManager.Exists(registryEntity) ||
            !entityManager.HasBuffer<ProjectilePoolPrefabLink>(registryEntity))
        {
            return false;
        }

        DynamicBuffer<ProjectilePoolPrefabLink> poolLinks = entityManager.GetBuffer<ProjectilePoolPrefabLink>(registryEntity);
        poolByPrefab = new NativeParallelHashMap<Entity, Entity>(math.max(1, poolLinks.Length), Allocator.Temp);

        int added = 0;
        for (int i = 0; i < poolLinks.Length; i++)
        {
            ProjectilePoolPrefabLink link = poolLinks[i];
            if (link.kind != ProjectilePoolKind.Bullet || link.prefabEntity == Entity.Null || !IsPoolRuntimeReady(entityManager, link.poolEntity))
            {
                continue;
            }

            if (poolByPrefab.TryAdd(link.prefabEntity, link.poolEntity))
            {
                added++;
            }
        }

        if (added == 0)
        {
            poolByPrefab.Dispose();
            poolByPrefab = default;
            return false;
        }

        return true;
    }
}
