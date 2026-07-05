using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct StrategicIconSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StrategicIconRenderData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        StrategicIconRenderData renderData = SystemAPI.GetSingleton<StrategicIconRenderData>();
        if (renderData.IconPrefab == Entity.Null)
        {
            return;
        }

        Entity configEntity = SystemAPI.GetSingletonEntity<StrategicIconRenderData>();
        DynamicBuffer<StrategicIconMaterialEntry> materialEntries =
            SystemAPI.GetBuffer<StrategicIconMaterialEntry>(configEntity);
        ComponentLookup<ShipCatalogId> shipCatalogIds = SystemAPI.GetComponentLookup<ShipCatalogId>(true);
        ComponentLookup<SquadronTag> squadronTags = SystemAPI.GetComponentLookup<SquadronTag>(true);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach ((RefRO<Unit> unit, RefRO<LocalToWorld> _, Entity entity) in
                 SystemAPI.Query<RefRO<Unit>, RefRO<LocalToWorld>>()
                     .WithNone<StrategicIconOwner, Prefab, SquadComponent>()
                     .WithEntityAccess())
        {
            if (squadronTags.HasComponent(entity))
            {
                continue;
            }

            int iconKey = ResolveIconKey(entity, unit.ValueRO, shipCatalogIds);
            MaterialMeshInfo materialMeshInfo = ResolveMaterial(materialEntries, iconKey, renderData.FallbackMaterial);
            Entity icon = ecb.Instantiate(renderData.IconPrefab);

            ecb.SetComponent(icon, materialMeshInfo);
            ecb.SetComponent(icon, new StrategicIcon
            {
                Owner = entity,
                Faction = unit.ValueRO.faction,
                ShipSize = unit.ValueRO.shipSize,
                IconKey = iconKey,
            });
            ecb.SetComponent(icon, LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 0.001f));
            ecb.SetComponent(icon, new URPMaterialPropertyBaseColor
            {
                Value = new float4(1f, 1f, 1f, 0f),
            });
            ecb.SetComponentEnabled<MaterialMeshInfo>(icon, false);
            ecb.AddComponent(entity, new StrategicIconOwner
            {
                Icon = icon,
            });
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private static int ResolveIconKey(Entity entity, Unit unit, ComponentLookup<ShipCatalogId> shipCatalogIds)
    {
        if (unit.faction == Faction.Enemy)
        {
            return StrategicIconKeys.ResolveEnemyKey(unit.shipSize);
        }

        if (shipCatalogIds.HasComponent(entity))
        {
            int shipId = shipCatalogIds[entity].Value;
            if (shipId >= 0)
            {
                return shipId;
            }
        }

        return StrategicIconKeys.Fallback;
    }

    private static MaterialMeshInfo ResolveMaterial(
        DynamicBuffer<StrategicIconMaterialEntry> materialEntries,
        int iconKey,
        MaterialMeshInfo fallbackMaterial)
    {
        for (int i = 0; i < materialEntries.Length; i++)
        {
            StrategicIconMaterialEntry entry = materialEntries[i];
            if (entry.IconKey == iconKey)
            {
                return entry.MaterialMeshInfo;
            }
        }

        return fallbackMaterial;
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StrategicIconSpawnSystem))]
public partial struct StrategicIconCleanupSystem : ISystem
{
    private const double CleanupIntervalSeconds = 0.5;

    private double nextCleanupTime;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StrategicIconRenderData>();
        nextCleanupTime = 0;
    }

    public void OnUpdate(ref SystemState state)
    {
        double now = SystemAPI.Time.ElapsedTime;
        if (now < nextCleanupTime)
        {
            return;
        }

        nextCleanupTime = now + CleanupIntervalSeconds;

        ComponentLookup<StrategicIconOwner> ownerLookup = SystemAPI.GetComponentLookup<StrategicIconOwner>(true);
        ComponentLookup<StrategicIcon> iconLookup = SystemAPI.GetComponentLookup<StrategicIcon>(true);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach ((RefRO<StrategicIcon> icon, Entity entity) in
                 SystemAPI.Query<RefRO<StrategicIcon>>().WithEntityAccess())
        {
            Entity owner = icon.ValueRO.Owner;
            bool ownerMissing = owner == Entity.Null || !state.EntityManager.Exists(owner);
            bool ownerLostIcon = !ownerMissing &&
                                 (!ownerLookup.HasComponent(owner) || ownerLookup[owner].Icon != entity);

            if (ownerMissing || ownerLostIcon)
            {
                ecb.DestroyEntity(entity);
            }
        }

        foreach ((RefRO<StrategicIconOwner> owner, Entity entity) in
                 SystemAPI.Query<RefRO<StrategicIconOwner>>().WithEntityAccess())
        {
            Entity icon = owner.ValueRO.Icon;
            if (icon == Entity.Null || !state.EntityManager.Exists(icon) || !iconLookup.HasComponent(icon))
            {
                ecb.RemoveComponent<StrategicIconOwner>(entity);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
[BurstCompile]
public partial struct StrategicIconUpdateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StrategicIconSettings>();
        state.RequireForUpdate<StrategicIconCameraData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        StrategicIconUpdateJob job = new StrategicIconUpdateJob
        {
            Settings = SystemAPI.GetSingleton<StrategicIconSettings>(),
            CameraData = SystemAPI.GetSingleton<StrategicIconCameraData>(),
            Units = SystemAPI.GetComponentLookup<Unit>(true),
            OwnerTransforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
            Healths = SystemAPI.GetComponentLookup<Health>(true),
            Visibilities = SystemAPI.GetComponentLookup<Visibility>(true),
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    private static bool TryResolveVisual(
        StrategicIcon icon,
        StrategicIconSettings settings,
        StrategicIconCameraData cameraData,
        ComponentLookup<Unit> units,
        ComponentLookup<LocalTransform> ownerTransforms,
        ComponentLookup<Health> healths,
        ComponentLookup<Visibility> visibilities,
        out float3 position,
        out float scale,
        out float4 color)
    {
        position = default;
        scale = 0f;
        color = default;

        Entity owner = icon.Owner;
        if (owner == Entity.Null ||
            cameraData.IsValid == 0 ||
            cameraData.ZoomAlpha <= 0f ||
            !units.HasComponent(owner) ||
            !ownerTransforms.HasComponent(owner))
        {
            return false;
        }

        Unit unit = units[owner];
        if (unit.faction == Faction.Friendly && settings.ShowFriendly == 0)
        {
            return false;
        }

        if (unit.faction == Faction.Enemy && settings.ShowEnemy == 0)
        {
            return false;
        }

        if (healths.HasComponent(owner) && healths[owner].healthAmount <= 0f)
        {
            return false;
        }

        if (settings.RespectFogOfWar != 0 &&
            unit.faction == Faction.Enemy &&
            !IsVisibleToFriendly(owner, visibilities))
        {
            return false;
        }

        float3 ownerPosition = ownerTransforms[owner].Position;
        float2 ownerXY = ownerPosition.xy;
        float2 min = cameraData.Center - cameraData.HalfExtents - cameraData.ScreenPaddingWorld;
        float2 max = cameraData.Center + cameraData.HalfExtents + cameraData.ScreenPaddingWorld;
        if (math.any(ownerXY < min) || math.any(ownerXY > max))
        {
            return false;
        }

        float healthFraction = ResolveHealthFraction(owner, healths);
        float4 lowColor = unit.faction == Faction.Enemy
            ? settings.EnemyLowHealthColor
            : settings.FriendlyLowHealthColor;
        float4 fullColor = unit.faction == Faction.Enemy
            ? settings.EnemyFullHealthColor
            : settings.FriendlyFullHealthColor;

        color = math.lerp(lowColor, fullColor, healthFraction);
        color.w *= cameraData.ZoomAlpha;
        if (color.w <= 0f)
        {
            return false;
        }

        float sizePixels = ResolveSizePixels(unit.shipSize, settings);
        scale = math.max(0.001f, sizePixels * cameraData.WorldUnitsPerPixel);
        position = new float3(ownerPosition.x, ownerPosition.y, settings.IconZ);
        return true;
    }

    private static bool IsVisibleToFriendly(Entity entity, ComponentLookup<Visibility> visibilities)
    {
        if (!visibilities.HasComponent(entity) || !visibilities.IsComponentEnabled(entity))
        {
            return false;
        }

        return visibilities[entity].visibleToFriendlyTimer > 0f;
    }

    private static float ResolveHealthFraction(Entity entity, ComponentLookup<Health> healths)
    {
        if (!healths.HasComponent(entity))
        {
            return 1f;
        }

        Health health = healths[entity];
        if (health.healthAmountMax <= 0f)
        {
            return health.healthAmount > 0f ? 1f : 0f;
        }

        return math.saturate(health.healthAmount / health.healthAmountMax);
    }

    private static float ResolveSizePixels(byte shipSize, StrategicIconSettings settings)
    {
        ShipSize size = (ShipSize)shipSize;
        if ((size & (ShipSize.Big | ShipSize.RocketBig)) != 0)
        {
            return settings.BigSizePixels;
        }

        if ((size & ShipSize.Medium) != 0)
        {
            return settings.MediumSizePixels;
        }

        if ((size & (ShipSize.Small | ShipSize.RocketSmall)) != 0)
        {
            return settings.SmallSizePixels;
        }

        return settings.DefaultSizePixels;
    }

    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    private partial struct StrategicIconUpdateJob : IJobEntity
    {
        public StrategicIconSettings Settings;
        public StrategicIconCameraData CameraData;

        [ReadOnly] public ComponentLookup<Unit> Units;
        [ReadOnly] public ComponentLookup<LocalTransform> OwnerTransforms;
        [ReadOnly] public ComponentLookup<Health> Healths;
        [ReadOnly] public ComponentLookup<Visibility> Visibilities;

        private void Execute(
            in StrategicIcon icon,
            ref LocalToWorld localToWorld,
            ref URPMaterialPropertyBaseColor baseColor,
            EnabledRefRW<MaterialMeshInfo> renderEnabled)
        {
            if (!TryResolveVisual(
                    icon,
                    Settings,
                    CameraData,
                    Units,
                    OwnerTransforms,
                    Healths,
                    Visibilities,
                    out float3 position,
                    out float scale,
                    out float4 color))
            {
                renderEnabled.ValueRW = false;
                return;
            }

            localToWorld.Value = float4x4.TRS(position, quaternion.identity, new float3(scale, scale, scale));
            baseColor.Value = color;
            renderEnabled.ValueRW = true;
        }
    }
}
