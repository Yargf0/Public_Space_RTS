using Unity.Entities;
using Unity.Mathematics;

public static class WeaponTargetDistributionUtility
{
    public static float ScoreCandidate(
        Entity candidate,
        Entity currentTarget,
        float distanceSq,
        bool isPriorityTarget,
        WeaponTargetPressure pressure,
        WeaponTargetDistributionSettings settings)
    {
        float distance = math.sqrt(math.max(0f, distanceSq));
        float score = 1000f;
        score -= distance * settings.distanceWeight;

        if (isPriorityTarget)
        {
            score += settings.priorityBonus;
        }

        if (candidate == currentTarget)
        {
            score += settings.currentTargetStickiness;
        }

        score -= pressure.assignedTurrets * settings.pressurePenaltyPerTurret;
        score -= pressure.assignedDps * settings.pressurePenaltyPerDps;
        return score;
    }

    public static float ComputeRetargetDelay(
        Entity weaponEntity,
        WeaponTargetDistributionSettings settings)
    {
        float minDelay = math.max(0.01f, settings.retargetIntervalMin);
        float maxDelay = math.max(minDelay, settings.retargetIntervalMax);
        float seed = math.frac(math.sin((weaponEntity.Index + 1) * 12.9898f) * 43758.5453f);
        return math.lerp(minDelay, maxDelay, seed);
    }
}
