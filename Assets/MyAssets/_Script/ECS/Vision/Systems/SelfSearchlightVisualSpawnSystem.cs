using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SearchlightVisualSystem))]
public partial class SelfSearchlightVisualSpawnSystem : SystemBase
{
    private const int LightMaskLayer = 10;
    private const float CleanupInterval = 0.5f;

    private Entity visualPrefab = Entity.Null;
    private EntityQuery missingInstanceQuery;
    private EntityQuery removedSearchlightQuery;
    private EntityQuery runtimeVisualQuery;
    private EntityQuery ownerInstanceQuery;
    private Mesh quadMesh;
    private Material material;
    private bool missingShaderLogged;
    private double nextCleanupTime;

    protected override void OnCreate()
    {
        missingInstanceQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<SelfSearchlight>(),
                ComponentType.ReadOnly<Searchlight>(),
            },
            None = new[]
            {
                ComponentType.ReadOnly<SelfSearchlightVisualInstance>(),
                ComponentType.ReadOnly<Prefab>(),
            },
        });

        removedSearchlightQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<SelfSearchlightVisualInstance>(),
            },
            None = new[]
            {
                ComponentType.ReadOnly<Searchlight>(),
            },
        });

        runtimeVisualQuery = GetEntityQuery(
            ComponentType.ReadOnly<SelfSearchlightVisualRuntime>(),
            ComponentType.ReadOnly<SearchlightVisualOwner>());

        ownerInstanceQuery = GetEntityQuery(ComponentType.ReadOnly<SelfSearchlightVisualInstance>());

        RequireForUpdate<SelfSearchlight>();
    }

    protected override void OnDestroy()
    {
        if (visualPrefab != Entity.Null && EntityManager.Exists(visualPrefab))
        {
            EntityManager.DestroyEntity(visualPrefab);
            visualPrefab = Entity.Null;
        }

        DestroyUnityObject(material);
        DestroyUnityObject(quadMesh);
        material = null;
        quadMesh = null;
    }

    protected override void OnUpdate()
    {
        if (!EnsureVisualPrefab())
            return;

        double now = SystemAPI.Time.ElapsedTime;
        bool hasSpawnWork = !missingInstanceQuery.IsEmptyIgnoreFilter;
        bool cleanupDue = now >= nextCleanupTime;
        if (!hasSpawnWork && !cleanupDue)
        {
            return;
        }

        Entity prefab = visualPrefab;
        EntityManager em = EntityManager;
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        if (hasSpawnWork)
        {
            foreach ((RefRO<Searchlight> _, Entity owner) in
                     SystemAPI.Query<RefRO<Searchlight>>()
                         .WithAll<SelfSearchlight>()
                         .WithNone<SelfSearchlightVisualInstance, Prefab>()
                         .WithEntityAccess())
            {
                Entity visual = ecb.Instantiate(prefab);
                ecb.SetComponent(visual, LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 1f));
                ecb.AddComponent(visual, new Parent { Value = owner });
                ecb.AddComponent(visual, new SearchlightVisualOwner { Owner = owner });
                ecb.AddComponent(owner, new SelfSearchlightVisualInstance { Visual = visual });
            }
        }

        if (cleanupDue)
        {
            nextCleanupTime = now + CleanupInterval;
            RunCleanup(em, ref ecb);
        }

        ecb.Playback(em);
        ecb.Dispose();
    }

    private void RunCleanup(EntityManager em, ref EntityCommandBuffer ecb)
    {
        if (!removedSearchlightQuery.IsEmptyIgnoreFilter)
        {
            foreach ((RefRO<SelfSearchlightVisualInstance> instance, Entity owner) in
                     SystemAPI.Query<RefRO<SelfSearchlightVisualInstance>>()
                         .WithNone<Searchlight>()
                         .WithEntityAccess())
            {
                Entity visual = instance.ValueRO.Visual;
                if (visual != Entity.Null && em.Exists(visual))
                {
                    ecb.DestroyEntity(visual);
                }

                ecb.RemoveComponent<SelfSearchlightVisualInstance>(owner);
            }
        }

        if (!runtimeVisualQuery.IsEmptyIgnoreFilter)
        {
            foreach ((RefRO<SearchlightVisualOwner> owner, Entity visual) in
                     SystemAPI.Query<RefRO<SearchlightVisualOwner>>()
                         .WithAll<SelfSearchlightVisualRuntime>()
                         .WithEntityAccess())
            {
                Entity ownerEntity = owner.ValueRO.Owner;
                if (ownerEntity == Entity.Null ||
                    !em.Exists(ownerEntity) ||
                    !em.HasComponent<SelfSearchlight>(ownerEntity))
                {
                    ecb.DestroyEntity(visual);
                }
            }
        }

        if (!ownerInstanceQuery.IsEmptyIgnoreFilter)
        {
            foreach ((RefRO<SelfSearchlightVisualInstance> instance, Entity owner) in
                     SystemAPI.Query<RefRO<SelfSearchlightVisualInstance>>()
                         .WithEntityAccess())
            {
                Entity visual = instance.ValueRO.Visual;
                if (visual == Entity.Null || !em.Exists(visual))
                {
                    ecb.RemoveComponent<SelfSearchlightVisualInstance>(owner);
                }
            }
        }
    }

    private bool EnsureVisualPrefab()
    {
        if (visualPrefab != Entity.Null && EntityManager.Exists(visualPrefab))
            return true;

        EntitiesGraphicsSystem graphicsSystem = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        if (graphicsSystem == null)
            return false;

        Shader shader = ResolveSearchlightShader();
        if (shader == null)
        {
            if (!missingShaderLogged)
            {
                Debug.LogError("SelfSearchlightVisualSpawnSystem: could not find Shader Graphs/SearchlightMask shader.");
                missingShaderLogged = true;
            }

            return false;
        }

        quadMesh = CreateQuadMesh();
        material = CreateMaterial(shader);

        BatchMeshID meshId = graphicsSystem.RegisterMesh(quadMesh);
        BatchMaterialID materialId = graphicsSystem.RegisterMaterial(material);
        MaterialMeshInfo materialMeshInfo = new MaterialMeshInfo(materialId, meshId);

        visualPrefab = EntityManager.CreateEntity(typeof(Prefab));
        RenderMeshDescription description = new RenderMeshDescription(
            ShadowCastingMode.Off,
            receiveShadows: false,
            motionVectorGenerationMode: MotionVectorGenerationMode.Camera,
            layer: LightMaskLayer,
            renderingLayerMask: uint.MaxValue,
            lightProbeUsage: LightProbeUsage.Off);

        RenderMeshUtility.AddComponents(visualPrefab, EntityManager, description, materialMeshInfo);
        EntityManager.AddComponentData(visualPrefab, LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 1f));
        EntityManager.AddComponentData(visualPrefab, new SearchlightVisual());
        EntityManager.AddComponentData(visualPrefab, new SelfSearchlightVisualRuntime());
        EntityManager.AddComponentData(visualPrefab, new LightMask_IsCircle { Value = 1f });
        EntityManager.AddComponentData(visualPrefab, new LightMask_Opacity { Value = 0f });
        EntityManager.AddComponentData(visualPrefab, new PostTransformMatrix { Value = float4x4.identity });

        return true;
    }

    private static Shader ResolveSearchlightShader()
    {
        Shader shader = Shader.Find("Shader Graphs/SearchlightMask");
        if (shader == null)
            shader = Shader.Find("SearchlightMask");

        return shader;
    }

    private static Material CreateMaterial(Shader shader)
    {
        Material maskMaterial = new Material(shader)
        {
            name = "RuntimeSelfSearchlightMask",
            enableInstancing = true,
        };

        SetFloatIfPresent(maskMaterial, "_Alpha", 1f);
        SetFloatIfPresent(maskMaterial, "_CircleEdgeSoft", 2f);
        SetFloatIfPresent(maskMaterial, "_EdgeHardness", 10f);
        SetFloatIfPresent(maskMaterial, "_IsCircle", 1f);
        SetFloatIfPresent(maskMaterial, "_Opacity", 0f);
        SetFloatIfPresent(maskMaterial, "_ZWrite", 0f);
        SetColorIfPresent(maskMaterial, "_Color", new Color(0f, 0f, 0f, 0f));

        return maskMaterial;
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "RuntimeSelfSearchlightQuad",
        };

        mesh.SetVertices(new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
        });
        mesh.SetUVs(0, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f),
        });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void SetFloatIfPresent(Material target, string property, float value)
    {
        if (target.HasProperty(property))
            target.SetFloat(property, value);
    }

    private static void SetColorIfPresent(Material target, string property, Color value)
    {
        if (target.HasProperty(property))
            target.SetColor(property, value);
    }

    private static void DestroyUnityObject(Object unityObject)
    {
        if (unityObject == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(unityObject);
        else
            Object.DestroyImmediate(unityObject);
    }
}
