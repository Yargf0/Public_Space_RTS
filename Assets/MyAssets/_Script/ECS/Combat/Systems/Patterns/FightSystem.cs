using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// picks point where ship should fly in fight
// Each pattern writes UnitMover.fightTarget, speed is handled by movement systems.
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ShipStateChangeSystem))]
[UpdateAfter(typeof(LastKnownTargetBehaviorSystem))]
[UpdateBefore(typeof(SetTotalVelocitySystem))]
partial struct FightSystem : ISystem
{
    // Cached lookups for Burst.
    private ComponentLookup<Unit> unitLookup;
    private ComponentLookup<Velocity> velocityLookup;
    private ComponentLookup<ShipSquadRef> shipSquadRefLookup;
    private ComponentLookup<WeaponShipSummaryInitialized> weaponSummaryInitializedLookup;

    // Phases for AttackRun / InterceptorPass.
    private const byte PhaseApproach = 0;
    private const byte PhaseFiring = 1;
    private const byte PhaseBreakaway = 2;
    private const byte PhaseReposition = 3;

    // Phases for MissileAttackRun.
    private const byte PhaseMissileApproach = 0;
    private const byte PhaseMissileLaunch = 1;
    private const byte PhaseMissileRetreat = 2;
    private const byte PhaseMissileReload = 3;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        unitLookup = state.GetComponentLookup<Unit>(true);
        velocityLookup = state.GetComponentLookup<Velocity>(true);
        shipSquadRefLookup = state.GetComponentLookup<ShipSquadRef>(true);
        weaponSummaryInitializedLookup = state.GetComponentLookup<WeaponShipSummaryInitialized>(true);

        state.RequireForUpdate<FightPatternState>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        float elapsed = (float)SystemAPI.Time.ElapsedTime;

        unitLookup.Update(ref state);
        velocityLookup.Update(ref state);
        shipSquadRefLookup.Update(ref state);
        weaponSummaryInitializedLookup.Update(ref state);

        foreach ((
            RefRO<ShipStateComponent> shipState,
            RefRO<ShipAgro> shipAgro,
            RefRW<UnitMover> unitMover,
            RefRO<LocalTransform> localTransform,
            RefRW<FightLogic> fightLogic,
            RefRW<FightPatternState> patternState,
            Entity entity)
            in SystemAPI.Query<
                RefRO<ShipStateComponent>,
                RefRO<ShipAgro>,
                RefRW<UnitMover>,
                RefRO<LocalTransform>,
                RefRW<FightLogic>,
                RefRW<FightPatternState>>().WithEntityAccess())
        {
            if (shipState.ValueRO.currentState != ShipState.InCombat) continue;
            if (shipAgro.ValueRO.targetEntity == Entity.Null) continue;

            float2 localPos = localTransform.ValueRO.Position.xy;
            bool shouldChaseWithoutWeaponRange =
                fightLogic.ValueRO.idealDistance <= 0f &&
                shipAgro.ValueRO.attackRangeMax <= 0f &&
                weaponSummaryInitializedLookup.HasComponent(entity);

            float idealDist = ResolveUsableIdealDistance(ref fightLogic.ValueRW, in shipAgro.ValueRO);
            if (idealDist <= 0f)
            {
                // ships without weapons (rams) still need point to fly. armed ones wait for range data
                unitMover.ValueRW.fightTarget = shouldChaseWithoutWeaponRange
                    ? shipAgro.ValueRO.targetPosition
                    : localPos;
                continue;
            }

            Entity targetEntity = shipAgro.ValueRO.targetEntity;
            float2 targetPos = shipAgro.ValueRO.targetPosition;
            float2 toTarget = targetPos - localPos;
            float distance = math.length(toTarget);
            float2 toTargetDir = distance > 1e-4f ? toTarget / distance : new float2(1f, 0f);

            // Own velocity is needed for Dogfight and pass patterns.
            float2 ownVelocity = velocityLookup.HasComponent(entity)
                ? velocityLookup[entity].velocity
                : float2.zero;

            FightLogicType desired = ResolveFightPatternAndIdealDistance(
                in fightLogic.ValueRO,
                targetEntity,
                ref unitLookup,
                ref idealDist);

            idealDist = ClampIdealDistanceToWeaponRange(desired, idealDist, in shipAgro.ValueRO);
            if (idealDist <= 0f) continue;

            ref var ps = ref patternState.ValueRW;

            // seed rng per ship so ships don't move same way
            if (ps.rngState == 0u)
            {
                uint seed = HashEntityToRng(entity);
                ps.rngState = seed == 0u ? 1u : seed;
            }
            var rng = new Random(ps.rngState);

            // New pattern = reset phase state.
            if (ps.activePattern != desired)
            {
                ps.activePattern = desired;
                ps.phase = 0;
                ps.phaseTimer = 0f;
                ps.strafeDirection = 0;
                ps.strafeProgress = 0f;
                ps.radiusOffset = 0f;
                ps.radiusOffsetTimer = 0f;
                ps.inPosition = 0;
                ps.driftPhase = rng.NextFloat(0f, math.PI * 2f);
                ps.cachedPoint = float2.zero;
            }

            ps.phaseTimer += dt;

            switch (desired)
            {
                case FightLogicType.HoldDistance:
                    StepHoldDistance(ref unitMover.ValueRW, in fightLogic.ValueRO,
                        ref ps, ref rng, targetPos, toTargetDir, distance, idealDist, dt);
                    break;

                case FightLogicType.CloseAndHold:
                    StepCloseAndHold(ref unitMover.ValueRW,
                        ref ps, localPos, targetPos, toTargetDir, distance, idealDist);
                    break;

                case FightLogicType.Orbit:
                    StepOrbit(ref unitMover.ValueRW, in fightLogic.ValueRO,
                        ref ps, ref rng, localPos, targetPos, distance, idealDist, shipAgro.ValueRO.attackRangeMax, dt, elapsed);
                    break;

                case FightLogicType.AttackRun:
                    StepAttackRun(ref unitMover.ValueRW, in fightLogic.ValueRO, in shipAgro.ValueRO,
                        ref ps, ref rng, localPos, targetPos, toTargetDir, distance);
                    break;

                case FightLogicType.InterceptorPass:
                    StepInterceptorPass(ref unitMover.ValueRW, in fightLogic.ValueRO, in shipAgro.ValueRO,
                        ref ps, ref rng, localPos, targetPos, toTargetDir, distance,
                        targetEntity, ref velocityLookup, ownVelocity);
                    break;

                case FightLogicType.MissileAttackRun:
                    StepMissileAttackRun(ref unitMover.ValueRW, in fightLogic.ValueRO,
                        ref ps, ref rng, localPos, targetPos, toTargetDir, distance, idealDist, shipAgro.ValueRO.attackRangeMax);
                    break;

                case FightLogicType.Strafe:
                    StepStrafe(ref unitMover.ValueRW, in fightLogic.ValueRO,
                        ref ps, ref rng, localPos, targetPos, toTargetDir, distance, idealDist, dt, ownVelocity);
                    break;

                case FightLogicType.Dogfight:
                    StepDogfight(ref unitMover.ValueRW, in fightLogic.ValueRO,
                        ownVelocity, localPos, targetPos, distance, idealDist);
                    break;

                case FightLogicType.Swarm:
                    StepSwarm(ref unitMover.ValueRW, in fightLogic.ValueRO,
                        ref ps, entity, localPos, targetPos, idealDist, elapsed,
                        ref shipSquadRefLookup);
                    break;

                default:
                    // Fallback for new patterns.
                    StepHoldDistance(ref unitMover.ValueRW, in fightLogic.ValueRO,
                        ref ps, ref rng, targetPos, toTargetDir, distance, idealDist, dt);
                    break;
            }

            ps.rngState = rng.state;
        }
    }

    // Keep preferred range, with small side drift.
    private static void StepHoldDistance(
        ref UnitMover mover, in FightLogic fl, ref FightPatternState ps, ref Random rng,
        float2 targetPos, float2 toTargetDir, float distance, float idealDist, float dt)
    {
        // slow distance jitter so group don't look like wall
        UpdateRadiusOffset(ref ps, ref rng, fl.holdDriftPeriod, idealDist * 0.05f, dt);
        float effectiveIdeal = idealDist + ps.radiusOffset;

        float innerR = effectiveIdeal * fl.holdHysteresisInner;
        float outerR = effectiveIdeal * fl.holdHysteresisOuter;

        // dead zone so ship don't toggle at range border
        if (ps.inPosition == 0)
        {
            if (distance >= innerR && distance <= outerR)
                ps.inPosition = 1;
        }
        else
        {
            if (distance < innerR * 0.85f || distance > outerR)
                ps.inPosition = 0;
        }

        if (ps.inPosition == 1)
        {
            ps.driftPhase += dt * (math.PI * 2f / fl.holdDriftPeriod);
            float2 perp = new float2(-toTargetDir.y, toTargetDir.x);
            float driftAmp = idealDist * fl.holdDriftSpeed * 0.5f;
            float driftSign = math.sin(ps.driftPhase);

            float2 anchor = targetPos - toTargetDir * effectiveIdeal;
            mover.fightTarget = anchor + perp * (driftAmp * driftSign);
        }
        else
        {
            float2 stationPoint = targetPos - toTargetDir * effectiveIdeal;
            mover.fightTarget = stationPoint;
        }
    }

    // Fly to firing range and stay there, don't kite back.
    private static void StepCloseAndHold(
        ref UnitMover mover, ref FightPatternState ps,
        float2 localPos, float2 targetPos, float2 toTargetDir,
        float distance, float idealDist)
    {
        float stopDistance = math.max(0.5f, idealDist);

        float resumeDistance = stopDistance * 1.12f;

        if (ps.inPosition == 0)
        {
            if (distance <= stopDistance)
            {
                ps.inPosition = 1;
            }
        }
        else
        {
            if (distance > resumeDistance)
            {
                ps.inPosition = 0;
            }
        }

        if (ps.inPosition != 0)
        {
            mover.fightTarget = localPos;
            return;
        }

        mover.fightTarget = targetPos - toTargetDir * stopDistance;
    }

    // Orbit around target, correct radius drift.
    private static void StepOrbit(
        ref UnitMover mover, in FightLogic fl, ref FightPatternState ps, ref Random rng,
        float2 localPos, float2 targetPos, float distance, float idealDist, float attackRangeMax,
        float dt, float elapsed)
    {
        // jitter ring radius, not direction
        float maxOffset = idealDist * fl.orbitRadiusJitter;
        UpdateRadiusOffset(ref ps, ref rng, fl.orbitJitterPeriod, maxOffset, dt);

        float effectiveRadius = math.max(0.5f, idealDist + ps.radiusOffset);
        if (attackRangeMax > 0.01f)
        {
            effectiveRadius = math.min(effectiveRadius, attackRangeMax * 0.86f);
        }

        float2 fromTarget = localPos - targetPos;
        float2 radialOut = math.normalizesafe(fromTarget, new float2(1f, 0f));
        float orbitSign = fl.orbitDirection >= 0f ? 1f : -1f;
        float2 tangent = new float2(-radialOut.y, radialOut.x) * orbitSign;

        // error > 0 too far, error < 0 too close.
        float radialErrNorm = math.clamp(
            (distance - effectiveRadius) / math.max(effectiveRadius, 0.01f),
            -1f,
            1f);

        float radialStrength = math.clamp(math.abs(radialErrNorm) * 1.35f, 0f, 1.25f);
        float2 radialCorrection = radialErrNorm > 0f
            ? -radialOut * radialStrength
            : radialOut * radialStrength;

        float speedJitter = 1f + fl.orbitSpeedJitter * 0.12f * math.sin(elapsed * 0.7f + ps.driftPhase);
        float2 desiredDir = math.normalizesafe(tangent * speedJitter + radialCorrection, tangent);

        // orbit point must be ahead or ship stops near enemy
        float lookDistance = math.max(2f, effectiveRadius * 0.65f);
        mover.fightTarget = localPos + desiredDir * lookDistance;
    }

    // AttackRun: approach, fire pass, breakaway, reposition.
    private static void StepAttackRun(
        ref UnitMover mover, in FightLogic fl, in ShipAgro agro,
        ref FightPatternState ps, ref Random rng,
        float2 localPos, float2 targetPos, float2 toTargetDir, float distance)
    {
        float attackRange = math.max(agro.attackRangeMax, 1f);
        float fireDist = attackRange * fl.attackRunFireRange;

        switch (ps.phase)
        {
            case PhaseApproach:
                {
                    mover.fightTarget = targetPos;

                    if (distance <= fireDist)
                    {
                        ps.phase = PhaseFiring;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseFiring:
                {
                    mover.fightTarget = targetPos + toTargetDir * (attackRange * 0.4f);

                    bool passedTarget = math.dot(toTargetDir, targetPos - localPos) < 0f;
                    if (ps.phaseTimer >= fl.attackRunFiringDuration || passedTarget)
                    {
                        ps.phase = PhaseBreakaway;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseBreakaway:
                {
                    float2 awayDir = -toTargetDir;
                    mover.fightTarget = localPos + awayDir * attackRange;

                    if (ps.phaseTimer >= fl.attackRunBreakawayDuration)
                    {
                        float spread = fl.attackRunRepositionSpread;
                        float angle = rng.NextFloat(-spread, spread);
                        float2 baseDir = -toTargetDir;
                        float2 rotated = math.mul(float2x2.Rotate(angle), baseDir);
                        ps.cachedPoint = targetPos + rotated * (attackRange * fl.attackRunRepositionDistance);

                        ps.phase = PhaseReposition;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseReposition:
                {
                    mover.fightTarget = ps.cachedPoint;

                    bool reachedPoint = math.distancesq(localPos, ps.cachedPoint) < (attackRange * 0.2f) * (attackRange * 0.2f);
                    if (ps.phaseTimer >= fl.attackRunRepositionDuration || reachedPoint)
                    {
                        ps.phase = PhaseApproach;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }
        }
    }

    // InterceptorPass
    private static void StepInterceptorPass(
        ref UnitMover mover, in FightLogic fl, in ShipAgro agro,
        ref FightPatternState ps, ref Random rng,
        float2 localPos, float2 targetPos, float2 toTargetDir, float distance,
        Entity targetEntity, ref ComponentLookup<Velocity> velocityLookup, float2 ownVelocity)
    {
        float attackRange = math.max(agro.attackRangeMax, 1f);
        float fireDist = attackRange * fl.attackRunFireRange;

        float2 targetVel = float2.zero;
        if (velocityLookup.HasComponent(targetEntity))
        {
            targetVel = velocityLookup[targetEntity].velocity;
        }

        float ownSpeed = math.length(ownVelocity);
        float roughT = distance / math.max(ownSpeed, fl.interceptorMinOwnSpeedForLead);
        roughT = math.min(roughT, fl.interceptorMaxLeadTime);
        float2 leadPoint = targetPos + targetVel * roughT;

        switch (ps.phase)
        {
            case PhaseApproach:
                {
                    mover.fightTarget = leadPoint;

                    if (distance <= fireDist)
                    {
                        float2 entryDir = math.normalizesafe(leadPoint - localPos, toTargetDir);
                        ps.cachedPoint = entryDir;
                        ps.phase = PhaseFiring;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseFiring:
                {
                    float2 entryDir = ps.cachedPoint;
                    if (math.lengthsq(entryDir) < 1e-4f)
                    {
                        entryDir = math.normalizesafe(leadPoint - localPos, toTargetDir);
                    }
                    mover.fightTarget = leadPoint + entryDir * (attackRange * 0.4f);

                    bool passedTarget = math.dot(entryDir, leadPoint - localPos) < 0f;
                    if (ps.phaseTimer >= fl.attackRunFiringDuration || passedTarget)
                    {
                        ps.cachedPoint = -entryDir;
                        ps.phase = PhaseBreakaway;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseBreakaway:
                {
                    float2 awayDir = ps.cachedPoint;
                    if (math.lengthsq(awayDir) < 1e-4f)
                    {
                        awayDir = -toTargetDir;
                    }
                    mover.fightTarget = localPos + awayDir * attackRange;

                    if (ps.phaseTimer >= fl.attackRunBreakawayDuration)
                    {
                        float spread = fl.attackRunRepositionSpread;
                        float angle = rng.NextFloat(-spread, spread);
                        float2 rotated = math.mul(float2x2.Rotate(angle), awayDir);
                        ps.cachedPoint = targetPos + rotated * (attackRange * fl.attackRunRepositionDistance);

                        ps.phase = PhaseReposition;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseReposition:
                {
                    mover.fightTarget = ps.cachedPoint;

                    bool reachedPoint = math.distancesq(localPos, ps.cachedPoint) < (attackRange * 0.2f) * (attackRange * 0.2f);
                    if (ps.phaseTimer >= fl.attackRunRepositionDuration || reachedPoint)
                    {
                        ps.phase = PhaseApproach;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }
        }
    }

    // MissileAttackRun
    private static void StepMissileAttackRun(
        ref UnitMover mover, in FightLogic fl, ref FightPatternState ps, ref Random rng,
        float2 localPos, float2 targetPos, float2 toTargetDir, float distance, float idealDist, float attackRangeMax)
    {
        float launchDist = idealDist * math.max(0.1f, fl.missileLaunchDistance);
        float retreatDist = idealDist * math.max(launchDist / math.max(idealDist, 0.001f), fl.missileRetreatDistance);

        if (attackRangeMax > 0.01f)
        {
            float maxLaunchDist = attackRangeMax * 0.82f;
            launchDist = math.min(launchDist, maxLaunchDist);

            float minRetreatDist = launchDist * 1.08f;
            float maxRetreatDist = attackRangeMax * 1.15f;
            retreatDist = math.clamp(retreatDist, minRetreatDist, math.max(minRetreatDist, maxRetreatDist));
        }

        switch (ps.phase)
        {
            case PhaseMissileApproach:
                {
                    float2 launchPoint = targetPos - toTargetDir * launchDist;
                    mover.fightTarget = launchPoint;

                    float lower = launchDist * 0.92f;
                    float upper = launchDist * 1.08f;
                    if (distance >= lower && distance <= upper)
                    {
                        ps.phase = PhaseMissileLaunch;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseMissileLaunch:
                {
                    float driftAmp = idealDist * 0.04f;
                    float2 perp = new float2(-toTargetDir.y, toTargetDir.x);
                    float driftSign = math.sin(ps.phaseTimer * 1.5f + ps.driftPhase);

                    float2 launchPoint = targetPos - toTargetDir * launchDist;
                    mover.fightTarget = launchPoint + perp * driftAmp * driftSign;

                    if (ps.phaseTimer >= fl.missileLaunchDuration)
                    {
                        float spread = math.radians(35f);
                        float angle = rng.NextFloat(-spread, spread);
                        float2 awayBase = -toTargetDir;
                        float2 rotated = math.mul(float2x2.Rotate(angle), awayBase);
                        ps.cachedPoint = targetPos + rotated * retreatDist;

                        ps.phase = PhaseMissileRetreat;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseMissileRetreat:
                {
                    mover.fightTarget = ps.cachedPoint;

                    bool reachedPoint = math.distancesq(localPos, ps.cachedPoint) < (retreatDist * 0.15f) * (retreatDist * 0.15f);
                    if (reachedPoint || ps.phaseTimer >= fl.missileRetreatDuration)
                    {
                        ps.phase = PhaseMissileReload;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }

            case PhaseMissileReload:
                {
                    mover.fightTarget = ps.cachedPoint;

                    if (ps.phaseTimer >= fl.missileReloadDuration)
                    {
                        ps.phase = PhaseMissileApproach;
                        ps.phaseTimer = 0f;
                    }
                    break;
                }
        }
    }

    // Strafe
    private static void StepStrafe(
        ref UnitMover mover, in FightLogic fl, ref FightPatternState ps, ref Random rng,
        float2 localPos, float2 targetPos, float2 toTargetDir, float distance, float idealDist,
        float dt, float2 ownVelocity)
    {
        float legLength = idealDist * fl.strafeLegLength;
        float minDist = idealDist * fl.strafeMinDistance;

        if (ps.strafeDirection == 0)
        {
            ps.strafeDirection = (sbyte)(rng.NextBool() ? 1 : -1);
            ps.strafeProgress = 0f;
        }

        if (distance < minDist)
        {
            float2 stationPoint = targetPos - toTargetDir * idealDist;
            mover.fightTarget = stationPoint;
            return;
        }

        ps.strafeProgress += math.length(ownVelocity) * dt;

        if (ps.strafeProgress >= legLength)
        {
            ps.strafeDirection = (sbyte)(-ps.strafeDirection);
            ps.strafeProgress = 0f;
        }

        float2 lateral = new float2(-toTargetDir.y, toTargetDir.x) * ps.strafeDirection;

        float radialCorr = (idealDist - distance) / idealDist;
        float2 radial = -toTargetDir * radialCorr;

        mover.fightTarget = localPos + (lateral + radial * 0.4f) * idealDist;
    }

    // Dogfight
    private static void StepDogfight(
        ref UnitMover mover, in FightLogic fl, float2 ownVelocity,
        float2 localPos, float2 targetPos, float distance, float idealDist)
    {
        float2 toTarget = targetPos - localPos;
        float2 toTargetDir = math.normalizesafe(toTarget, new float2(1f, 0f));
        float2 shipForward = math.normalizesafe(ownVelocity, toTargetDir);
        float dot = math.dot(shipForward, toTargetDir);

        if (distance > idealDist)
        {
            mover.fightTarget = targetPos;
        }
        else if (dot > 0.7f)
        {
            mover.fightTarget = localPos + shipForward * idealDist;
        }
        else if (dot > 0f)
        {
            mover.fightTarget = targetPos;
        }
        else
        {
            float2 sideDir = new float2(-shipForward.y, shipForward.x) * fl.orbitDirection;
            mover.fightTarget = localPos + sideDir * idealDist * 0.6f
                              + shipForward * idealDist * 0.3f;
        }
    }

    // Swarm
    private static void StepSwarm(
        ref UnitMover mover, in FightLogic fl,
        ref FightPatternState ps, Entity entity,
        float2 localPos, float2 targetPos, float idealDist, float elapsed,
        ref ComponentLookup<ShipSquadRef> shipSquadRefLookup)
    {
        int slot;
        if (shipSquadRefLookup.HasComponent(entity))
        {
            slot = shipSquadRefLookup[entity].formationSlotIndex;
        }
        else
        {
            slot = entity.Index;
        }

        int slotsPerCircle = math.max(2, fl.swarmSlotsPerCircle);
        float baseAngle = (slot % slotsPerCircle) * (math.PI * 2f / slotsPerCircle);

        float globalRot = math.radians(fl.swarmRotationDegPerSec) * elapsed;
        if (fl.orbitDirection < 0f)
        {
            globalRot = -globalRot;
        }

        float jitter = math.sin(elapsed * 0.6f + ps.driftPhase) * 0.08f;

        float angle = baseAngle + globalRot + jitter;
        float2 dir = new float2(math.cos(angle), math.sin(angle));

        mover.fightTarget = targetPos + dir * idealDist;
    }


    private static void UpdateRadiusOffset(
        ref FightPatternState ps, ref Random rng, float period, float maxAmp, float dt)
    {
        ps.radiusOffsetTimer -= dt;
        if (ps.radiusOffsetTimer <= 0f)
        {
            ps.radiusOffset = rng.NextFloat(-maxAmp, maxAmp);
            ps.radiusOffsetTimer = period;
        }
    }

    private static float ResolveUsableIdealDistance(ref FightLogic fightLogic, in ShipAgro shipAgro)
    {
        float idealDist = fightLogic.idealDistance;
        if (idealDist > 0f)
            return idealDist;

        // only weapon range here, detection radius is not firing range
        if (shipAgro.attackRangeMax > 0f)
        {
            idealDist = shipAgro.attackRangeMax * 0.7f;
            fightLogic.idealDistance = idealDist;
            return idealDist;
        }

        return 0f;
    }

    private static float ClampIdealDistanceToWeaponRange(FightLogicType pattern, float idealDist, in ShipAgro shipAgro)
    {
        if (idealDist <= 0f)
            return 0f;

        float attackRangeMax = shipAgro.attackRangeMax;
        if (attackRangeMax <= 0.01f)
            return idealDist;

        // keep inside weapon range or ship never fires
        float maxFactor = 0.86f;
        if (pattern == FightLogicType.Orbit)
            maxFactor = 0.78f;
        else if (pattern == FightLogicType.MissileAttackRun)
            maxFactor = 0.72f;

        float maxIdeal = math.max(0.5f, attackRangeMax * maxFactor);
        return math.min(idealDist, maxIdeal);
    }

    private static FightLogicType ResolveFightPatternAndIdealDistance(
        in FightLogic fl,
        Entity targetEntity,
        ref ComponentLookup<Unit> unitLookup,
        ref float idealDist)
    {
        if (fl.useTargetSizePatterns == 0)
        {
            return fl.movementType;
        }

        if (targetEntity == Entity.Null || !unitLookup.HasComponent(targetEntity))
        {
            return fl.movementType;
        }

        ShipSize targetSize = (ShipSize)unitLookup[targetEntity].shipSize;

        if (IsBigTarget(targetSize))
        {
            idealDist *= SafeDistanceMultiplier(fl.bigTargetIdealDistanceMultiplier);
            return fl.bigTargetPattern;
        }

        if (IsMediumTarget(targetSize))
        {
            idealDist *= SafeDistanceMultiplier(fl.mediumTargetIdealDistanceMultiplier);
            return fl.mediumTargetPattern;
        }

        idealDist *= SafeDistanceMultiplier(fl.smallTargetIdealDistanceMultiplier);
        return fl.smallTargetPattern;
    }

    private static bool IsBigTarget(ShipSize targetSize)
    {
        return (targetSize & ShipSize.Big) != 0
            || (targetSize & ShipSize.RocketBig) != 0;
    }

    private static bool IsMediumTarget(ShipSize targetSize)
    {
        return (targetSize & ShipSize.Medium) != 0;
    }

    private static float SafeDistanceMultiplier(float value)
    {
        return value > 0.01f ? value : 1f;
    }

    private static uint HashEntityToRng(Entity entity)
    {
        unchecked
        {
            uint h = (uint)entity.Index * 2654435761u;
            h ^= (uint)entity.Version * 40503u;
            h = (h ^ (h >> 16)) * 0x85ebca6bu;
            h = (h ^ (h >> 13)) * 0xc2b2ae35u;
            h ^= h >> 16;
            return h;
        }
    }
}
