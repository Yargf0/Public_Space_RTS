using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// optional authoring for combat movement. don't put with ShipBaseAuthoring - duplicate baking
class FightLogicAuthoring : MonoBehaviour
{
    [Header("Pattern")]
    public FightLogicType MovementType;
    public OrbitDirection orbitDirection = OrbitDirection.Right;

    [Header("Distance")]
    [Tooltip("Base combat distance. 0 = external systems can fill it from weapon range.")]
    public float idealDistance = 0f;

    [Header("Target Size Patterns")]
    public bool useTargetSizePatterns = false;
    public FightLogicType smallTargetPattern = FightLogicType.Dogfight;
    public FightLogicType mediumTargetPattern = FightLogicType.Orbit;
    public FightLogicType bigTargetPattern = FightLogicType.AttackRun;
    [Range(0.2f, 2f)] public float smallTargetIdealDistanceMultiplier = 0.85f;
    [Range(0.2f, 2f)] public float mediumTargetIdealDistanceMultiplier = 1f;
    [Range(0.2f, 2.5f)] public float bigTargetIdealDistanceMultiplier = 1.15f;

    [Header("HoldDistance")]
    [Range(0.5f, 1f)] public float holdHysteresisInner = 0.88f;
    [Range(1f, 1.6f)] public float holdHysteresisOuter = 1.18f;
    [Range(0f, 0.5f)] public float holdDriftSpeed = 0.15f;
    [Range(0.5f, 6f)] public float holdDriftPeriod = 2.5f;

    [Header("Orbit")]
    [Range(0f, 0.25f)] public float orbitRadiusJitter = 0.08f;
    [Range(0.5f, 6f)] public float orbitJitterPeriod = 1.8f;
    [Range(0f, 0.5f)] public float orbitSpeedJitter = 0.25f;

    [Header("AttackRun / InterceptorPass")]
    [Range(0.2f, 0.8f)] public float attackRunFireRange = 0.55f;
    [Range(0.3f, 4f)] public float attackRunFiringDuration = 1.2f;
    [Range(0.5f, 4f)] public float attackRunBreakawayDuration = 1.5f;
    [Range(0.5f, 5f)] public float attackRunRepositionDuration = 2.0f;
    [Range(0f, 180f)] public float attackRunRepositionSpread = 70f;
    [Range(1f, 3f)] public float attackRunRepositionDistance = 1.6f;

    [Header("InterceptorPass (lead prediction)")]
    [Tooltip("Minimum own speed used for lead prediction. Prevents huge lead points on very slow ships.")]
    [Range(1f, 50f)] public float interceptorMinOwnSpeedForLead = 5f;
    [Tooltip("Maximum lead time. Keeps fast targets from producing extreme intercept points.")]
    [Range(0.1f, 5f)] public float interceptorMaxLeadTime = 1.5f;

    [Header("MissileAttackRun")]
    [Tooltip("Launch distance multiplier based on idealDistance. Should be lower than retreat distance.")]
    [Range(0.5f, 2f)] public float missileLaunchDistance = 1.0f;
    [Range(0.2f, 5f)] public float missileLaunchDuration = 1.5f;
    [Tooltip("Retreat distance multiplier after launch.")]
    [Range(1.2f, 4f)] public float missileRetreatDistance = 1.8f;
    [Range(0.5f, 5f)] public float missileRetreatDuration = 2.0f;
    [Tooltip("Delay at the retreat point before the next attack cycle.")]
    [Range(0.5f, 8f)] public float missileReloadDuration = 2.5f;

    [Header("Strafe")]
    [Range(0.5f, 3f)] public float strafeLegLength = 1.4f;
    [Range(0.2f, 0.9f)] public float strafeMinDistance = 0.6f;

    [Header("Swarm")]
    [Tooltip("How many formation slots fit on one attack ring around the target.")]
    [Range(2, 32)] public int swarmSlotsPerCircle = 8;
    [Tooltip("Shared rotation speed for the swarm ring. Direction comes from orbitDirection.")]
    [Range(-90f, 90f)] public float swarmRotationDegPerSec = 25f;

    class Baker : Baker<FightLogicAuthoring>
    {
        public override void Bake(FightLogicAuthoring a)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new FightLogic
            {
                movementType = a.MovementType,
                orbitDirection = (float)a.orbitDirection,
                idealDistance = a.idealDistance,

                useTargetSizePatterns = a.useTargetSizePatterns ? (byte)1 : (byte)0,
                smallTargetPattern = a.smallTargetPattern,
                mediumTargetPattern = a.mediumTargetPattern,
                bigTargetPattern = a.bigTargetPattern,
                smallTargetIdealDistanceMultiplier = a.smallTargetIdealDistanceMultiplier,
                mediumTargetIdealDistanceMultiplier = a.mediumTargetIdealDistanceMultiplier,
                bigTargetIdealDistanceMultiplier = a.bigTargetIdealDistanceMultiplier,

                holdHysteresisInner = a.holdHysteresisInner,
                holdHysteresisOuter = a.holdHysteresisOuter,
                holdDriftSpeed = a.holdDriftSpeed,
                holdDriftPeriod = math.max(0.1f, a.holdDriftPeriod),

                orbitRadiusJitter = a.orbitRadiusJitter,
                orbitJitterPeriod = math.max(0.1f, a.orbitJitterPeriod),
                orbitSpeedJitter = a.orbitSpeedJitter,

                attackRunFireRange = a.attackRunFireRange,
                attackRunFiringDuration = a.attackRunFiringDuration,
                attackRunBreakawayDuration = a.attackRunBreakawayDuration,
                attackRunRepositionDuration = a.attackRunRepositionDuration,
                attackRunRepositionSpread = math.radians(a.attackRunRepositionSpread),
                attackRunRepositionDistance = a.attackRunRepositionDistance,

                interceptorMinOwnSpeedForLead = math.max(0.1f, a.interceptorMinOwnSpeedForLead),
                interceptorMaxLeadTime = math.max(0.05f, a.interceptorMaxLeadTime),

                missileLaunchDistance = math.max(0.1f, a.missileLaunchDistance),
                missileLaunchDuration = math.max(0.05f, a.missileLaunchDuration),
                missileRetreatDistance = math.max(a.missileLaunchDistance + 0.1f, a.missileRetreatDistance),
                missileRetreatDuration = math.max(0.05f, a.missileRetreatDuration),
                missileReloadDuration = math.max(0f, a.missileReloadDuration),

                strafeLegLength = a.strafeLegLength,
                strafeMinDistance = a.strafeMinDistance,

                swarmSlotsPerCircle = math.max(2, a.swarmSlotsPerCircle),
                swarmRotationDegPerSec = a.swarmRotationDegPerSec,
            });

            AddComponent(entity, new FightPatternState
            {
                activePattern = (FightLogicType)byte.MaxValue,
                rngState = 0u,
            });
        }
    }
}

public struct FightLogic : IComponentData
{
    public FightLogicType movementType;
    public float idealDistance;
    public float orbitDirection;

    // pattern override by target size
    public byte useTargetSizePatterns;
    public FightLogicType smallTargetPattern;
    public FightLogicType mediumTargetPattern;
    public FightLogicType bigTargetPattern;
    public float smallTargetIdealDistanceMultiplier;
    public float mediumTargetIdealDistanceMultiplier;
    public float bigTargetIdealDistanceMultiplier;

    // HoldDistance
    public float holdHysteresisInner;
    public float holdHysteresisOuter;
    public float holdDriftSpeed;
    public float holdDriftPeriod;

    // Orbit
    public float orbitRadiusJitter;
    public float orbitJitterPeriod;
    public float orbitSpeedJitter;

    // AttackRun / InterceptorPass phase settings
    public float attackRunFireRange;
    public float attackRunFiringDuration;
    public float attackRunBreakawayDuration;
    public float attackRunRepositionDuration;
    public float attackRunRepositionSpread;
    public float attackRunRepositionDistance;

    // lead prediction for InterceptorPass
    public float interceptorMinOwnSpeedForLead;
    public float interceptorMaxLeadTime;

    // MissileAttackRun
    public float missileLaunchDistance;
    public float missileLaunchDuration;
    public float missileRetreatDistance;
    public float missileRetreatDuration;
    public float missileReloadDuration;

    // Strafe
    public float strafeLegLength;
    public float strafeMinDistance;

    // Swarm
    public int swarmSlotsPerCircle;
    public float swarmRotationDegPerSec;
}

public struct FightPatternState : IComponentData
{
    public FightLogicType activePattern;
    public byte phase;
    public float phaseTimer;
    public sbyte strafeDirection;
    public float strafeProgress;
    public float radiusOffset;
    public float radiusOffsetTimer;
    public float driftPhase;
    public byte inPosition;
    // cached direction or point for pattern
    public float2 cachedPoint;
    public uint rngState;
}

public enum OrbitDirection : sbyte
{
    Left = -1,
    Right = 1
}
