using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class StrategicIconRenderBootstrap : MonoBehaviour
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");

    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private ShipCatalogAsset catalog;
    [SerializeField] private Sprite fallbackIcon;
    [SerializeField] private Sprite enemySmallFallbackIcon;
    [SerializeField] private Sprite enemyMediumFallbackIcon;
    [SerializeField] private Sprite enemyBigFallbackIcon;

    [Header("Zoom")]
    [SerializeField] private float fadeStartOrthoSize = 28f;
    [SerializeField] private float fadeFullOrthoSize = 36f;

    [Header("Layout")]
    [SerializeField] private Vector2 iconSize = new Vector2(32f, 32f);
    [SerializeField] private Vector2 smallIconSize = new Vector2(12f, 12f);
    [SerializeField] private Vector2 mediumIconSize = new Vector2(24f, 24f);
    [SerializeField] private Vector2 bigIconSize = new Vector2(50f, 50f);
    [SerializeField] private float screenPadding = 48f;
    [SerializeField] private float iconZ = GameConstants.BuildPreviewZ - 1f;
    [SerializeField] private int renderLayer = 0;

    [Header("Filters")]
    [SerializeField] private bool showFriendly = true;
    [SerializeField] private bool showEnemy = true;
    [SerializeField] private bool respectFogOfWar = true;

    [Header("Health Tint")]
    [SerializeField] private Color friendlyLowHealthColor = new Color(0.05f, 0.35f, 0.08f, 1f);
    [SerializeField] private Color friendlyFullHealthColor = new Color(0.65f, 1f, 0.65f, 1f);
    [SerializeField] private Color enemyLowHealthColor = new Color(0.35f, 0.02f, 0.02f, 1f);
    [SerializeField] private Color enemyFullHealthColor = new Color(1f, 0.55f, 0.55f, 1f);

    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly HashSet<int> registeredKeys = new HashSet<int>();

    private World registeredWorld;
    private EntityManager registeredEntityManager;
    private Entity configEntity = Entity.Null;
    private Entity iconPrefab = Entity.Null;
    private Mesh quadMesh;

    private void OnEnable()
    {
        EnsureRegistered();
        WriteCameraData();
    }

    private void Update()
    {
        EnsureRegistered();
        WriteCameraData();
    }

    private void LateUpdate()
    {
        WriteCameraData();
    }

    private void OnDisable()
    {
        CleanupRegisteredWorld();
        DestroyRuntimeObjects();
    }

    private void OnDestroy()
    {
        CleanupRegisteredWorld();
        DestroyRuntimeObjects();
    }

    private bool EnsureRegistered()
    {
        if (configEntity != Entity.Null &&
            registeredWorld != null &&
            registeredWorld.IsCreated &&
            registeredEntityManager.Exists(configEntity))
        {
            return true;
        }

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        EntitiesGraphicsSystem graphicsSystem = world.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        if (graphicsSystem == null)
        {
            return false;
        }

        CleanupRegisteredWorld();
        DestroyRuntimeObjects();

        registeredWorld = world;
        registeredEntityManager = entityManager;
        registeredKeys.Clear();

        quadMesh = CreateQuadMesh();
        BatchMeshID meshId = graphicsSystem.RegisterMesh(quadMesh);

        configEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(configEntity, BuildSettings());
        entityManager.AddComponentData(configEntity, new StrategicIconCameraData());
        DynamicBuffer<StrategicIconMaterialEntry> materials = entityManager.AddBuffer<StrategicIconMaterialEntry>(configEntity);

        MaterialMeshInfo fallbackMaterial = RegisterIconMaterial(
            graphicsSystem,
            materials,
            meshId,
            StrategicIconKeys.Fallback,
            fallbackIcon,
            "StrategicIcon_Fallback");

        RegisterIconMaterial(graphicsSystem, materials, meshId, StrategicIconKeys.EnemySmall,
            enemySmallFallbackIcon != null ? enemySmallFallbackIcon : fallbackIcon, "StrategicIcon_EnemySmall");
        RegisterIconMaterial(graphicsSystem, materials, meshId, StrategicIconKeys.EnemyMedium,
            enemyMediumFallbackIcon != null ? enemyMediumFallbackIcon : fallbackIcon, "StrategicIcon_EnemyMedium");
        RegisterIconMaterial(graphicsSystem, materials, meshId, StrategicIconKeys.EnemyBig,
            enemyBigFallbackIcon != null ? enemyBigFallbackIcon : fallbackIcon, "StrategicIcon_EnemyBig");
        RegisterCatalogMaterials(graphicsSystem, materials, meshId);

        iconPrefab = CreateIconPrefab(entityManager, fallbackMaterial);
        entityManager.AddComponentData(configEntity, new StrategicIconRenderData
        {
            IconPrefab = iconPrefab,
            FallbackMaterial = fallbackMaterial,
        });

        WriteCameraData();
        return true;
    }

    private StrategicIconSettings BuildSettings()
    {
        return new StrategicIconSettings
        {
            DefaultSizePixels = SanitizeSize(iconSize, 32f),
            SmallSizePixels = SanitizeSize(smallIconSize, 12f),
            MediumSizePixels = SanitizeSize(mediumIconSize, 24f),
            BigSizePixels = SanitizeSize(bigIconSize, 50f),
            FadeStartOrthoSize = fadeStartOrthoSize,
            FadeFullOrthoSize = fadeFullOrthoSize,
            ScreenPaddingPixels = math.max(0f, screenPadding),
            IconZ = iconZ,
            ShowFriendly = ToByte(showFriendly),
            ShowEnemy = ToByte(showEnemy),
            RespectFogOfWar = ToByte(respectFogOfWar),
            FriendlyLowHealthColor = ToLinearFloat4(friendlyLowHealthColor),
            FriendlyFullHealthColor = ToLinearFloat4(friendlyFullHealthColor),
            EnemyLowHealthColor = ToLinearFloat4(enemyLowHealthColor),
            EnemyFullHealthColor = ToLinearFloat4(enemyFullHealthColor),
        };
    }

    private Entity CreateIconPrefab(EntityManager entityManager, MaterialMeshInfo fallbackMaterial)
    {
        Entity prefab = entityManager.CreateEntity(typeof(Prefab));
        RenderMeshDescription description = new RenderMeshDescription(
            ShadowCastingMode.Off,
            receiveShadows: false,
            motionVectorGenerationMode: MotionVectorGenerationMode.Camera,
            layer: renderLayer,
            renderingLayerMask: uint.MaxValue,
            lightProbeUsage: LightProbeUsage.Off);

        RenderMeshUtility.AddComponents(prefab, entityManager, description, fallbackMaterial);
        entityManager.AddComponentData(prefab, LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 1f));
        entityManager.AddComponentData(prefab, new StrategicIcon
        {
            Owner = Entity.Null,
            Faction = Faction.Friendly,
            ShipSize = (byte)ShipSize.Small,
            IconKey = StrategicIconKeys.Fallback,
        });
        entityManager.AddComponentData(prefab, new URPMaterialPropertyBaseColor
        {
            Value = new float4(1f, 1f, 1f, 0f),
        });
        entityManager.SetComponentEnabled<MaterialMeshInfo>(prefab, false);
        return prefab;
    }

    private void RegisterCatalogMaterials(
        EntitiesGraphicsSystem graphicsSystem,
        DynamicBuffer<StrategicIconMaterialEntry> materials,
        BatchMeshID meshId)
    {
        if (catalog == null || catalog.ships == null)
        {
            return;
        }

        for (int i = 0; i < catalog.ships.Length; i++)
        {
            ShipCatalogAssetEntry ship = catalog.ships[i];
            if (ship.id < 0)
            {
                continue;
            }

            Sprite icon = ship.Icon != null ? ship.Icon : fallbackIcon;
            RegisterIconMaterial(graphicsSystem, materials, meshId, ship.id, icon, $"StrategicIcon_{ship.id}");
        }
    }

    private MaterialMeshInfo RegisterIconMaterial(
        EntitiesGraphicsSystem graphicsSystem,
        DynamicBuffer<StrategicIconMaterialEntry> materials,
        BatchMeshID meshId,
        int iconKey,
        Sprite sprite,
        string materialName)
    {
        if (!registeredKeys.Add(iconKey))
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].IconKey == iconKey)
                {
                    return materials[i].MaterialMeshInfo;
                }
            }
        }

        Material material = CreateRuntimeMaterial(sprite, materialName);
        BatchMaterialID materialId = graphicsSystem.RegisterMaterial(material);
        MaterialMeshInfo materialMeshInfo = new MaterialMeshInfo(materialId, meshId);
        materials.Add(new StrategicIconMaterialEntry
        {
            IconKey = iconKey,
            MaterialMeshInfo = materialMeshInfo,
        });
        return materialMeshInfo;
    }

    private Material CreateRuntimeMaterial(Sprite sprite, string materialName)
    {
        Shader shader = ResolveIconShader();
        Material material = new Material(shader)
        {
            name = materialName,
            enableInstancing = true,
            renderQueue = (int)RenderQueue.Transparent,
        };

        ConfigureTransparentMaterial(material);
        SetMaterialColor(material, Color.white);

        if (sprite != null && sprite.texture != null)
        {
            SetMaterialTexture(material, sprite);
        }

        runtimeMaterials.Add(material);
        return material;
    }

    private void WriteCameraData()
    {
        if (configEntity == Entity.Null ||
            registeredWorld == null ||
            !registeredWorld.IsCreated ||
            !registeredEntityManager.Exists(configEntity))
        {
            return;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        StrategicIconCameraData cameraData = default;
        if (targetCamera != null && targetCamera.orthographic && Screen.height > 0)
        {
            float orthoSize = math.max(0.01f, targetCamera.orthographicSize);
            float aspect = targetCamera.aspect > 0f ? targetCamera.aspect : 1f;
            float worldUnitsPerPixel = (orthoSize * 2f) / Screen.height;
            float zoomAlpha = ResolveZoomAlpha(orthoSize);

            Vector3 cameraPosition = targetCamera.transform.position;
            cameraData = new StrategicIconCameraData
            {
                Center = new float2(cameraPosition.x, cameraPosition.y),
                HalfExtents = new float2(orthoSize * aspect, orthoSize),
                WorldUnitsPerPixel = worldUnitsPerPixel,
                ScreenPaddingWorld = screenPadding * worldUnitsPerPixel,
                ZoomAlpha = zoomAlpha,
                IsValid = 1,
            };
        }

        registeredEntityManager.SetComponentData(configEntity, cameraData);
    }

    private float ResolveZoomAlpha(float orthographicSize)
    {
        float start = fadeStartOrthoSize;
        float full = fadeFullOrthoSize;
        if (full <= start)
        {
            return orthographicSize >= full ? 1f : 0f;
        }

        return Mathf.InverseLerp(start, full, orthographicSize);
    }

    private void CleanupRegisteredWorld()
    {
        if (registeredWorld == null || !registeredWorld.IsCreated)
        {
            configEntity = Entity.Null;
            iconPrefab = Entity.Null;
            return;
        }

        EntityManager entityManager = registeredWorld.EntityManager;

        EntityQuery icons = entityManager.CreateEntityQuery(typeof(StrategicIcon));
        if (!icons.IsEmptyIgnoreFilter)
        {
            entityManager.DestroyEntity(icons);
        }
        icons.Dispose();

        EntityQuery owners = entityManager.CreateEntityQuery(typeof(StrategicIconOwner));
        if (!owners.IsEmptyIgnoreFilter)
        {
            entityManager.RemoveComponent<StrategicIconOwner>(owners);
        }
        owners.Dispose();

        if (iconPrefab != Entity.Null && entityManager.Exists(iconPrefab))
        {
            entityManager.DestroyEntity(iconPrefab);
        }

        if (configEntity != Entity.Null && entityManager.Exists(configEntity))
        {
            entityManager.DestroyEntity(configEntity);
        }

        configEntity = Entity.Null;
        iconPrefab = Entity.Null;
        registeredWorld = null;
    }

    private void DestroyRuntimeObjects()
    {
        for (int i = 0; i < runtimeMaterials.Count; i++)
        {
            DestroyUnityObject(runtimeMaterials[i]);
        }

        runtimeMaterials.Clear();
        registeredKeys.Clear();
        DestroyUnityObject(quadMesh);
        quadMesh = null;
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "StrategicIconQuad",
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

    private static Shader ResolveIconShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        return shader;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material.HasProperty(SurfaceId)) { material.SetFloat(SurfaceId, 1f); }
        if (material.HasProperty(BlendId)) { material.SetFloat(BlendId, 0f); }
        if (material.HasProperty(SrcBlendId)) { material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha); }
        if (material.HasProperty(DstBlendId)) { material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha); }
        if (material.HasProperty(ZWriteId)) { material.SetFloat(ZWriteId, 0f); }
        if (material.HasProperty(CullId)) { material.SetFloat(CullId, (float)CullMode.Off); }
        if (material.HasProperty(AlphaClipId)) { material.SetFloat(AlphaClipId, 0f); }

        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private static void SetMaterialTexture(Material material, Sprite sprite)
    {
        Texture2D texture = sprite.texture;
        Rect rect = sprite.textureRect;
        Vector2 scale = new Vector2(rect.width / texture.width, rect.height / texture.height);
        Vector2 offset = new Vector2(rect.xMin / texture.width, rect.yMin / texture.height);

        if (material.HasProperty(BaseMapId))
        {
            material.SetTexture(BaseMapId, texture);
            material.SetVector(BaseMapStId, new Vector4(scale.x, scale.y, offset.x, offset.y));
        }

        if (material.HasProperty(MainTexId))
        {
            material.SetTexture(MainTexId, texture);
            material.SetVector(MainTexStId, new Vector4(scale.x, scale.y, offset.x, offset.y));
        }

        material.mainTexture = texture;
        material.mainTextureScale = scale;
        material.mainTextureOffset = offset;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty(BaseColorId)) { material.SetColor(BaseColorId, color); }
        if (material.HasProperty(ColorId)) { material.SetColor(ColorId, color); }
        material.color = color;
    }

    private static float SanitizeSize(Vector2 size, float fallback)
    {
        if (size.x <= 0f && size.y <= 0f)
        {
            return fallback;
        }

        if (size.x <= 0f)
        {
            return size.y;
        }

        if (size.y <= 0f)
        {
            return size.x;
        }

        return math.max(size.x, size.y);
    }

    private static float4 ToLinearFloat4(Color color)
    {
        Color linear = color.linear;
        return new float4(linear.r, linear.g, linear.b, linear.a);
    }

    private static byte ToByte(bool value)
    {
        return value ? (byte)1 : (byte)0;
    }

    private static void DestroyUnityObject(Object unityObject)
    {
        if (unityObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(unityObject);
        }
        else
        {
            DestroyImmediate(unityObject);
        }
    }
}
