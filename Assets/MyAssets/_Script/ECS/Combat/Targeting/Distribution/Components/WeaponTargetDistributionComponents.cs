using Unity.Entities;

public struct WeaponTargetAssignment : IComponentData
{
    public Entity targetEntity;
    public Entity ownerEntity;
    public float estimatedDps;
    public byte ownerShipSize;
    public float nextRetargetTime;
}

public struct WeaponTargetDistributionSettings : IComponentData
{
    public float distanceWeight;
    public float priorityBonus;
    public float currentTargetStickiness;
    public float switchScoreThreshold;

    public float pressurePenaltyPerTurret;
    public float pressurePenaltyPerDps;

    public float retargetIntervalMin;
    public float retargetIntervalMax;

    public byte enableDebug;
}

public struct WeaponTargetPressure
{
    public int assignedTurrets;
    public float assignedDps;
}

// tag: weapons on this ship ignore target pressure and can focus same target
public struct DisableWeaponTargetDistribution : IComponentData
{
}

public static class WeaponTargetDistributionDefaults
{
    public static WeaponTargetDistributionSettings Create()
    {
        return new WeaponTargetDistributionSettings
        {
            distanceWeight = 0.05f,
            priorityBonus = 50f,
            currentTargetStickiness = 20f,
            switchScoreThreshold = 10f,

            pressurePenaltyPerTurret = 2f,
            pressurePenaltyPerDps = 0.05f,

            retargetIntervalMin = 0.35f,
            retargetIntervalMax = 0.9f,

            enableDebug = 0,
        };
    }
}
