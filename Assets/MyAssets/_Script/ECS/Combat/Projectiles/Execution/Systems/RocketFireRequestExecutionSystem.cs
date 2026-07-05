using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedWeaponFireRequestBuildSystem))]
[UpdateBefore(typeof(RocketMoverSystem))]
public partial struct RocketFireRequestExecutionSystem : ISystem
{
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private EntityQuery rocketQueueQuery;

    private struct PoolFrameDemand
    {
        public Entity poolEntity;
        public Entity prefabEntity;
        public int requestCount;
    }

    public void OnCreate(ref SystemState state)
    {
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(true);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(true);

        rocketQueueQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<WeaponFireRequestQueueSingleton>(),
            ComponentType.ReadWrite<RocketWeaponFireRequestElement>());

        state.RequireForUpdate<WeaponProfileDatabase>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<WeaponProfileDatabase>(out WeaponProfileDatabase database))
        {
            return;
        }

        buffStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);

        ref WeaponProfileDatabaseBlob root = ref database.Value.Value;
        EntityManager entityManager = state.EntityManager;

        bool hasQueuedRequests = rocketQueueQuery.TryGetSingletonBuffer<RocketWeaponFireRequestElement>(out DynamicBuffer<RocketWeaponFireRequestElement> queuedRequests) && queuedRequests.Length > 0;
        if (!hasQueuedRequests)
        {
            return;
        }

        bool hasPoolRegistry = SystemAPI.TryGetSingletonEntity<ProjectilePoolRegistry>(out Entity registryEntity);
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

        if (hasPoolRegistry)
        {
            PreGrowPoolsForFrame(entityManager, registryEntity, requests.AsArray(), ref root);
        }

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        for (int i = 0; i < requests.Length; i++)
        {
            WeaponFireRequest request = requests[i];
            if ((WeaponRequestKind)request.requestKind != WeaponRequestKind.Rocket)
            {
                continue;
            }

            int profileIndex = request.profileIndex;
            if ((uint)profileIndex >= (uint)root.Profiles.Length)
            {
                continue;
            }

            Entity ammoEntity = request.ammoEntity;
            if (!IsRocketAmmoRuntimeReady(entityManager, ammoEntity))
            {
                continue;
            }

            WeaponProfileBlob profile = root.Profiles[profileIndex];
            Entity projectileEntity = CreateRocketEntity(ref ecb, entityManager, ammoEntity, hasPoolRegistry, registryEntity, out bool fromPool);
            if (projectileEntity == Entity.Null)
            {
                continue;
            }

            float2 spawnDirection = math.normalizesafe(request.direction, new float2(0f, 1f));
            bool useLaunchScatter = profile.rocketLaunchScatterAngle > 0.001f &&
                (profile.rocketLaunchScatterDuration > 0.001f || profile.rocketLaunchScatterDistance > 0.001f);

            float2 launchDirection = useLaunchScatter
                ? ApplyLaunchScatter(spawnDirection, profile.rocketLaunchScatterAngle, request.randomSeed)
                : spawnDirection;

            LocalTransform spawnTransform = LocalTransform.FromPositionRotation(request.spawnPosition, RotationFromDirection(launchDirection));
            bool hasLocalToWorld = fromPool
                ? entityManager.HasComponent<LocalToWorld>(projectileEntity)
                : entityManager.HasComponent<LocalToWorld>(ammoEntity);

            SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
                ref ecb,
                projectileEntity,
                spawnTransform,
                true,
                hasLocalToWorld);

            bool projectileHasTrailReset = fromPool
                ? entityManager.HasComponent<RocketTrailResetRequest>(projectileEntity)
                : entityManager.HasComponent<RocketTrailResetRequest>(ammoEntity);

            if (projectileHasTrailReset)
            {
                ecb.SetComponent(projectileEntity, new RocketTrailResetRequest
                {
                    pending = 1,
                    showAfterClear = 1,
                    emitAfterClear = 1,
                    activateAfterClear = 1,
                    moveAfterClear = 0,
                    movePosition = float3.zero,
                    delayFramesBeforeEmit = 1,
                });
                ecb.SetComponentEnabled<RocketTrailResetRequest>(projectileEntity, true);
            }

            float baseLaunchSpeed = math.max(1f, profile.projectileSpeed * 0.35f);
            float inheritedLaunchSpeed = WeaponAimUtility.ResolvePositiveInheritedSpeed(request.ownerVelocity, launchDirection);
            float launchSpeed = baseLaunchSpeed + inheritedLaunchSpeed;
            float2 launchVelocity = launchDirection * launchSpeed;

            Rocket rocket = entityManager.GetComponentData<Rocket>(ammoEntity);
            rocket.currentSpeed = launchSpeed;
            rocket.maxSpeed = profile.projectileSpeed;
            rocket.acceleration = profile.rocketAcceleration;
            rocket.timer = profile.lifetime;
            rocket.ownerFaction = request.ownerFaction;
            rocket.useFogOfWar = request.ownerUsesFogOfWar;
            rocket.phase = (byte)RocketFlightPhase.Locked;
            rocket.lkpFreeFlightTimer = 0f;

            // torpedo mode: keeps launch direction, no homing
            if (!profile.turnAfterLaunch)
            {
                rocket.rotationSpeed = 0f;
            }

            ecb.SetComponent(projectileEntity, rocket);

            bool projectileHasScatter = fromPool
                ? entityManager.HasComponent<RocketLaunchScatter>(projectileEntity)
                : entityManager.HasComponent<RocketLaunchScatter>(ammoEntity);

            if (useLaunchScatter)
            {
                RocketLaunchScatter scatter = new RocketLaunchScatter
                {
                    timer = profile.rocketLaunchScatterDuration > 0f ? profile.rocketLaunchScatterDuration : float.MaxValue,
                    distanceRemaining = profile.rocketLaunchScatterDistance > 0f ? profile.rocketLaunchScatterDistance : float.MaxValue,
                    scatterDirection = launchDirection,
                };

                if (projectileHasScatter)
                {
                    ecb.SetComponent(projectileEntity, scatter);
                }
                else
                {
                    ecb.AddComponent(projectileEntity, scatter);
                }
            }
            else if (projectileHasScatter)
            {
                ecb.RemoveComponent<RocketLaunchScatter>(projectileEntity);
            }

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

            ecb.SetComponent(projectileEntity, new Target
            {
                targetEntity = request.targetEntity,
                targetFaction = request.targetFaction,
                targetPosition = request.targetPosition,
            });

            ecb.SetComponent(projectileEntity, new Velocity
            {
                velocity = launchVelocity,
            });

            if (entityManager.HasComponent<LastKnownTarget>(ammoEntity))
            {
                ecb.SetComponent(projectileEntity, new LastKnownTarget
                {
                    target = Entity.Null,
                    lastKnownPosition = float2.zero,
                    searchTimer = 0f,
                });
                ecb.SetComponentEnabled<LastKnownTarget>(projectileEntity, false);
            }

            if (entityManager.HasComponent<FindTarget>(ammoEntity))
            {
                FindTarget findTarget = entityManager.GetComponentData<FindTarget>(ammoEntity);
                findTarget.rangeWorkAfterLockTarget = false;
                ecb.SetComponent(projectileEntity, findTarget);
            }

        }

        ecb.Playback(entityManager);
        requests.Dispose();
    }


    private static void PreGrowPoolsForFrame(
        EntityManager entityManager,
        Entity registryEntity,
        NativeArray<WeaponFireRequest> requests,
        ref WeaponProfileDatabaseBlob root)
    {
        NativeList<PoolFrameDemand> demands = new NativeList<PoolFrameDemand>(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            WeaponFireRequest request = requests[i];
            if ((WeaponRequestKind)request.requestKind != WeaponRequestKind.Rocket)
            {
                continue;
            }

            if ((uint)request.profileIndex >= (uint)root.Profiles.Length || !IsRocketAmmoRuntimeReady(entityManager, request.ammoEntity))
            {
                continue;
            }

            if (!TryFindPoolEntity(entityManager, registryEntity, request.ammoEntity, out Entity poolEntity) || !IsPoolRuntimeReady(entityManager, poolEntity))
            {
                continue;
            }

            AddDemand(ref demands, poolEntity, request.ammoEntity);
        }

        for (int i = 0; i < demands.Length; i++)
        {
            PoolFrameDemand demand = demands[i];
            ProjectilePool pool = entityManager.GetComponentData<ProjectilePool>(demand.poolEntity);
            int validFreeCount = ProjectilePoolUtility.CleanInvalidFreeEntries(entityManager, demand.poolEntity, ref pool);
            int deficit = demand.requestCount - validFreeCount;
            if (deficit <= 0 || !pool.allowRuntimeExpand || pool.totalSpawned >= pool.hardCap)
            {
                entityManager.SetComponentData(demand.poolEntity, pool);
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

        demands.Dispose();
    }

    private static void AddDemand(ref NativeList<PoolFrameDemand> demands, Entity poolEntity, Entity prefabEntity)
    {
        for (int i = 0; i < demands.Length; i++)
        {
            PoolFrameDemand demand = demands[i];
            if (demand.poolEntity == poolEntity)
            {
                demand.requestCount++;
                demands[i] = demand;
                return;
            }
        }

        demands.Add(new PoolFrameDemand
        {
            poolEntity = poolEntity,
            prefabEntity = prefabEntity,
            requestCount = 1,
        });
    }

    private static bool IsRocketAmmoRuntimeReady(EntityManager entityManager, Entity ammoEntity)
    {
        return ammoEntity != Entity.Null &&
               entityManager.Exists(ammoEntity) &&
               entityManager.HasComponent<Rocket>(ammoEntity) &&
               entityManager.HasComponent<RocketActive>(ammoEntity) &&
               entityManager.HasComponent<ProjectilePoolMember>(ammoEntity) &&
               entityManager.HasComponent<WeaponPayloadRuntime>(ammoEntity) &&
               entityManager.HasComponent<Target>(ammoEntity) &&
               entityManager.HasComponent<Velocity>(ammoEntity) &&
               entityManager.HasComponent<LastKnownTarget>(ammoEntity) &&
               entityManager.HasComponent<LocalTransform>(ammoEntity);
    }

    private static Entity CreateRocketEntity(
        ref EntityCommandBuffer ecb,
        EntityManager entityManager,
        Entity ammoEntity,
        bool hasPoolRegistry,
        Entity registryEntity,
        out bool fromPool)
    {
        fromPool = false;
        if (!hasPoolRegistry || !TryFindPoolEntity(entityManager, registryEntity, ammoEntity, out Entity poolEntity))
        {
            Entity fallbackEntity = ecb.Instantiate(ammoEntity);
            ecb.SetComponentEnabled<RocketActive>(fallbackEntity, true);
            return fallbackEntity;
        }

        if (!IsPoolRuntimeReady(entityManager, poolEntity))
        {
            Entity fallbackEntity = ecb.Instantiate(ammoEntity);
            ecb.SetComponentEnabled<RocketActive>(fallbackEntity, true);
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

    private static bool TryFindPoolEntity(EntityManager entityManager, Entity registryEntity, Entity prefabEntity, out Entity poolEntity)
    {
        DynamicBuffer<ProjectilePoolPrefabLink> poolLinks = entityManager.GetBuffer<ProjectilePoolPrefabLink>(registryEntity);
        for (int i = 0; i < poolLinks.Length; i++)
        {
            ProjectilePoolPrefabLink link = poolLinks[i];
            if (link.kind == ProjectilePoolKind.Rocket &&
                link.prefabEntity == prefabEntity &&
                link.poolEntity != Entity.Null &&
                entityManager.Exists(link.poolEntity))
            {
                poolEntity = link.poolEntity;
                return true;
            }
        }

        poolEntity = Entity.Null;
        return false;
    }

    private static float2 ApplyLaunchScatter(float2 forward, float scatterAngle, uint randomSeed)
    {
        float2 normalizedForward = math.normalizesafe(forward, new float2(0f, 1f));
        Random rng = Random.CreateFromIndex(math.max(1u, randomSeed ^ 0x9E3779B9u));
        float offsetRad = math.radians(rng.NextFloat(-scatterAngle, scatterAngle));
        float sin = math.sin(offsetRad);
        float cos = math.cos(offsetRad);

        float2 scatterDirection = new float2(
            normalizedForward.x * cos - normalizedForward.y * sin,
            normalizedForward.x * sin + normalizedForward.y * cos);

        return math.normalizesafe(scatterDirection, normalizedForward);
    }

    private static quaternion RotationFromDirection(float2 direction)
    {
        direction = math.normalizesafe(direction, new float2(0f, 1f));

        // 2D top-down, rocket nose is local +Y
        float angle = math.atan2(direction.y, direction.x) - math.radians(90f);

        return quaternion.RotateZ(angle);
    }
}
