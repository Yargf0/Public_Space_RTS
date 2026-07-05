using Unity.Mathematics;

public static class VelocitySteeringUtility
{
    public static float2 CalculateFinalVelocity(
        float2 currentVelocity,
        float2 potentialVelocity,
        float maxSpeed,
        float acceleration,
        float deltaTime)
    {
        return CalculateFinalVelocity(
            currentVelocity,
            potentialVelocity,
            maxSpeed,
            acceleration,
            deltaTime,
            PathfindingSizeClass.Medium,
            ShipState.Idle);
    }

    public static float2 CalculateFinalVelocity(
        float2 currentVelocity,
        float2 potentialVelocity,
        float maxSpeed,
        float acceleration,
        float deltaTime,
        PathfindingSizeClass sizeClass,
        ShipState currentState)
    {
        if (!math.isfinite(potentialVelocity.x) || !math.isfinite(potentialVelocity.y))
            return float2.zero;

        float potentialLength = math.length(potentialVelocity);
        if (potentialLength < 0.0001f || maxSpeed <= 0f)
            return float2.zero;

        if (!math.isfinite(currentVelocity.x) || !math.isfinite(currentVelocity.y))
            currentVelocity = float2.zero;

        float2 desiredDirection = potentialVelocity / potentialLength;

        // Don't normalize intent to maxSpeed, small intent is on purpose (follow, final approach).
        float desiredSpeed = maxSpeed * math.saturate(potentialLength);
        float2 desiredVelocity = desiredDirection * desiredSpeed;

        float currentSpeedSq = math.lengthsq(currentVelocity);
        float currentSpeed = math.sqrt(currentSpeedSq);
        float turnDot = 1f;
        if (currentSpeed > 0.0001f)
            turnDot = math.dot(currentVelocity / currentSpeed, desiredDirection);

        float turn01 = math.saturate((0.5f - turnDot) / 1.5f);
        float sizeResponsiveness = GetSizeResponsiveness(sizeClass);
        float combatResponsiveness = currentState == ShipState.InCombat ? 1.25f : 1f;
        float turnAssist = math.lerp(1f, 2.75f, turn01);
        float effectiveAcceleration = math.max(0f, acceleration) * sizeResponsiveness * combatResponsiveness * turnAssist;

        // Cut side velocity faster in sharp turns so fighters don't slide like big ships.
        if (currentSpeed > 0.0001f && turn01 > 0.001f && effectiveAcceleration > 0f)
        {
            float forwardSpeed = math.max(0f, math.dot(currentVelocity, desiredDirection));
            float2 forwardVelocity = desiredDirection * forwardSpeed;
            float2 lateralVelocity = currentVelocity - forwardVelocity;
            float lateralDamping = math.saturate(turn01 * effectiveAcceleration * deltaTime / math.max(currentSpeed, 0.0001f) * 2f);
            currentVelocity -= lateralVelocity * lateralDamping;
        }

        float2 delta = desiredVelocity - currentVelocity;
        float maxDelta = effectiveAcceleration * deltaTime;

        if (math.lengthsq(delta) > maxDelta * maxDelta && maxDelta > 0f)
            delta = math.normalizesafe(delta) * maxDelta;

        float2 finalVelocity = currentVelocity + delta;
        if (!math.isfinite(finalVelocity.x) || !math.isfinite(finalVelocity.y))
            return float2.zero;

        float maxSpeedSq = maxSpeed * maxSpeed;
        if (math.lengthsq(finalVelocity) > maxSpeedSq)
            finalVelocity = math.normalize(finalVelocity) * maxSpeed;

        return finalVelocity;
    }

    private static float GetSizeResponsiveness(PathfindingSizeClass sizeClass)
    {
        switch (sizeClass)
        {
            case PathfindingSizeClass.Small:
                return 3.5f;
            case PathfindingSizeClass.Large:
                return 1f;
            case PathfindingSizeClass.Medium:
            default:
                return 1.75f;
        }
    }
}
