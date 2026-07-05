using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public static class WeaponAimUtility
{
    private const float Epsilon = 0.001f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 ResolveProjectileDirection(
        float2 shooterPosition,
        float2 targetPosition,
        float2 shooterVelocity,
        float2 targetVelocity,
        float projectileSpeed,
        float2 fallbackDirection)
    {
        float2 fallback = math.normalizesafe(fallbackDirection, new float2(0f, 1f));
        float2 directDirection = math.normalizesafe(targetPosition - shooterPosition, fallback);

        if (projectileSpeed <= Epsilon)
        {
            return directDirection;
        }

        float effectiveProjectileSpeed = projectileSpeed + ResolvePositiveInheritedSpeed(shooterVelocity, directDirection);
        float interceptTime = ResolveProjectileInterceptTime(shooterPosition, targetPosition, targetVelocity, effectiveProjectileSpeed);
        float2 aimVector = targetPosition - shooterPosition + targetVelocity * interceptTime;

        return math.normalizesafe(aimVector, directDirection);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 ResolveProjectileAimPoint(
        float3 shooterPosition,
        float3 targetPosition,
        float2 shooterVelocity,
        float2 targetVelocity,
        float projectileSpeed,
        float2 fallbackDirection)
    {
        float2 fallback = math.normalizesafe(fallbackDirection, new float2(0f, 1f));
        float2 directDirection = math.normalizesafe(targetPosition.xy - shooterPosition.xy, fallback);

        if (projectileSpeed <= Epsilon)
        {
            return targetPosition;
        }

        float effectiveProjectileSpeed = projectileSpeed + ResolvePositiveInheritedSpeed(shooterVelocity, directDirection);
        float interceptTime = ResolveProjectileInterceptTime(shooterPosition.xy, targetPosition.xy, targetVelocity, effectiveProjectileSpeed);
        float2 aimPoint = targetPosition.xy + targetVelocity * interceptTime;

        return new float3(aimPoint.x, aimPoint.y, targetPosition.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 ResolvePositiveInheritedVelocity(float2 ownerVelocity, float2 projectileDirection)
    {
        float2 direction = math.normalizesafe(projectileDirection, new float2(0f, 1f));
        float inheritedSpeed = ResolvePositiveInheritedSpeed(ownerVelocity, direction);
        return direction * inheritedSpeed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ResolvePositiveInheritedSpeed(float2 ownerVelocity, float2 projectileDirection)
    {
        float2 direction = math.normalizesafe(projectileDirection, new float2(0f, 1f));
        return math.max(0f, math.dot(ownerVelocity, direction));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveProjectileInterceptTime(
        float2 shooterPosition,
        float2 targetPosition,
        float2 targetVelocity,
        float projectileSpeed)
    {
        float2 toTarget = targetPosition - shooterPosition;
        float distanceSq = math.lengthsq(toTarget);

        if (distanceSq <= Epsilon * Epsilon || projectileSpeed <= Epsilon)
        {
            return 0f;
        }

        float projectileSpeedSq = projectileSpeed * projectileSpeed;
        float a = math.dot(targetVelocity, targetVelocity) - projectileSpeedSq;
        float b = 2f * math.dot(toTarget, targetVelocity);
        float c = distanceSq;
        float interceptTime = 0f;

        if (math.abs(a) <= 0.0001f)
        {
            if (math.abs(b) > 0.0001f)
            {
                interceptTime = -c / b;
            }
        }
        else
        {
            float discriminant = b * b - 4f * a * c;

            if (discriminant >= 0f)
            {
                float sqrtDiscriminant = math.sqrt(discriminant);
                float invDenominator = 1f / (2f * a);
                float t0 = (-b - sqrtDiscriminant) * invDenominator;
                float t1 = (-b + sqrtDiscriminant) * invDenominator;

                if (t0 > Epsilon && t1 > Epsilon)
                {
                    interceptTime = math.min(t0, t1);
                }
                else if (t0 > Epsilon)
                {
                    interceptTime = t0;
                }
                else if (t1 > Epsilon)
                {
                    interceptTime = t1;
                }
            }
        }

        if (interceptTime <= Epsilon)
        {
            interceptTime = math.sqrt(distanceSq) / projectileSpeed;
        }

        return interceptTime;
    }
}
