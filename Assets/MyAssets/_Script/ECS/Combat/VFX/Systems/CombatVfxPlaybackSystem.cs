using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class CombatVfxPlaybackSystem : SystemBase
{
    private const int MuzzleBudget = 96;
    private const int TracerBudget = 120;
    private const int BeamBudget = 72;
    private const int ImpactBudget = 120;
    private const int ExplosionBudget = 42;
    private const int DeathBudget = 18;
    private const int StatusBudget = 48;
    // pools are prewarmed big so they don't grow in battle
    // show/hide with renderer.enabled, SetActive is much slower
    private const int PrewarmSpriteCount = 512;
    private const int PrewarmLineCount = 384;
    private const float CameraRefreshInterval = 0.5f;

    private EntityQuery requestQuery;

    private GameObject root;
    private Material spriteMaterial;
    private Material lineMaterial;
    private Sprite softCircleSprite;
    private Camera cachedCamera;
    private float nextCameraRefreshTime;

    private readonly Queue<SpriteVisual> spritePool = new Queue<SpriteVisual>(256);
    private readonly Queue<LineVisual> linePool = new Queue<LineVisual>(256);
    private readonly List<SpriteVisual> activeSprites = new List<SpriteVisual>(256);
    private readonly List<LineVisual> activeLines = new List<LineVisual>(256);

    private int muzzleCount;
    private int tracerCount;
    private int beamCount;
    private int impactCount;
    private int explosionCount;
    private int deathCount;
    private int statusCount;

    protected override void OnCreate()
    {
        requestQuery = GetEntityQuery(ComponentType.ReadOnly<CombatVfxRequest>());
    }

    protected override void OnUpdate()
    {
        EnsureRuntimeObjects();

        float now = (float)SystemAPI.Time.ElapsedTime;
        ResetFrameBudgets();

        if (!requestQuery.IsEmptyIgnoreFilter)
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            Camera camera = ResolveCamera(now);

            foreach ((RefRO<CombatVfxRequest> request, Entity entity) in
                     SystemAPI.Query<RefRO<CombatVfxRequest>>()
                         .WithEntityAccess())
            {
                CombatVfxRequest value = request.ValueRO;
                TryPlayRequest(in value, now, camera);
                ecb.DestroyEntity(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        UpdateSprites(now);
        UpdateLines(now);
    }

    protected override void OnDestroy()
    {
        if (root != null)
        {
            Object.Destroy(root);
        }

        if (softCircleSprite != null)
        {
            Object.Destroy(softCircleSprite.texture);
            Object.Destroy(softCircleSprite);
        }

        if (spriteMaterial != null)
        {
            Object.Destroy(spriteMaterial);
        }

        if (lineMaterial != null)
        {
            Object.Destroy(lineMaterial);
        }

        cachedCamera = null;
    }

    private void TryPlayRequest(in CombatVfxRequest request, float now, Camera camera)
    {
        CombatVfxKind kind = (CombatVfxKind)request.kind;
        if (!ConsumeBudget(kind, request.priority))
        {
            return;
        }

        if (ShouldCull(request, camera))
        {
            return;
        }

        switch (kind)
        {
            case CombatVfxKind.MuzzleFlash:
                PlayMuzzle(request, now);
                break;
            case CombatVfxKind.ProjectileTracer:
                PlayTracer(request, now);
                break;
            case CombatVfxKind.Beam:
                PlayBeam(request, now);
                break;
            case CombatVfxKind.Impact:
                PlayImpact(request, now);
                break;
            case CombatVfxKind.Explosion:
                PlayExplosion(request, now, false);
                break;
            case CombatVfxKind.DeathExplosion:
                PlayExplosion(request, now, true);
                break;
            case CombatVfxKind.Status:
                PlayStatus(request, now);
                break;
        }
    }

    private void PlayMuzzle(in CombatVfxRequest request, float now)
    {
        CombatVfxStyle style = (CombatVfxStyle)request.style;
        Color color = ResolveColor(style, request.faction, 0.9f);
        float radius = Mathf.Max(0.18f, request.radius);
        float lifetime = ResolveLifetime(request, 0.065f);
        float angle = DirectionToAngle(request.direction);

        PlayOrientedSprite(
            request.start,
            angle,
            radius * 0.5f,
            radius * 0.95f,
            radius * 1.05f,
            radius * 1.8f,
            color,
            now,
            lifetime);
    }

    private void PlayTracer(in CombatVfxRequest request, float now)
    {
        CombatVfxStyle style = (CombatVfxStyle)request.style;
        float width = Mathf.Max(0.025f, request.width);
        float lifetime = ResolveLifetime(request, 0.045f);

        if (!IsBallisticTracerStyle(style))
        {
            Color color = ResolveColor(style, request.faction, 0.85f);
            PlayLine(request.start, request.end, width, color, now, lifetime, true);
            return;
        }

        float2 direction2 = math.normalizesafe(request.direction, new float2(0f, 1f));
        float3 direction = new float3(direction2.x, direction2.y, 0f);
        float travelLength = math.length((request.end - request.start).xy);
        if (travelLength < 0.001f)
        {
            return;
        }

        float angle = DirectionToAngle(direction2);
        float boltLength = Mathf.Min(ResolveTracerBoltLength(style), travelLength * 0.85f);
        float3 nose = request.end;
        float3 coreCenter = nose - direction * (boltLength * 0.5f);

        Color wake = ResolveColor(style, request.faction, 0.38f);
        PlayLine(request.start, nose, width * 0.55f, wake, now, lifetime * 1.2f, true);

        if (style == CombatVfxStyle.BallisticHeavy || style == CombatVfxStyle.Flak)
        {
            float2 perpendicular2 = new float2(-direction2.y, direction2.x);
            float3 perpendicular = new float3(perpendicular2.x, perpendicular2.y, 0f) * width * 1.15f;
            Color sideWake = ResolveColor(style, request.faction, 0.22f);
            PlayLine(request.start + perpendicular, nose + perpendicular * 0.25f, width * 0.22f, sideWake, now, lifetime * 1.05f, true);
            PlayLine(request.start - perpendicular, nose - perpendicular * 0.25f, width * 0.22f, sideWake, now, lifetime * 1.05f, true);
        }

        Color core = ResolveColor(style, request.faction, 0.95f);
        PlayOrientedSprite(
            coreCenter,
            angle,
            width * 2.35f,
            boltLength,
            width * 0.85f,
            boltLength * 0.42f,
            core,
            now,
            lifetime * 0.9f);

        if (style == CombatVfxStyle.BallisticHeavy || style == CombatVfxStyle.Flak)
        {
            Color spark = style == CombatVfxStyle.Flak
                ? new Color(1f, 0.82f, 0.34f, 0.88f)
                : new Color(1f, 0.96f, 0.62f, 0.82f);
            PlaySprite(nose, 0f, width * 2.1f, width * 0.45f, spark, now, lifetime * 0.65f);
        }
    }

    private void PlayBeam(in CombatVfxRequest request, float now)
    {
        CombatVfxStyle style = (CombatVfxStyle)request.style;
        Color color = ResolveColor(style, request.faction, 0.95f);
        float width = Mathf.Max(0.05f, request.width);
        PlayLine(request.start, request.end, width, color, now, ResolveLifetime(request, 0.08f), false);

        if (style == CombatVfxStyle.RailgunHeavy)
        {
            PlaySprite(request.start, DirectionToAngle(request.direction), width * 2.1f, width * 4.4f, color, now, 0.12f);
        }
    }

    private void PlayImpact(in CombatVfxRequest request, float now)
    {
        CombatVfxStyle style = (CombatVfxStyle)request.style;
        Color color = ResolveColor(style, request.faction, 0.78f);
        float radius = Mathf.Max(0.26f, request.radius);
        PlaySprite(request.start, 0f, radius * 0.35f, radius * 1.05f, color, now, ResolveLifetime(request, 0.14f));
    }

    private void PlayExplosion(in CombatVfxRequest request, float now, bool death)
    {
        CombatVfxStyle style = (CombatVfxStyle)request.style;
        bool torpedoExplosion = !death && style == CombatVfxStyle.TorpedoHeavy;
        Color color = ResolveColor(style, request.faction, death ? 0.9f : 0.82f);
        float radius = Mathf.Max(death ? 1.0f : 0.55f, request.radius);
        float lifetime = ResolveLifetime(request, death ? 0.65f : 0.42f);

        if (torpedoExplosion)
        {
            Color shock = new Color(0.12f, 0.58f, 1f, 0.42f);
            PlaySprite(request.start, 0f, radius * 0.55f, radius * 2.55f, shock, now, lifetime * 0.78f);
        }

        float outerEndScale = death ? 2.1f : torpedoExplosion ? 1.95f : 1.55f;
        PlaySprite(request.start, 0f, radius * 0.25f, radius * outerEndScale, color, now, lifetime);

        Color core = torpedoExplosion
            ? new Color(0.74f, 0.95f, 1f, 0.95f)
            : new Color(1f, 0.95f, 0.65f, 0.9f);
        float coreEndScale = torpedoExplosion ? 0.92f : 0.72f;
        PlaySprite(request.start, 0f, radius * 0.12f, radius * coreEndScale, core, now, Mathf.Min(torpedoExplosion ? 0.26f : 0.18f, lifetime));
    }

    private void PlayStatus(in CombatVfxRequest request, float now)
    {
        CombatVfxStyle style = (CombatVfxStyle)request.style;
        Color color = ResolveColor(style, request.faction, 0.82f);
        float radius = Mathf.Max(0.6f, request.radius);
        PlaySprite(request.start, 0f, radius * 0.65f, radius * 1.45f, color, now, ResolveLifetime(request, 0.45f));
    }

    private void PlaySprite(
        float3 position,
        float angleDegrees,
        float startScale,
        float endScale,
        Color color,
        float now,
        float lifetime)
    {
        PlayOrientedSprite(
            position,
            angleDegrees,
            startScale,
            startScale,
            endScale,
            endScale,
            color,
            now,
            lifetime);
    }

    private void PlayOrientedSprite(
        float3 position,
        float angleDegrees,
        float startWidth,
        float startLength,
        float endWidth,
        float endLength,
        Color color,
        float now,
        float lifetime)
    {
        SpriteVisual visual = AcquireSprite();
        Transform transform = visual.GameObject.transform;
        transform.position = new Vector3(position.x, position.y, position.z);
        transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);
        transform.localScale = new Vector3(
            Mathf.Max(0.001f, startWidth),
            Mathf.Max(0.001f, startLength),
            1f);

        visual.Renderer.color = color;
        visual.StartTime = now;
        visual.EndTime = now + Mathf.Max(0.01f, lifetime);
        visual.StartScale = new Vector2(startWidth, startLength);
        visual.EndScale = new Vector2(endWidth, endLength);
        visual.StartColor = color;
        visual.Renderer.enabled = true;
        activeSprites.Add(visual);
    }

    private void PlayLine(
        float3 start,
        float3 end,
        float width,
        Color color,
        float now,
        float lifetime,
        bool taper)
    {
        float3 delta = end - start;
        if (math.lengthsq(delta.xy) < 0.0001f)
        {
            return;
        }

        LineVisual visual = AcquireLine();
        visual.Line.positionCount = 2;
        visual.Line.SetPosition(0, new Vector3(start.x, start.y, start.z));
        visual.Line.SetPosition(1, new Vector3(end.x, end.y, end.z));
        visual.Line.startWidth = Mathf.Max(0.001f, width);
        visual.Line.endWidth = taper ? Mathf.Max(0.001f, width * 0.25f) : Mathf.Max(0.001f, width);
        visual.Line.startColor = color;
        visual.Line.endColor = new Color(color.r, color.g, color.b, taper ? color.a * 0.35f : color.a);
        visual.Line.enabled = true;

        visual.StartTime = now;
        visual.EndTime = now + Mathf.Max(0.01f, lifetime);
        visual.StartColor = color;
        visual.StartWidth = width;
        visual.Taper = taper;
        activeLines.Add(visual);
    }

    private void UpdateSprites(float now)
    {
        for (int i = activeSprites.Count - 1; i >= 0; i--)
        {
            SpriteVisual visual = activeSprites[i];
            float duration = Mathf.Max(0.001f, visual.EndTime - visual.StartTime);
            float t = Mathf.Clamp01((now - visual.StartTime) / duration);

            if (t >= 1f)
            {
                ReleaseSpriteAt(i);
                continue;
            }

            float eased = 1f - (1f - t) * (1f - t);
            Vector2 scale = Vector2.Lerp(visual.StartScale, visual.EndScale, eased);
            Color color = visual.StartColor;
            color.a *= 1f - t;

            visual.GameObject.transform.localScale = new Vector3(
                Mathf.Max(0.001f, scale.x),
                Mathf.Max(0.001f, scale.y),
                1f);
            visual.Renderer.color = color;
        }
    }

    private void UpdateLines(float now)
    {
        for (int i = activeLines.Count - 1; i >= 0; i--)
        {
            LineVisual visual = activeLines[i];
            float duration = Mathf.Max(0.001f, visual.EndTime - visual.StartTime);
            float t = Mathf.Clamp01((now - visual.StartTime) / duration);

            if (t >= 1f)
            {
                ReleaseLineAt(i);
                continue;
            }

            Color color = visual.StartColor;
            color.a *= 1f - t;
            visual.Line.startColor = color;
            visual.Line.endColor = new Color(color.r, color.g, color.b, visual.Taper ? color.a * 0.35f : color.a);
            visual.Line.startWidth = visual.StartWidth * (1f - t * 0.35f);
            visual.Line.endWidth = visual.Taper
                ? visual.StartWidth * 0.25f * (1f - t)
                : visual.StartWidth * (1f - t * 0.35f);
        }
    }

    private bool ConsumeBudget(CombatVfxKind kind, byte priority)
    {
        int limit;
        ref int count = ref muzzleCount;

        switch (kind)
        {
            case CombatVfxKind.MuzzleFlash:
                limit = MuzzleBudget;
                count = ref muzzleCount;
                break;
            case CombatVfxKind.ProjectileTracer:
                limit = TracerBudget;
                count = ref tracerCount;
                break;
            case CombatVfxKind.Beam:
                limit = BeamBudget;
                count = ref beamCount;
                break;
            case CombatVfxKind.Impact:
                limit = ImpactBudget;
                count = ref impactCount;
                break;
            case CombatVfxKind.Explosion:
                limit = ExplosionBudget;
                count = ref explosionCount;
                break;
            case CombatVfxKind.DeathExplosion:
                limit = DeathBudget;
                count = ref deathCount;
                break;
            case CombatVfxKind.Status:
                limit = StatusBudget;
                count = ref statusCount;
                break;
            default:
                return false;
        }

        int hardLimit = priority >= 3 ? limit + limit / 2 : limit;
        if (count >= hardLimit)
        {
            return false;
        }

        count++;
        return true;
    }

    private bool ShouldCull(in CombatVfxRequest request, Camera camera)
    {
        if (camera == null || request.priority >= 3)
        {
            return false;
        }

        float3 center = (request.start + request.end) * 0.5f;
        Vector3 viewport = camera.WorldToViewportPoint(new Vector3(center.x, center.y, center.z));
        const float margin = 0.14f;
        return viewport.x < -margin || viewport.x > 1f + margin || viewport.y < -margin || viewport.y > 1f + margin;
    }

    private SpriteVisual AcquireSprite()
    {
        if (spritePool.Count > 0)
        {
            return spritePool.Dequeue();
        }

        return CreateSpriteVisual();
    }

    private SpriteVisual CreateSpriteVisual()
    {
        GameObject gameObject = new GameObject("Combat VFX Sprite");
        gameObject.transform.SetParent(root.transform, false);

        SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = softCircleSprite;
        renderer.material = spriteMaterial;
        renderer.sortingOrder = 950;
        renderer.enabled = false;

        return new SpriteVisual
        {
            GameObject = gameObject,
            Renderer = renderer,
        };
    }

    private LineVisual AcquireLine()
    {
        if (linePool.Count > 0)
        {
            return linePool.Dequeue();
        }

        return CreateLineVisual();
    }

    private LineVisual CreateLineVisual()
    {
        GameObject gameObject = new GameObject("Combat VFX Line");
        gameObject.transform.SetParent(root.transform, false);

        LineRenderer line = gameObject.AddComponent<LineRenderer>();
        line.material = lineMaterial;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 2;
        line.numCornerVertices = 1;
        line.sortingOrder = 960;

        return new LineVisual
        {
            GameObject = gameObject,
            Line = line,
        };
    }

    private void ReleaseSpriteAt(int activeIndex)
    {
        SpriteVisual visual = activeSprites[activeIndex];
        visual.Renderer.enabled = false;
        spritePool.Enqueue(visual);
        int lastIndex = activeSprites.Count - 1;
        activeSprites[activeIndex] = activeSprites[lastIndex];
        activeSprites.RemoveAt(lastIndex);
    }

    private void ReleaseLineAt(int activeIndex)
    {
        LineVisual visual = activeLines[activeIndex];
        visual.Line.enabled = false;
        linePool.Enqueue(visual);
        int lastIndex = activeLines.Count - 1;
        activeLines[activeIndex] = activeLines[lastIndex];
        activeLines.RemoveAt(lastIndex);
    }

    private void EnsureRuntimeObjects()
    {
        if (root != null)
        {
            return;
        }

        root = new GameObject("Combat VFX Runtime Pool");
        Object.DontDestroyOnLoad(root);

        spriteMaterial = CreateMaterial();
        lineMaterial = CreateMaterial();
        softCircleSprite = CreateSoftCircleSprite();
        PrewarmPools();
    }

    private void PrewarmPools()
    {
        while (spritePool.Count < PrewarmSpriteCount)
        {
            spritePool.Enqueue(CreateSpriteVisual());
        }

        while (linePool.Count < PrewarmLineCount)
        {
            linePool.Enqueue(CreateLineVisual());
        }
    }

    private Camera ResolveCamera(float now)
    {
        if (cachedCamera != null && now < nextCameraRefreshTime)
        {
            return cachedCamera;
        }

        nextCameraRefreshTime = now + CameraRefreshInterval;
        cachedCamera = Camera.main;
        return cachedCamera;
    }

    private static Material CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        Material material = new Material(shader);
        material.name = "CombatVfxRuntimeMaterial";
        return material;
    }

    private static Sprite CreateSoftCircleSprite()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "CombatVfxSoftCircle";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - distance);
                alpha = alpha * alpha;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static float ResolveLifetime(in CombatVfxRequest request, float fallback)
    {
        return request.lifetime > 0.001f ? request.lifetime : fallback;
    }

    private static float DirectionToAngle(float2 direction)
    {
        float2 normalized = math.normalizesafe(direction, new float2(0f, 1f));
        return math.degrees(math.atan2(normalized.y, normalized.x)) - 90f;
    }

    private static bool IsBallisticTracerStyle(CombatVfxStyle style)
    {
        return style == CombatVfxStyle.BallisticSmall ||
               style == CombatVfxStyle.BallisticMedium ||
               style == CombatVfxStyle.BallisticHeavy ||
               style == CombatVfxStyle.Flak;
    }

    private static float ResolveTracerBoltLength(CombatVfxStyle style)
    {
        switch (style)
        {
            case CombatVfxStyle.BallisticHeavy:
                return 0.72f;
            case CombatVfxStyle.Flak:
                return 0.58f;
            case CombatVfxStyle.BallisticMedium:
                return 0.5f;
            default:
                return 0.34f;
        }
    }

    private static Color ResolveColor(CombatVfxStyle style, Faction faction, float alpha)
    {
        bool enemy = faction == Faction.Enemy;
        switch (style)
        {
            case CombatVfxStyle.LaserThin:
                return enemy ? new Color(1f, 0.18f, 0.36f, alpha) : new Color(0.25f, 0.82f, 1f, alpha);
            case CombatVfxStyle.RailgunHeavy:
                return enemy ? new Color(1f, 0.45f, 0.65f, alpha) : new Color(0.72f, 0.95f, 1f, alpha);
            case CombatVfxStyle.Repair:
                return new Color(0.28f, 1f, 0.58f, alpha);
            case CombatVfxStyle.Emp:
                return new Color(0.45f, 0.45f, 1f, alpha);
            case CombatVfxStyle.RocketSmall:
                return new Color(1f, 0.44f, 0.12f, alpha);
            case CombatVfxStyle.MissileMedium:
                return new Color(1f, 0.62f, 0.16f, alpha);
            case CombatVfxStyle.TorpedoHeavy:
                return new Color(0.2f, 0.66f, 1f, alpha);
            case CombatVfxStyle.ShipDeathSmall:
            case CombatVfxStyle.ShipDeathMedium:
            case CombatVfxStyle.ShipDeathBig:
                return new Color(1f, 0.56f, 0.16f, alpha);
            case CombatVfxStyle.Flak:
                return new Color(1f, 0.74f, 0.28f, alpha);
            default:
                return enemy ? new Color(1f, 0.34f, 0.14f, alpha) : new Color(1f, 0.9f, 0.42f, alpha);
        }
    }

    private void ResetFrameBudgets()
    {
        muzzleCount = 0;
        tracerCount = 0;
        beamCount = 0;
        impactCount = 0;
        explosionCount = 0;
        deathCount = 0;
        statusCount = 0;
    }

    private sealed class SpriteVisual
    {
        public GameObject GameObject;
        public SpriteRenderer Renderer;
        public float StartTime;
        public float EndTime;
        public Vector2 StartScale;
        public Vector2 EndScale;
        public Color StartColor;
    }

    private sealed class LineVisual
    {
        public GameObject GameObject;
        public LineRenderer Line;
        public float StartTime;
        public float EndTime;
        public float StartWidth;
        public bool Taper;
        public Color StartColor;
    }
}
