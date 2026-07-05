using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// builds weapon blob from catalog. database entity is runtime so it survives SubScene unload
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct WeaponProfileDatabaseBuildSystem : ISystem
{
    private Entity databaseEntity;
    private Entity sourceCatalogEntity;
    private BlobAssetReference<WeaponProfileDatabaseBlob> profileBlob;

    public void OnCreate(ref SystemState state)
    {
        databaseEntity = Entity.Null;
        sourceCatalogEntity = Entity.Null;
        state.RequireForUpdate<WeaponCatalogTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        EntityQuery query = SystemAPI.QueryBuilder()
            .WithAll<WeaponCatalogTag, WeaponProfileBakeElement>()
            .Build();

        int catalogCount = query.CalculateEntityCount();
        if (catalogCount == 0)
            return;

        NativeArray<Entity> catalogEntities = query.ToEntityArray(Allocator.Temp);
        Entity catalogEntity = Entity.Null;

        for (int i = 0; i < catalogEntities.Length; i++)
        {
            Entity candidate = catalogEntities[i];
            if (!entityManager.Exists(candidate) || !entityManager.HasBuffer<WeaponProfileBakeElement>(candidate))
                continue;

            DynamicBuffer<WeaponProfileBakeElement> candidateBuffer = entityManager.GetBuffer<WeaponProfileBakeElement>(candidate);
            if (candidateBuffer.Length <= 0)
                continue;

            catalogEntity = candidate;
            break;
        }

        catalogEntities.Dispose();

        if (catalogEntity == Entity.Null)
        {
            Debug.LogWarning("WeaponProfileDatabaseBuildSystem: no non-empty weapon catalog found.");
            return;
        }

        if (databaseEntity != Entity.Null &&
            entityManager.Exists(databaseEntity) &&
            sourceCatalogEntity == catalogEntity &&
            profileBlob.IsCreated)
        {
            return;
        }

        DynamicBuffer<WeaponProfileBakeElement> buffer = entityManager.GetBuffer<WeaponProfileBakeElement>(catalogEntity);
        int profileCount = buffer.Length;

        if (profileCount == 0)
        {
            Debug.LogWarning($"WeaponProfileDatabaseBuildSystem: weapon catalog {catalogEntity.Index}:{catalogEntity.Version} is empty.");
            return;
        }

        BlobAssetReference<WeaponProfileDatabaseBlob> newBlob = BuildBlob(buffer, profileCount);

        if (databaseEntity != Entity.Null && entityManager.Exists(databaseEntity))
            entityManager.DestroyEntity(databaseEntity);

        if (profileBlob.IsCreated)
            profileBlob.Dispose();

        profileBlob = newBlob;
        sourceCatalogEntity = catalogEntity;

        databaseEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(databaseEntity, new WeaponProfileDatabase { Value = profileBlob });

        ClearWeaponSummaryInitialized(ref state);

        Debug.Log($"WeaponProfileDatabase rebuilt: {profileCount} profiles. sourceCatalog={catalogEntity.Index}:{catalogEntity.Version}");
    }

    public void OnDestroy(ref SystemState state)
    {
        if (profileBlob.IsCreated)
            profileBlob.Dispose();

        if (databaseEntity != Entity.Null && state.EntityManager.Exists(databaseEntity))
            state.EntityManager.DestroyEntity(databaseEntity);
    }

    private static BlobAssetReference<WeaponProfileDatabaseBlob> BuildBlob(DynamicBuffer<WeaponProfileBakeElement> buffer, int profileCount)
    {
        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref WeaponProfileDatabaseBlob root = ref builder.ConstructRoot<WeaponProfileDatabaseBlob>();
        BlobBuilderArray<WeaponProfileBlob> profiles = builder.Allocate(ref root.Profiles, profileCount);

        for (int i = 0; i < profileCount; i++)
        {
            WeaponProfileBakeElement src = buffer[i];
            profiles[i] = new WeaponProfileBlob
            {
                reloadTime = src.reloadTime,
                attackDistance = src.attackDistance,
                damageAmount = src.damageAmount,
                projectileSpeed = src.projectileSpeed,
                lifetime = src.lifetime,
                rotate = src.rotate,
                limitRotation = src.limitRotation,
                rotationLimitAngle = src.rotationLimitAngle,
                allowedTargets = src.allowedTargets,
                priorityTargets = src.priorityTargets,
                requestKind = src.requestKind,
                firePattern = src.firePattern,
                payloadKind = src.payloadKind,
                statusEffectKind = src.statusEffectKind,
                burstInterval = src.burstInterval,
                spreadAngle = src.spreadAngle,
                splashRadius = src.splashRadius,
                statusDuration = src.statusDuration,
                moveSpeedMultiplier = src.moveSpeedMultiplier,
                accelerationMultiplier = src.accelerationMultiplier,
                disableWeapons = src.disableWeapons,
                rocketAcceleration = src.rocketAcceleration,
                turnAfterLaunch = src.turnAfterLaunch,
                rocketLaunchScatterAngle = src.rocketLaunchScatterAngle,
                rocketLaunchScatterDuration = src.rocketLaunchScatterDuration,
                rocketLaunchScatterDistance = src.rocketLaunchScatterDistance,
                hitscanBeamWidth = src.hitscanBeamWidth,
            };
        }

        return builder.CreateBlobAssetReference<WeaponProfileDatabaseBlob>(Allocator.Persistent);
    }

    private static void ClearWeaponSummaryInitialized(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        EntityQuery summaryQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<WeaponShipSummaryInitialized>()
            .Build(ref state);

        if (summaryQuery.CalculateEntityCount() == 0)
            return;

        NativeArray<Entity> entities = summaryQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            if (entityManager.Exists(entity) && entityManager.HasComponent<WeaponShipSummaryInitialized>(entity))
                entityManager.RemoveComponent<WeaponShipSummaryInitialized>(entity);
        }

        entities.Dispose();
    }
}
