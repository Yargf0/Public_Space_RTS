using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public struct ShipDamageDarkeningInitialized : IComponentData
{
}

public struct ShipDamageDarkeningTarget : IComponentData
{
    public Entity healthEntity;
}

public struct ShipDamageDarkeningBaseColor : IComponentData
{
    public float4 value;
}

public struct ShipDamageDarkeningState : IComponentData
{
    public float lastHealthFraction;
    public float pendingHealthFraction;
    public float nextUpdateTime;
    public float updatePhase;
    public byte dirty;
}

public struct ShipDamageDarkeningRenderTarget : IBufferElementData
{
    public Entity renderEntity;
    public byte writeMaterialColor;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StrategicIconSpawnSystem))]
public partial class ShipDamageDarkeningBindSystem : SystemBase
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private EntityQuery pendingShipQuery;

    protected override void OnCreate()
    {
        pendingShipQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Unit>(),
                ComponentType.ReadOnly<Health>(),
            },
            None = new[]
            {
                ComponentType.ReadOnly<ShipDamageDarkeningInitialized>(),
                ComponentType.ReadOnly<Prefab>(),
            },
        });

        RequireForUpdate(pendingShipQuery);
    }

    protected override void OnUpdate()
    {
        EntityManager entityManager = EntityManager;
        EntityCommandBuffer ecb = new EntityCommandBuffer(WorldUpdateAllocator);
        HashSet<Entity> excludedVisuals = BuildExcludedVisualSet(entityManager);

        foreach ((RefRO<Health> _, RefRO<Unit> _, Entity shipEntity) in
                 SystemAPI.Query<RefRO<Health>, RefRO<Unit>>()
                     .WithNone<ShipDamageDarkeningInitialized, Prefab>()
                     .WithEntityAccess())
        {
            DynamicBuffer<ShipDamageDarkeningRenderTarget> targets =
                ecb.AddBuffer<ShipDamageDarkeningRenderTarget>(shipEntity);

            ecb.AddComponent(shipEntity, new ShipDamageDarkeningState
            {
                lastHealthFraction = -1f,
                pendingHealthFraction = 1f,
                nextUpdateTime = 0f,
                updatePhase = ResolveStableUpdatePhase(shipEntity),
                dirty = 1,
            });

            if (entityManager.HasComponent<Selected>(shipEntity))
            {
                Selected selected = entityManager.GetComponentData<Selected>(shipEntity);
                if (selected.VisualEntity != Entity.Null)
                {
                    excludedVisuals.Add(selected.VisualEntity);
                }
            }

            if (entityManager.HasBuffer<LinkedEntityGroup>(shipEntity))
            {
                DynamicBuffer<LinkedEntityGroup> linked = entityManager.GetBuffer<LinkedEntityGroup>(shipEntity);
                for (int i = 0; i < linked.Length; i++)
                {
                    TryAddRenderTarget(ref ecb, entityManager, shipEntity, linked[i].Value, excludedVisuals, ref targets);
                }
            }
            else
            {
                TryAddRenderTarget(ref ecb, entityManager, shipEntity, shipEntity, excludedVisuals, ref targets);
            }

            ecb.AddComponent<ShipDamageDarkeningInitialized>(shipEntity);
        }

        ecb.Playback(entityManager);
        ecb.Dispose();
    }

    private static HashSet<Entity> BuildExcludedVisualSet(EntityManager entityManager)
    {
        HashSet<Entity> result = new HashSet<Entity>();
        using EntityQuery healthBarQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<HealthBar>());
        using Unity.Collections.NativeArray<HealthBar> healthBars = healthBarQuery.ToComponentDataArray<HealthBar>(Unity.Collections.Allocator.Temp);

        for (int i = 0; i < healthBars.Length; i++)
        {
            HealthBar healthBar = healthBars[i];
            if (healthBar.barVisualEntity != Entity.Null)
            {
                result.Add(healthBar.barVisualEntity);
            }
        }

        using Unity.Collections.NativeArray<Entity> healthBarEntities = healthBarQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < healthBarEntities.Length; i++)
        {
            result.Add(healthBarEntities[i]);
        }

        return result;
    }

    private static bool TryAddRenderTarget(
        ref EntityCommandBuffer ecb,
        EntityManager entityManager,
        Entity shipEntity,
        Entity renderEntity,
        HashSet<Entity> excludedVisuals,
        ref DynamicBuffer<ShipDamageDarkeningRenderTarget> targets)
    {
        if (renderEntity == Entity.Null ||
            !entityManager.Exists(renderEntity) ||
            excludedVisuals.Contains(renderEntity) ||
            entityManager.HasComponent<ShipDamageDarkeningTarget>(renderEntity) ||
            entityManager.HasComponent<StrategicIcon>(renderEntity) ||
            entityManager.HasComponent<HealthBar>(renderEntity) ||
            !entityManager.HasComponent<MaterialMeshInfo>(renderEntity) ||
            IsZeroScaleVisual(entityManager, renderEntity))
        {
            return false;
        }

        float4 baseColor = ResolveBaseColor(entityManager, renderEntity);
        bool writeMaterialColor = ShouldWriteMaterialColor(entityManager, renderEntity);

        ecb.AddComponent(renderEntity, new ShipDamageDarkeningTarget
        {
            healthEntity = shipEntity,
        });

        ecb.AddComponent(renderEntity, new ShipDamageDarkeningBaseColor
        {
            value = baseColor,
        });

        if (entityManager.HasComponent<URPMaterialPropertyBaseColor>(renderEntity))
        {
            ecb.SetComponent(renderEntity, new URPMaterialPropertyBaseColor { Value = baseColor });
        }
        else
        {
            ecb.AddComponent(renderEntity, new URPMaterialPropertyBaseColor { Value = baseColor });
        }

        if (writeMaterialColor)
        {
            if (entityManager.HasComponent<MaterialColor>(renderEntity))
            {
                ecb.SetComponent(renderEntity, new MaterialColor { Value = baseColor });
            }
            else
            {
                ecb.AddComponent(renderEntity, new MaterialColor { Value = baseColor });
            }
        }

        targets.Add(new ShipDamageDarkeningRenderTarget
        {
            renderEntity = renderEntity,
            writeMaterialColor = writeMaterialColor ? (byte)1 : (byte)0,
        });

        return true;
    }

    private static bool ShouldWriteMaterialColor(EntityManager entityManager, Entity renderEntity)
    {
        if (entityManager.HasComponent<MaterialColor>(renderEntity))
        {
            return true;
        }

        if (!TryResolveMaterial(entityManager, renderEntity, out Material material))
        {
            return false;
        }

        return !material.HasProperty(BaseColorId) && material.HasProperty(ColorId);
    }

    private static float ResolveStableUpdatePhase(Entity entity)
    {
        uint hash = (uint)entity.Index * 747796405u;
        hash ^= (uint)entity.Version * 2891336453u;
        hash ^= hash >> 16;
        hash *= 2246822519u;
        hash ^= hash >> 13;

        return ((hash & 1023u) / 1023f) * ShipDamageDarkeningUpdateSystem.ColorUpdateInterval;
    }

    private static bool IsZeroScaleVisual(EntityManager entityManager, Entity entity)
    {
        if (!entityManager.HasComponent<LocalTransform>(entity))
        {
            return false;
        }

        LocalTransform transform = entityManager.GetComponentData<LocalTransform>(entity);
        return transform.Scale <= 0.001f;
    }

    private static float4 ResolveBaseColor(EntityManager entityManager, Entity renderEntity)
    {
        if (entityManager.HasComponent<URPMaterialPropertyBaseColor>(renderEntity))
        {
            return SanitizeColor(entityManager.GetComponentData<URPMaterialPropertyBaseColor>(renderEntity).Value);
        }

        if (entityManager.HasComponent<MaterialColor>(renderEntity))
        {
            return SanitizeColor(entityManager.GetComponentData<MaterialColor>(renderEntity).Value);
        }

        if (TryResolveMaterial(entityManager, renderEntity, out Material material))
        {
            if (material.HasProperty(BaseColorId))
            {
                return ToLinearFloat4(material.GetColor(BaseColorId));
            }

            if (material.HasProperty(ColorId))
            {
                return ToLinearFloat4(material.GetColor(ColorId));
            }
        }

        return new float4(1f, 1f, 1f, 1f);
    }

    private static bool TryResolveMaterial(EntityManager entityManager, Entity renderEntity, out Material material)
    {
        material = null;
        MaterialMeshInfo materialMeshInfo = entityManager.GetComponentData<MaterialMeshInfo>(renderEntity);

        if (!materialMeshInfo.HasMaterialMeshIndexRange && materialMeshInfo.Material >= 0)
        {
            EntitiesGraphicsSystem graphicsSystem = entityManager.World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (graphicsSystem != null)
            {
                material = graphicsSystem.GetMaterial(new BatchMaterialID { value = (uint)materialMeshInfo.Material });
                return material != null;
            }
        }

        try
        {
            RenderMeshArray renderMeshArray = entityManager.GetSharedComponentManaged<RenderMeshArray>(renderEntity);
            material = renderMeshArray.GetMaterial(materialMeshInfo);
            return material != null;
        }
        catch
        {
            return false;
        }
    }

    private static float4 ToLinearFloat4(Color color)
    {
        Color linear = color.linear;
        return SanitizeColor(new float4(linear.r, linear.g, linear.b, linear.a));
    }

    private static float4 SanitizeColor(float4 color)
    {
        if (color.w <= 0.001f)
        {
            color.w = 1f;
        }

        return color;
    }
}

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateBefore(typeof(ResetEventSystem))]
public partial struct ShipDamageDarkeningUpdateSystem : ISystem
{
    public const float ColorUpdateInterval = 0.05f;

    private const float MinBrightness = 0.7f;
    private const float HealthFractionUpdateEpsilon = 0.005f;

    private ComponentLookup<ShipDamageDarkeningBaseColor> baseColorLookup;
    private ComponentLookup<URPMaterialPropertyBaseColor> urpColorLookup;
    private ComponentLookup<MaterialColor> materialColorLookup;

    public void OnCreate(ref SystemState state)
    {
        baseColorLookup = state.GetComponentLookup<ShipDamageDarkeningBaseColor>(true);
        urpColorLookup = state.GetComponentLookup<URPMaterialPropertyBaseColor>(false);
        materialColorLookup = state.GetComponentLookup<MaterialColor>(false);
        state.RequireForUpdate<ShipDamageDarkeningState>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        baseColorLookup.Update(ref state);
        urpColorLookup.Update(ref state);
        materialColorLookup.Update(ref state);

        float now = (float)SystemAPI.Time.ElapsedTime;

        foreach ((RefRO<Health> health,
                  RefRW<ShipDamageDarkeningState> darkeningState,
                  DynamicBuffer<ShipDamageDarkeningRenderTarget> targets)
                 in SystemAPI.Query<
                     RefRO<Health>,
                     RefRW<ShipDamageDarkeningState>,
                     DynamicBuffer<ShipDamageDarkeningRenderTarget>>())
        {
            bool healthChanged = health.ValueRO.onHealthChanged || darkeningState.ValueRO.lastHealthFraction < 0f;
            float lastHealthFraction = darkeningState.ValueRO.lastHealthFraction;
            float pendingHealthFraction = darkeningState.ValueRO.pendingHealthFraction;
            byte dirty = darkeningState.ValueRO.dirty;

            if (healthChanged)
            {
                pendingHealthFraction = ResolveHealthFraction(health.ValueRO);
                if (ShouldUpdateColor(pendingHealthFraction, lastHealthFraction))
                {
                    dirty = 1;
                }
            }

            if (dirty == 0)
            {
                continue;
            }

            bool forceUpdate = lastHealthFraction < 0f ||
                               ReachedExtremeHealthFraction(pendingHealthFraction, lastHealthFraction);

            if (!forceUpdate && now + 0.0001f < darkeningState.ValueRO.nextUpdateTime)
            {
                if (healthChanged || darkeningState.ValueRO.dirty != dirty)
                {
                    darkeningState.ValueRW.pendingHealthFraction = pendingHealthFraction;
                    darkeningState.ValueRW.dirty = dirty;
                }

                continue;
            }

            if (!ShouldUpdateColor(pendingHealthFraction, lastHealthFraction))
            {
                darkeningState.ValueRW.pendingHealthFraction = pendingHealthFraction;
                darkeningState.ValueRW.dirty = 0;
                continue;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                ShipDamageDarkeningRenderTarget target = targets[i];
                Entity renderEntity = target.renderEntity;

                if (!baseColorLookup.HasComponent(renderEntity))
                {
                    continue;
                }

                float4 color = ResolveDamageColor(pendingHealthFraction, baseColorLookup[renderEntity].value);

                if (urpColorLookup.HasComponent(renderEntity))
                {
                    urpColorLookup.GetRefRW(renderEntity).ValueRW.Value = color;
                }

                if (target.writeMaterialColor != 0 && materialColorLookup.HasComponent(renderEntity))
                {
                    materialColorLookup.GetRefRW(renderEntity).ValueRW.Value = color;
                }
            }

            darkeningState.ValueRW.lastHealthFraction = pendingHealthFraction;
            darkeningState.ValueRW.pendingHealthFraction = pendingHealthFraction;
            darkeningState.ValueRW.nextUpdateTime = ResolveNextUpdateTime(now, darkeningState.ValueRO.updatePhase);
            darkeningState.ValueRW.dirty = 0;
        }
    }

    private static bool ShouldUpdateColor(float healthFraction, float lastHealthFraction)
    {
        if (lastHealthFraction < 0f)
        {
            return true;
        }

        if (ReachedExtremeHealthFraction(healthFraction, lastHealthFraction))
        {
            return true;
        }

        return math.abs(healthFraction - lastHealthFraction) >= HealthFractionUpdateEpsilon;
    }

    private static bool ReachedExtremeHealthFraction(float healthFraction, float lastHealthFraction)
    {
        return (healthFraction <= 0f && lastHealthFraction > 0f) ||
               (healthFraction >= 1f && lastHealthFraction < 1f);
    }

    private static float ResolveNextUpdateTime(float now, float updatePhase)
    {
        float bucket = math.floor((now - updatePhase) / ColorUpdateInterval) + 1f;
        return updatePhase + bucket * ColorUpdateInterval;
    }

    private static float4 ResolveDamageColor(float healthFraction, float4 baseColor)
    {
        float smoothed = healthFraction * healthFraction * (3f - 2f * healthFraction);
        float brightness = math.lerp(MinBrightness, 1f, smoothed);

        return new float4(
            baseColor.x * brightness,
            baseColor.y * brightness,
            baseColor.z * brightness,
            baseColor.w);
    }

    private static float ResolveHealthFraction(Health health)
    {
        if (health.healthAmountMax <= 0f)
        {
            return health.healthAmount > 0f ? 1f : 0f;
        }

        return math.saturate(health.healthAmount / health.healthAmountMax);
    }
}
