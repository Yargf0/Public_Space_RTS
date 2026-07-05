using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct ProjectilePoolBootstrapSystem : ISystem
{
    private const int DefaultBulletPrewarmPerPrefab = 512;
    private const int DefaultBulletGrowChunk = 64;
    private const int DefaultBulletHardCapPerPrefab = 4096;
    private const int DefaultRocketPrewarmPerPrefab = 128;
    private const int DefaultRocketGrowChunk = 32;
    private const int DefaultRocketHardCapPerPrefab = 1024;
    private const float DefaultRuntimeScanInterval = 1f;
    private const int DefaultMaxPoolsCreatedPerFrame = 2;
    private const int DefaultMaxPrewarmPerFrame = 256;

    private bool initialBootstrapDone;
    private double nextRuntimeScanTime;
    private EntityQuery pendingPrewarmQuery;

    private struct PoolBuildSettings
    {
        public ProjectilePoolKind kind;
        public int prewarmPerPrefab;
        public int growChunk;
        public int hardCapPerPrefab;
        public bool allowRuntimeExpand;
        public bool logRuntimeExpand;
        public bool keepBootstrapEnabledForRuntimePrefabs;
        public float runtimeScanInterval;
        public int maxPoolsCreatedPerFrame;
        public int maxPrewarmPerFrame;
        public float3 inactivePosition;
    }

    public void OnCreate(ref SystemState state)
    {
        pendingPrewarmQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<ProjectilePool>(),
            ComponentType.ReadOnly<ProjectilePoolPendingPrewarm>());
    }

    public void OnUpdate(ref SystemState state)
    {
        double now = SystemAPI.Time.ElapsedTime;
        PoolBuildSettings bulletSettings = CreateDefaultBulletSettings();
        if (SystemAPI.TryGetSingleton(out BulletPoolSettings authoredBulletSettings))
        {
            ApplyBulletSettings(ref bulletSettings, authoredBulletSettings);
        }

        PoolBuildSettings rocketSettings = CreateDefaultRocketSettings();
        if (SystemAPI.TryGetSingleton(out RocketPoolSettings authoredRocketSettings))
        {
            ApplyRocketSettings(ref rocketSettings, authoredRocketSettings);
        }

        NormalizeSettings(ref bulletSettings);
        NormalizeSettings(ref rocketSettings);

        EntityManager entityManager = state.EntityManager;
        bool shouldScanForPrefabs = !initialBootstrapDone;
        bool keepRuntimeScan = bulletSettings.keepBootstrapEnabledForRuntimePrefabs || rocketSettings.keepBootstrapEnabledForRuntimePrefabs;
        float scanInterval = math.min(bulletSettings.runtimeScanInterval, rocketSettings.runtimeScanInterval);

        if (initialBootstrapDone && keepRuntimeScan && now >= nextRuntimeScanTime)
        {
            shouldScanForPrefabs = true;
        }

        if (shouldScanForPrefabs)
        {
            bool hasMorePoolsToCreate = DiscoverAndCreatePoolsForKnownProjectilePrefabs(entityManager, bulletSettings, rocketSettings);
            initialBootstrapDone = !hasMorePoolsToCreate;
            nextRuntimeScanTime = now + (hasMorePoolsToCreate ? 0.01f : scanInterval);
        }

        ProcessPendingPrewarm(entityManager, bulletSettings, rocketSettings);

        bool hasPendingPrewarm = !pendingPrewarmQuery.IsEmptyIgnoreFilter;
        if (initialBootstrapDone && !keepRuntimeScan && !hasPendingPrewarm)
        {
            state.Enabled = false;
        }
    }

    private static void ApplyBulletSettings(ref PoolBuildSettings settings, BulletPoolSettings authoredSettings)
    {
        settings.prewarmPerPrefab = authoredSettings.prewarmPerPrefab;
        settings.growChunk = authoredSettings.growChunk;
        settings.hardCapPerPrefab = authoredSettings.hardCapPerPrefab;
        settings.allowRuntimeExpand = authoredSettings.allowRuntimeExpand;
        settings.logRuntimeExpand = authoredSettings.logRuntimeExpand;
        settings.keepBootstrapEnabledForRuntimePrefabs = authoredSettings.keepBootstrapEnabledForRuntimePrefabs;
        settings.runtimeScanInterval = authoredSettings.runtimeScanInterval;
        settings.maxPoolsCreatedPerFrame = authoredSettings.maxPoolsCreatedPerFrame;
        settings.maxPrewarmPerFrame = authoredSettings.maxPrewarmPerFrame;
        settings.inactivePosition = authoredSettings.inactivePosition;
    }

    private static void ApplyRocketSettings(ref PoolBuildSettings settings, RocketPoolSettings authoredSettings)
    {
        settings.prewarmPerPrefab = authoredSettings.prewarmPerPrefab;
        settings.growChunk = authoredSettings.growChunk;
        settings.hardCapPerPrefab = authoredSettings.hardCapPerPrefab;
        settings.allowRuntimeExpand = authoredSettings.allowRuntimeExpand;
        settings.logRuntimeExpand = authoredSettings.logRuntimeExpand;
        settings.keepBootstrapEnabledForRuntimePrefabs = authoredSettings.keepBootstrapEnabledForRuntimePrefabs;
        settings.runtimeScanInterval = authoredSettings.runtimeScanInterval;
        settings.maxPoolsCreatedPerFrame = authoredSettings.maxPoolsCreatedPerFrame;
        settings.maxPrewarmPerFrame = authoredSettings.maxPrewarmPerFrame;
        settings.inactivePosition = authoredSettings.inactivePosition;
    }

    private static bool DiscoverAndCreatePoolsForKnownProjectilePrefabs(
        EntityManager entityManager,
        PoolBuildSettings bulletSettings,
        PoolBuildSettings rocketSettings)
    {
        Entity registryEntity = GetOrCreateRegistry(entityManager);
        DynamicBuffer<ProjectilePoolPrefabLink> registry = entityManager.GetBuffer<ProjectilePoolPrefabLink>(registryEntity);

        NativeHashSet<Entity> knownPoolPrefabs = new NativeHashSet<Entity>(math.max(16, registry.Length * 2 + 1), Allocator.Temp);
        for (int i = 0; i < registry.Length; i++)
        {
            ProjectilePoolPrefabLink link = registry[i];
            if (link.prefabEntity != Entity.Null &&
                link.poolEntity != Entity.Null &&
                entityManager.Exists(link.poolEntity))
            {
                knownPoolPrefabs.Add(link.prefabEntity);
            }
        }

        NativeList<Entity> bulletPrefabsToCreate = default;
        NativeList<Entity> rocketPrefabsToCreate = default;

        using (EntityQuery embeddedQuery = entityManager.CreateEntityQuery(
                   ComponentType.ReadOnly<EmbeddedWeaponHost>(),
                   ComponentType.ReadOnly<EmbeddedWeaponSlot>()))
        using (NativeArray<Entity> embeddedHosts = embeddedQuery.ToEntityArray(Allocator.Temp))
        {
            for (int hostIndex = 0; hostIndex < embeddedHosts.Length; hostIndex++)
            {
                Entity hostEntity = embeddedHosts[hostIndex];
                if (!entityManager.Exists(hostEntity) || !entityManager.HasBuffer<EmbeddedWeaponSlot>(hostEntity))
                {
                    continue;
                }

                DynamicBuffer<EmbeddedWeaponSlot> slots = entityManager.GetBuffer<EmbeddedWeaponSlot>(hostEntity, true);
                for (int i = 0; i < slots.Length; i++)
                {
                    EmbeddedWeaponSlot slot = slots[i];
                    if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
                    {
                        continue;
                    }

                    TryQueueProjectilePoolPrefab(entityManager, slot.ammoEntity, ProjectilePoolKind.Bullet, ref knownPoolPrefabs, ref bulletPrefabsToCreate);
                    TryQueueProjectilePoolPrefab(entityManager, slot.ammoEntity, ProjectilePoolKind.Rocket, ref knownPoolPrefabs, ref rocketPrefabsToCreate);
                }
            }
        }

        knownPoolPrefabs.Dispose();

        bool hasMoreBulletPools = CreateQueuedPools(entityManager, registryEntity, bulletSettings, ref bulletPrefabsToCreate);
        bool hasMoreRocketPools = CreateQueuedPools(entityManager, registryEntity, rocketSettings, ref rocketPrefabsToCreate);
        return hasMoreBulletPools || hasMoreRocketPools;
    }

    private static Entity GetOrCreateRegistry(EntityManager entityManager)
    {
        using EntityQuery registryQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectilePoolRegistry>());
        if (registryQuery.IsEmptyIgnoreFilter)
        {
            Entity registryEntity = entityManager.CreateEntity(typeof(ProjectilePoolRegistry));
            entityManager.AddBuffer<ProjectilePoolPrefabLink>(registryEntity);
            return registryEntity;
        }

        return registryQuery.GetSingletonEntity();
    }

    private static bool CreateQueuedPools(
        EntityManager entityManager,
        Entity registryEntity,
        PoolBuildSettings settings,
        ref NativeList<Entity> prefabsToCreate)
    {
        if (!prefabsToCreate.IsCreated)
        {
            return false;
        }

        int createLimit = math.max(1, settings.maxPoolsCreatedPerFrame);
        int createCount = math.min(createLimit, prefabsToCreate.Length);
        bool hasMorePoolsToCreate = prefabsToCreate.Length > createCount;

        for (int i = 0; i < createCount; i++)
        {
            CreatePoolForPrefab(entityManager, registryEntity, prefabsToCreate[i], settings);
        }

        prefabsToCreate.Dispose();
        return hasMorePoolsToCreate;
    }

    private static void ProcessPendingPrewarm(
        EntityManager entityManager,
        PoolBuildSettings bulletSettings,
        PoolBuildSettings rocketSettings)
    {
        int bulletBudgetRemaining = math.max(0, bulletSettings.maxPrewarmPerFrame);
        int rocketBudgetRemaining = math.max(0, rocketSettings.maxPrewarmPerFrame);

        if (bulletBudgetRemaining <= 0 && rocketBudgetRemaining <= 0)
        {
            return;
        }

        using EntityQuery pendingQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<ProjectilePool>(),
            ComponentType.ReadWrite<ProjectilePoolPendingPrewarm>());

        if (pendingQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        using NativeArray<Entity> poolEntities = pendingQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < poolEntities.Length; i++)
        {
            Entity poolEntity = poolEntities[i];
            if (!entityManager.Exists(poolEntity) ||
                !entityManager.HasComponent<ProjectilePool>(poolEntity) ||
                !entityManager.HasComponent<ProjectilePoolPendingPrewarm>(poolEntity))
            {
                continue;
            }

            ProjectilePool pool = entityManager.GetComponentData<ProjectilePool>(poolEntity);
            ProjectilePoolPendingPrewarm pending = entityManager.GetComponentData<ProjectilePoolPendingPrewarm>(poolEntity);
            int budgetRemaining = pool.kind == ProjectilePoolKind.Bullet ? bulletBudgetRemaining : rocketBudgetRemaining;
            if (budgetRemaining <= 0)
            {
                continue;
            }

            Entity prefabEntity = pending.prefabEntity != Entity.Null ? pending.prefabEntity : pool.prefabEntity;
            if (prefabEntity == Entity.Null || !entityManager.Exists(prefabEntity))
            {
                entityManager.RemoveComponent<ProjectilePoolPendingPrewarm>(poolEntity);
                continue;
            }

            int capacityRemaining = math.max(0, pool.hardCap - pool.totalSpawned);
            int growCount = math.min(budgetRemaining, math.min(math.max(1, pool.growChunk), math.min(pending.remaining, capacityRemaining)));
            if (growCount <= 0)
            {
                entityManager.RemoveComponent<ProjectilePoolPendingPrewarm>(poolEntity);
                continue;
            }

            int runtimeExpandCountBeforePrewarm = pool.runtimeExpandCount;
            ProjectilePoolUtility.GrowFreeProjectiles(entityManager, prefabEntity, poolEntity, ref pool, growCount);
            pool.runtimeExpandCount = runtimeExpandCountBeforePrewarm;
            pending.remaining -= growCount;

            if (pool.kind == ProjectilePoolKind.Bullet)
            {
                bulletBudgetRemaining -= growCount;
            }
            else if (pool.kind == ProjectilePoolKind.Rocket)
            {
                rocketBudgetRemaining -= growCount;
            }

            entityManager.SetComponentData(poolEntity, pool);
            if (pending.remaining <= 0 || pool.totalSpawned >= pool.hardCap)
            {
                entityManager.RemoveComponent<ProjectilePoolPendingPrewarm>(poolEntity);
            }
            else
            {
                entityManager.SetComponentData(poolEntity, pending);
            }
        }
    }

    private static void TryQueueProjectilePoolPrefab(
        EntityManager entityManager,
        Entity ammoEntity,
        ProjectilePoolKind kind,
        ref NativeHashSet<Entity> knownPoolPrefabs,
        ref NativeList<Entity> ammoPrefabsToCreate)
    {
        if (ammoEntity == Entity.Null || !entityManager.Exists(ammoEntity) || knownPoolPrefabs.Contains(ammoEntity))
        {
            return;
        }

        if (!HasProjectileKind(entityManager, ammoEntity, kind))
        {
            return;
        }

        if (!ValidateProjectilePrefab(entityManager, ammoEntity, kind))
        {
            return;
        }

        if (!ammoPrefabsToCreate.IsCreated)
        {
            ammoPrefabsToCreate = new NativeList<Entity>(Allocator.Temp);
        }

        if (knownPoolPrefabs.Add(ammoEntity))
        {
            ammoPrefabsToCreate.Add(ammoEntity);
        }
    }

    private static bool HasProjectileKind(EntityManager entityManager, Entity ammoEntity, ProjectilePoolKind kind)
    {
        if (kind == ProjectilePoolKind.Bullet)
        {
            return entityManager.HasComponent<Bullet>(ammoEntity);
        }

        if (kind == ProjectilePoolKind.Rocket)
        {
            return entityManager.HasComponent<Rocket>(ammoEntity);
        }

        return false;
    }

    private static PoolBuildSettings CreateDefaultBulletSettings()
    {
        return new PoolBuildSettings
        {
            kind = ProjectilePoolKind.Bullet,
            prewarmPerPrefab = DefaultBulletPrewarmPerPrefab,
            growChunk = DefaultBulletGrowChunk,
            hardCapPerPrefab = DefaultBulletHardCapPerPrefab,
            allowRuntimeExpand = true,
            logRuntimeExpand = false,
            keepBootstrapEnabledForRuntimePrefabs = false,
            runtimeScanInterval = DefaultRuntimeScanInterval,
            maxPoolsCreatedPerFrame = DefaultMaxPoolsCreatedPerFrame,
            maxPrewarmPerFrame = DefaultMaxPrewarmPerFrame,
            inactivePosition = new float3(999999f, 999999f, -9999f),
        };
    }

    private static PoolBuildSettings CreateDefaultRocketSettings()
    {
        return new PoolBuildSettings
        {
            kind = ProjectilePoolKind.Rocket,
            prewarmPerPrefab = DefaultRocketPrewarmPerPrefab,
            growChunk = DefaultRocketGrowChunk,
            hardCapPerPrefab = DefaultRocketHardCapPerPrefab,
            allowRuntimeExpand = true,
            logRuntimeExpand = false,
            keepBootstrapEnabledForRuntimePrefabs = false,
            runtimeScanInterval = DefaultRuntimeScanInterval,
            maxPoolsCreatedPerFrame = DefaultMaxPoolsCreatedPerFrame,
            maxPrewarmPerFrame = DefaultMaxPrewarmPerFrame,
            inactivePosition = new float3(999999f, 999999f, -9999f),
        };
    }

    private static void NormalizeSettings(ref PoolBuildSettings settings)
    {
        settings.prewarmPerPrefab = math.max(0, settings.prewarmPerPrefab);
        settings.growChunk = math.max(1, settings.growChunk);
        settings.hardCapPerPrefab = math.max(1, math.max(settings.hardCapPerPrefab, settings.prewarmPerPrefab));
        settings.runtimeScanInterval = math.max(0.05f, settings.runtimeScanInterval <= 0f ? DefaultRuntimeScanInterval : settings.runtimeScanInterval);
        settings.maxPoolsCreatedPerFrame = math.max(1, settings.maxPoolsCreatedPerFrame <= 0 ? DefaultMaxPoolsCreatedPerFrame : settings.maxPoolsCreatedPerFrame);
        settings.maxPrewarmPerFrame = math.max(1, settings.maxPrewarmPerFrame <= 0 ? DefaultMaxPrewarmPerFrame : settings.maxPrewarmPerFrame);
    }

    private static bool ValidateProjectilePrefab(EntityManager entityManager, Entity ammoEntity, ProjectilePoolKind kind)
    {
        bool valid = true;
        string label = kind == ProjectilePoolKind.Bullet ? "BulletPool" : "RocketPool";

        if (kind == ProjectilePoolKind.Bullet && !entityManager.HasComponent<BulletActive>(ammoEntity))
        {
            Debug.LogError($"[{label}] Bullet prefab {ammoEntity.Index}:{ammoEntity.Version} has Bullet but missing BulletActive. Re-bake BulletAuthoring.");
            valid = false;
        }

        if (kind == ProjectilePoolKind.Rocket && !entityManager.HasComponent<RocketActive>(ammoEntity))
        {
            Debug.LogError($"[{label}] Rocket prefab {ammoEntity.Index}:{ammoEntity.Version} has Rocket but missing RocketActive. Re-bake RocketAuthoring.");
            valid = false;
        }

        if (!entityManager.HasComponent<WeaponPayloadRuntime>(ammoEntity))
        {
            Debug.LogError($"[{label}] Projectile prefab {ammoEntity.Index}:{ammoEntity.Version} missing WeaponPayloadRuntime.");
            valid = false;
        }

        if (!entityManager.HasComponent<Target>(ammoEntity))
        {
            Debug.LogError($"[{label}] Projectile prefab {ammoEntity.Index}:{ammoEntity.Version} missing Target.");
            valid = false;
        }

        if (!entityManager.HasComponent<LocalTransform>(ammoEntity))
        {
            Debug.LogError($"[{label}] Projectile prefab {ammoEntity.Index}:{ammoEntity.Version} missing LocalTransform.");
            valid = false;
        }

        if (!entityManager.HasComponent<ProjectilePoolMember>(ammoEntity))
        {
            Debug.LogError($"[{label}] Projectile prefab {ammoEntity.Index}:{ammoEntity.Version} missing ProjectilePoolMember. Re-bake ammo prefab.");
            valid = false;
        }

        if (kind == ProjectilePoolKind.Rocket)
        {
            if (!entityManager.HasComponent<Velocity>(ammoEntity))
            {
                Debug.LogError($"[{label}] Rocket prefab {ammoEntity.Index}:{ammoEntity.Version} missing Velocity.");
                valid = false;
            }

            if (!entityManager.HasComponent<LastKnownTarget>(ammoEntity))
            {
                Debug.LogError($"[{label}] Rocket prefab {ammoEntity.Index}:{ammoEntity.Version} missing LastKnownTarget.");
                valid = false;
            }
        }

        return valid;
    }

    private static void CreatePoolForPrefab(
        EntityManager entityManager,
        Entity registryEntity,
        Entity ammoEntity,
        PoolBuildSettings settings)
    {
        Entity poolEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(poolEntity, new ProjectilePool
        {
            prefabEntity = ammoEntity,
            kind = settings.kind,
            totalSpawned = 0,
            activeCount = 0,
            peakActiveCount = 0,
            runtimeExpandCount = 0,
            droppedShotCount = 0,
            duplicateFreeEntryCount = 0,
            doubleReleaseCount = 0,
            growChunk = settings.growChunk,
            hardCap = settings.hardCapPerPrefab,
            allowRuntimeExpand = settings.allowRuntimeExpand,
            logRuntimeExpand = settings.logRuntimeExpand,
            inactivePosition = settings.inactivePosition,
        });

        entityManager.AddBuffer<ProjectilePoolFreeElement>(poolEntity);
        entityManager.AddBuffer<ProjectilePoolAllElement>(poolEntity);
        entityManager.AddComponentData(poolEntity, new ProjectilePoolLogState());

        int prewarmCount = math.min(settings.prewarmPerPrefab, settings.hardCapPerPrefab);
        if (prewarmCount > 0)
        {
            entityManager.AddComponentData(poolEntity, new ProjectilePoolPendingPrewarm
            {
                prefabEntity = ammoEntity,
                kind = settings.kind,
                remaining = prewarmCount,
            });
        }

        entityManager.GetBuffer<ProjectilePoolPrefabLink>(registryEntity).Add(new ProjectilePoolPrefabLink
        {
            prefabEntity = ammoEntity,
            poolEntity = poolEntity,
            kind = settings.kind,
        });
    }
}
