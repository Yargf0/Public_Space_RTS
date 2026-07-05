using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

public static class CombatVfxRequestUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnqueueMuzzleFlash(
        ref EntityCommandBuffer ecb,
        in WeaponFireRequest request,
        in WeaponProfileBlob profile)
    {
        CombatVfxStyle style = ResolveWeaponStyle(in profile);
        float2 direction = math.normalizesafe(request.direction, new float2(0f, 1f));
        float3 start = ToEffectsPosition(request.spawnPosition);

        CreateRequest(ref ecb, new CombatVfxRequest
        {
            kind = (byte)CombatVfxKind.MuzzleFlash,
            style = (byte)style,
            priority = ResolveMuzzlePriority(style),
            faction = request.ownerFaction,
            start = start,
            end = start + new float3(direction.x, direction.y, 0f),
            direction = direction,
            radius = ResolveMuzzleRadius(style),
            width = 0f,
            lifetime = ResolveMuzzleLifetime(style),
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnqueueProjectileTracer(
        ref EntityCommandBuffer ecb,
        in WeaponFireRequest request,
        in WeaponProfileBlob profile)
    {
        if ((WeaponRequestKind)profile.requestKind != WeaponRequestKind.Ballistic)
        {
            return;
        }

        float2 direction = math.normalizesafe(request.direction, new float2(0f, 1f));
        float length = math.clamp(profile.projectileSpeed * 0.035f, 0.9f, 4.5f);
        CombatVfxStyle style = ResolveWeaponStyle(in profile);
        float3 start = ToProjectilePosition(request.spawnPosition);

        CreateRequest(ref ecb, new CombatVfxRequest
        {
            kind = (byte)CombatVfxKind.ProjectileTracer,
            style = (byte)style,
            priority = ResolveTracerPriority(style),
            faction = request.ownerFaction,
            start = start,
            end = start + new float3(direction.x * length, direction.y * length, 0f),
            direction = direction,
            radius = 0f,
            width = ResolveTracerWidth(style),
            lifetime = 0.045f,
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnqueueBeam(
        ref EntityCommandBuffer ecb,
        in WeaponFireRequest request,
        in WeaponProfileBlob profile,
        float2 impactPosition)
    {
        if ((WeaponRequestKind)profile.requestKind != WeaponRequestKind.Hitscan)
        {
            return;
        }

        float2 direction = math.normalizesafe(request.direction, new float2(0f, 1f));
        CombatVfxStyle style = ResolveWeaponStyle(in profile);
        float3 start = ToProjectilePosition(request.spawnPosition);
        float3 end = ToProjectilePosition(new float3(impactPosition.x, impactPosition.y, GameConstants.ProjectileZ));

        CreateRequest(ref ecb, new CombatVfxRequest
        {
            kind = (byte)CombatVfxKind.Beam,
            style = (byte)style,
            priority = style == CombatVfxStyle.RailgunHeavy ? (byte)3 : (byte)2,
            faction = request.ownerFaction,
            start = start,
            end = end,
            direction = direction,
            radius = 0f,
            width = math.max(profile.hitscanBeamWidth, ResolveBeamWidth(style)),
            lifetime = math.clamp(profile.lifetime, 0.045f, 0.16f),
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnqueueImpactOrExplosion(
        ref EntityCommandBuffer ecb,
        byte payloadKind,
        float splashRadius,
        byte statusEffectKind,
        float2 impactPosition,
        Faction sourceFaction)
    {
        CombatVfxStyle style = ResolveImpactStyle(payloadKind, splashRadius, statusEffectKind);
        CombatVfxKind kind = IsExplosionPayload(payloadKind, splashRadius)
            ? CombatVfxKind.Explosion
            : CombatVfxKind.Impact;
        float radius = kind == CombatVfxKind.Explosion
            ? math.max(splashRadius, ResolveExplosionMinRadius(style))
            : 0.35f;

        CreateRequest(ref ecb, new CombatVfxRequest
        {
            kind = (byte)kind,
            style = (byte)style,
            priority = kind == CombatVfxKind.Explosion ? (byte)3 : (byte)1,
            faction = sourceFaction,
            start = ToEffectsPosition(new float3(impactPosition.x, impactPosition.y, GameConstants.EffectsZ)),
            end = ToEffectsPosition(new float3(impactPosition.x, impactPosition.y, GameConstants.EffectsZ)),
            direction = new float2(0f, 1f),
            radius = radius,
            width = 0f,
            lifetime = kind == CombatVfxKind.Explosion ? ResolveExplosionLifetime(style) : 0.14f,
        });

        if ((WeaponStatusEffectKind)statusEffectKind == WeaponStatusEffectKind.Emp)
        {
            CreateRequest(ref ecb, new CombatVfxRequest
            {
                kind = (byte)CombatVfxKind.Status,
                style = (byte)CombatVfxStyle.Emp,
                priority = 2,
                faction = sourceFaction,
                start = ToEffectsPosition(new float3(impactPosition.x, impactPosition.y, GameConstants.EffectsZ)),
                end = ToEffectsPosition(new float3(impactPosition.x, impactPosition.y, GameConstants.EffectsZ)),
                direction = new float2(0f, 1f),
                radius = math.max(splashRadius, 0.8f),
                width = 0f,
                lifetime = 0.45f,
            });
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnqueueDeathExplosion(
        ref EntityCommandBuffer ecb,
        float3 worldPosition,
        byte shipSize,
        Faction faction)
    {
        CombatVfxStyle style = ResolveDeathStyle(shipSize);
        float radius = style == CombatVfxStyle.ShipDeathBig
            ? 3.4f
            : style == CombatVfxStyle.ShipDeathMedium
                ? 2.1f
                : 1.2f;

        CreateRequest(ref ecb, new CombatVfxRequest
        {
            kind = (byte)CombatVfxKind.DeathExplosion,
            style = (byte)style,
            priority = style == CombatVfxStyle.ShipDeathBig ? (byte)4 : (byte)3,
            faction = faction,
            start = ToEffectsPosition(worldPosition),
            end = ToEffectsPosition(worldPosition),
            direction = new float2(0f, 1f),
            radius = radius,
            width = 0f,
            lifetime = style == CombatVfxStyle.ShipDeathBig ? 0.8f : 0.55f,
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CreateRequest(ref EntityCommandBuffer ecb, in CombatVfxRequest request)
    {
        Entity requestEntity = ecb.CreateEntity();
        ecb.AddComponent(requestEntity, request);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CombatVfxStyle ResolveWeaponStyle(in WeaponProfileBlob profile)
    {
        WeaponRequestKind requestKind = (WeaponRequestKind)profile.requestKind;

        if (requestKind == WeaponRequestKind.Hitscan)
        {
            return profile.hitscanBeamWidth >= 0.35f || profile.damageAmount >= 25f
                ? CombatVfxStyle.RailgunHeavy
                : CombatVfxStyle.LaserThin;
        }

        if (requestKind == WeaponRequestKind.Rocket)
        {
            if (!profile.turnAfterLaunch || profile.splashRadius >= 3.25f && profile.projectileSpeed <= 18f)
            {
                return CombatVfxStyle.TorpedoHeavy;
            }

            if (profile.splashRadius >= 2f || profile.rocketLaunchScatterAngle > 0.001f)
            {
                return CombatVfxStyle.MissileMedium;
            }

            return CombatVfxStyle.RocketSmall;
        }

        if (IsExplosionPayload(profile.payloadKind, profile.splashRadius))
        {
            return CombatVfxStyle.Flak;
        }

        if (profile.damageAmount >= 15f || profile.firePattern == (byte)WeaponFirePattern.SimultaneousHardpoints)
        {
            return CombatVfxStyle.BallisticHeavy;
        }

        return profile.damageAmount >= 6f
            ? CombatVfxStyle.BallisticMedium
            : CombatVfxStyle.BallisticSmall;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CombatVfxStyle ResolveImpactStyle(byte payloadKind, float splashRadius, byte statusEffectKind)
    {
        if ((WeaponStatusEffectKind)statusEffectKind == WeaponStatusEffectKind.Emp)
        {
            return CombatVfxStyle.Emp;
        }

        if (IsExplosionPayload(payloadKind, splashRadius))
        {
            if (splashRadius >= 3.25f)
            {
                return CombatVfxStyle.TorpedoHeavy;
            }

            if (splashRadius >= 1.75f)
            {
                return CombatVfxStyle.MissileMedium;
            }

            return CombatVfxStyle.RocketSmall;
        }

        return CombatVfxStyle.BallisticMedium;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CombatVfxStyle ResolveDeathStyle(byte shipSize)
    {
        ShipSize size = (ShipSize)shipSize;
        if ((size & (ShipSize.Big | ShipSize.RocketBig)) != 0)
        {
            return CombatVfxStyle.ShipDeathBig;
        }

        if ((size & ShipSize.Medium) != 0)
        {
            return CombatVfxStyle.ShipDeathMedium;
        }

        return CombatVfxStyle.ShipDeathSmall;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExplosionPayload(byte payloadKind, float splashRadius)
    {
        return splashRadius > 0.001f &&
               ((WeaponPayloadKind)payloadKind == WeaponPayloadKind.Splash ||
                (WeaponPayloadKind)payloadKind == WeaponPayloadKind.DirectPlusSplash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveMuzzleRadius(CombatVfxStyle style)
    {
        switch (style)
        {
            case CombatVfxStyle.BallisticHeavy:
            case CombatVfxStyle.RailgunHeavy:
                return 0.85f;
            case CombatVfxStyle.Flak:
            case CombatVfxStyle.MissileMedium:
            case CombatVfxStyle.TorpedoHeavy:
                return 0.7f;
            case CombatVfxStyle.RocketSmall:
            case CombatVfxStyle.BallisticMedium:
                return 0.52f;
            default:
                return 0.36f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveMuzzleLifetime(CombatVfxStyle style)
    {
        return style == CombatVfxStyle.RailgunHeavy || style == CombatVfxStyle.TorpedoHeavy
            ? 0.1f
            : 0.065f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ResolveMuzzlePriority(CombatVfxStyle style)
    {
        return style == CombatVfxStyle.BallisticSmall ? (byte)1 : (byte)2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ResolveTracerPriority(CombatVfxStyle style)
    {
        return style == CombatVfxStyle.BallisticSmall ? (byte)0 : (byte)1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveTracerWidth(CombatVfxStyle style)
    {
        switch (style)
        {
            case CombatVfxStyle.BallisticHeavy:
            case CombatVfxStyle.Flak:
                return 0.12f;
            case CombatVfxStyle.BallisticMedium:
                return 0.075f;
            default:
                return 0.045f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveBeamWidth(CombatVfxStyle style)
    {
        switch (style)
        {
            case CombatVfxStyle.RailgunHeavy:
                return 0.5f;
            case CombatVfxStyle.Repair:
                return 0.13f;
            default:
                return 0.1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveExplosionMinRadius(CombatVfxStyle style)
    {
        switch (style)
        {
            case CombatVfxStyle.TorpedoHeavy:
                return 1.45f;
            case CombatVfxStyle.MissileMedium:
                return 0.95f;
            case CombatVfxStyle.RocketSmall:
                return 0.65f;
            default:
                return 0.55f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveExplosionLifetime(CombatVfxStyle style)
    {
        switch (style)
        {
            case CombatVfxStyle.TorpedoHeavy:
                return 0.62f;
            case CombatVfxStyle.MissileMedium:
                return 0.48f;
            case CombatVfxStyle.RocketSmall:
                return 0.34f;
            default:
                return 0.42f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float3 ToProjectilePosition(float3 position)
    {
        position.z = GameConstants.ProjectileZ;
        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float3 ToEffectsPosition(float3 position)
    {
        position.z = GameConstants.EffectsZ;
        return position;
    }
}
