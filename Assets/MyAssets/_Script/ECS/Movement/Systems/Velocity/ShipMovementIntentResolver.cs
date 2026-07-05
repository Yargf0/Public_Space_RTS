using Unity.Mathematics;

public static class ShipMovementIntentResolver
{
    public static float2 ResolveDirectCombatVelocity(float2 position, float2 fightTarget, float flowFieldWeight)
    {
        return ResolveDirectCombatVelocity(position, fightTarget, flowFieldWeight, 0.15f);
    }

    public static float2 ResolveDirectCombatVelocity(float2 position, float2 fightTarget, float flowFieldWeight, float stopDistance)
    {
        float2 toTarget = fightTarget - position;
        if (!math.isfinite(toTarget.x) || !math.isfinite(toTarget.y))
            return float2.zero;

        float stopDistanceSq = math.max(0.0001f, stopDistance * stopDistance);
        if (math.lengthsq(toTarget) < stopDistanceSq)
            return float2.zero;

        return math.normalizesafe(toTarget) * flowFieldWeight;
    }

    public static float2 ResolveFollowingVelocity(
        float2 position,
        float2 followTargetPos,
        float2 shipHalfExtents,
        float maxSpeed,
        float flowFieldWeight,
        out bool shouldZeroCurrentVelocity)
    {
        shouldZeroCurrentVelocity = false;

        float dist = math.distance(position, followTargetPos);
        float2 halfExtents = math.abs(shipHalfExtents);
        float myRadius = math.max(halfExtents.x, halfExtents.y);
        float stopDistance = math.max(1.25f, myRadius * 0.5f);
        float slowDistance = math.max(stopDistance + 2f, stopDistance + maxSpeed * 0.35f);

        if (dist <= stopDistance)
        {
            shouldZeroCurrentVelocity = true;
            return float2.zero;
        }

        float2 moveDir = math.normalizesafe(followTargetPos - position);
        float slowFactor = math.saturate((dist - stopDistance) / (slowDistance - stopDistance));
        return moveDir * flowFieldWeight * slowFactor;
    }
}
