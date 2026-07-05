using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// hot path. member updates are throttled per squad, offsets cached in SquadMember
// job uses Schedule, not ScheduleParallel: members are written through ComponentLookup

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SquadDefaultsSystem))]
public partial struct SquadDefaultsRuntimeInitSystem : ISystem
{
    private EntityQuery missingRuntimeQuery;

    public void OnCreate(ref SystemState state)
    {
        missingRuntimeQuery = SystemAPI.QueryBuilder()
            .WithAll<SquadronTag>()
            .WithNone<SquadDefaultsRuntime>()
            .Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (missingRuntimeQuery.IsEmptyIgnoreFilter) { return; }

        state.EntityManager.AddComponent(
            missingRuntimeQuery,
            ComponentType.ReadWrite<SquadDefaultsRuntime>());
    }
}

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StrikeGroupOrderExecutionSystem))]
[UpdateAfter(typeof(CarrierRecallSystem))]
[UpdateAfter(typeof(SquadDefaultsRuntimeInitSystem))]
[UpdateBefore(typeof(GroupManagerSystem))]
[UpdateBefore(typeof(ShipAgroSystem))]
public partial struct SquadDefaultsSystem : ISystem
{
    private const float PositionWriteEpsilonSq = 0.0001f;
    private const float FloatWriteEpsilon = 0.0001f;

    // normal squads don't need defaults refreshed every frame
    private const float NormalUpdateInterval = 0.05f;   // 20 Hz
    private const float ReturningUpdateInterval = 0.025f; // 40 Hz for carrier recall/docking
    private const float StaggerWindow = 0.02f;

    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<UnitMover> moverLookup;
    private ComponentLookup<ShipStateComponent> stateLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<ShipPriorityHint> hintLookup;
    private ComponentLookup<GroupManagerComponent> groupManagerLookup;
    private ComponentLookup<UnitGroup> unitGroupLookup;
    private ComponentLookup<SquadPathSizeClass> squadPathSizeLookup;
    private ComponentLookup<PathfindingSizeClassComponent> pathSizeClassLookup;
    private EntityStorageInfoLookup entityStorageLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        transformLookup = state.GetComponentLookup<LocalTransform>(false);
        moverLookup = state.GetComponentLookup<UnitMover>(false);
        stateLookup = state.GetComponentLookup<ShipStateComponent>(false);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(true);
        hintLookup = state.GetComponentLookup<ShipPriorityHint>(false);
        groupManagerLookup = state.GetComponentLookup<GroupManagerComponent>(false);
        unitGroupLookup = state.GetComponentLookup<UnitGroup>(false);
        squadPathSizeLookup = state.GetComponentLookup<SquadPathSizeClass>(true);
        pathSizeClassLookup = state.GetComponentLookup<PathfindingSizeClassComponent>(true);
        entityStorageLookup = state.GetEntityStorageInfoLookup();

        state.RequireForUpdate<SquadronTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        moverLookup.Update(ref state);
        stateLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        hintLookup.Update(ref state);
        groupManagerLookup.Update(ref state);
        unitGroupLookup.Update(ref state);
        squadPathSizeLookup.Update(ref state);
        pathSizeClassLookup.Update(ref state);
        entityStorageLookup.Update(ref state);

        float flowFieldRepathDistance = 6f;
        if (SystemAPI.TryGetSingleton(out FlowFieldData flowFieldData) && flowFieldData.CellSize > 0f)
        {
            flowFieldRepathDistance = flowFieldData.CellSize;
        }

        SquadDefaultsJob job = new SquadDefaultsJob
        {
            transformLookup = transformLookup,
            moverLookup = moverLookup,
            stateLookup = stateLookup,
            shipAgroLookup = shipAgroLookup,
            hintLookup = hintLookup,
            groupManagerLookup = groupManagerLookup,
            unitGroupLookup = unitGroupLookup,
            squadPathSizeLookup = squadPathSizeLookup,
            pathSizeClassLookup = pathSizeClassLookup,
            entityStorageLookup = entityStorageLookup,
            now = (float)SystemAPI.Time.ElapsedTime,
            flowFieldRepathDistanceSq = flowFieldRepathDistance * flowFieldRepathDistance,
            positionWriteEpsilonSq = PositionWriteEpsilonSq,
            floatWriteEpsilon = FloatWriteEpsilon,
            normalUpdateInterval = NormalUpdateInterval,
            returningUpdateInterval = ReturningUpdateInterval,
            staggerWindow = StaggerWindow,
        };

        state.Dependency = job.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(SquadronTag))]
public partial struct SquadDefaultsJob : IJobEntity
{
    public ComponentLookup<LocalTransform> transformLookup;
    public ComponentLookup<UnitMover> moverLookup;
    public ComponentLookup<ShipStateComponent> stateLookup;
    public ComponentLookup<ShipPriorityHint> hintLookup;
    public ComponentLookup<GroupManagerComponent> groupManagerLookup;
    public ComponentLookup<UnitGroup> unitGroupLookup;

    [ReadOnly] public ComponentLookup<ShipAgro> shipAgroLookup;
    [ReadOnly] public ComponentLookup<SquadPathSizeClass> squadPathSizeLookup;
    [ReadOnly] public ComponentLookup<PathfindingSizeClassComponent> pathSizeClassLookup;
    [ReadOnly] public EntityStorageInfoLookup entityStorageLookup;

    public float now;
    public float flowFieldRepathDistanceSq;
    public float positionWriteEpsilonSq;
    public float floatWriteEpsilon;
    public float normalUpdateInterval;
    public float returningUpdateInterval;
    public float staggerWindow;

    public void Execute(
        Entity squadEntity,
        ref SquadComponent squad,
        ref SquadDefaultsRuntime runtime,
        DynamicBuffer<SquadMember> members)
    {
        if (members.Length == 0) { return; }

        bool isCarrierSquad = squad.origin == SquadOrigin.Carrier && squad.originEntity != Entity.Null;
        bool isCarrierReturning = isCarrierSquad &&
            squad.defaultFireMode == FireMode.HoldFire &&
            squad.defaultMoveMode == MoveMode.HoldPosition;

        if (runtime.nextUpdateTime > now) { return; }

        float interval = isCarrierReturning ? returningUpdateInterval : normalUpdateInterval;
        runtime.nextUpdateTime = now + interval + GetEntityStagger(squadEntity, staggerWindow);

        float2 anchor = squad.anchorPosition;
        TacticsModifiers tmod = TacticsUtility.Get(squad.tactics);

        if (squad.anchorEntity != Entity.Null &&
            transformLookup.TryGetComponent(squad.anchorEntity, out LocalTransform anchorTransform))
        {
            anchor = anchorTransform.Position.xy;
            if (!NearlyEqual(squad.anchorPosition, anchor, positionWriteEpsilonSq))
            {
                squad.anchorPosition = anchor;
            }
        }

        if (transformLookup.TryGetComponent(squadEntity, out LocalTransform squadTransform))
        {
            if (!NearlyEqual(squadTransform.Position.xy, anchor, positionWriteEpsilonSq))
            {
                squadTransform.Position = new float3(anchor.x, anchor.y, squadTransform.Position.z);
                transformLookup[squadEntity] = squadTransform;
            }
        }

        bool isCarrierLaunchedEscort = isCarrierSquad && !isCarrierReturning;
        int formationMemberCount = members.Length;

        float baseFormationArriveDistance = math.max(2.5f, squad.spacing * 0.15f);
        float baseCombatUnlockDistance = math.max(8f, squad.spacing * 1.25f);
        float formationArriveDistance = baseFormationArriveDistance * tmod.arrivalRadiusMul;
        float combatUnlockDistance = baseCombatUnlockDistance * tmod.anchorLeashMul;
        if (squad.defaultMoveMode == MoveMode.HoldPosition)
        {
            combatUnlockDistance = math.lerp(combatUnlockDistance, baseCombatUnlockDistance * 0.35f, tmod.holdTightness);
        }

        float formationArriveDistanceSq = formationArriveDistance * formationArriveDistance;
        float combatUnlockDistanceSq = combatUnlockDistance * combatUnlockDistance;

        float hintWeight = 50f * tmod.priorityHintWeightMul;
        Entity priorityTarget = squad.priorityTarget;
        FireMode squadFireMode = squad.defaultFireMode;
        MoveMode squadMoveMode = squad.defaultMoveMode;
        float spacing = squad.spacing;

        bool formationCacheChanged = runtime.formationCacheValid == 0 ||
            runtime.cachedFormation != squad.formation ||
            math.abs(runtime.cachedSpacing - spacing) > floatWriteEpsilon ||
            runtime.cachedMemberCount != formationMemberCount ||
            runtime.cachedReturning != (byte)(isCarrierReturning ? 1 : 0);

        if (formationCacheChanged)
        {
            runtime.cachedFormation = squad.formation;
            runtime.cachedSpacing = spacing;
            runtime.cachedMemberCount = formationMemberCount;
            runtime.cachedReturning = (byte)(isCarrierReturning ? 1 : 0);
            runtime.formationCacheValid = 1;
        }

        PathfindingSizeClass squadPathSize = PathfindingSizeClass.Medium;
        bool squadPathSizeValid = false;
        bool squadPathSizeComputed = false;

        for (int i = 0; i < members.Length; i++)
        {
            SquadMember member = members[i];
            Entity ship = member.ship;
            if (ship == Entity.Null || !entityStorageLookup.Exists(ship)) { continue; }

            float2 offset = GetCachedFormationOffset(
                i,
                ref member,
                formationCacheChanged,
                isCarrierReturning,
                squad.formation,
                formationMemberCount,
                spacing,
                members);

            float2 desiredPos = anchor + offset;

            bool hasState = stateLookup.TryGetComponent(ship, out ShipStateComponent shipState);
            ShipStateComponent stateOriginal = shipState;
            ShipState memberCurrentState = hasState ? shipState.currentState : ShipState.Idle;
            bool memberIsInCombat = hasState && memberCurrentState == ShipState.InCombat;

            bool memberHasEnemy = shipAgroLookup.HasComponent(ship) && shipAgroLookup.IsComponentEnabled(ship);

            bool reachedFormationSlot = false;
            bool reachedCombatArea = false;
            if (transformLookup.TryGetComponent(ship, out LocalTransform shipTransform))
            {
                float2 memberPosition = shipTransform.Position.xy;
                reachedFormationSlot = math.distancesq(memberPosition, desiredPos) <= formationArriveDistanceSq;
                reachedCombatArea = math.distancesq(memberPosition, anchor) <= combatUnlockDistanceSq;
            }

            bool reachedDesired = reachedFormationSlot;
            bool reachedForCombat = reachedFormationSlot || reachedCombatArea;

            bool squadControlsMemberMovement = isCarrierReturning;
            if (!squadControlsMemberMovement)
            {
                if (isCarrierLaunchedEscort)
                {
                    squadControlsMemberMovement =
                        !memberIsInCombat &&
                        !(memberHasEnemy && squadMoveMode == MoveMode.AttackMove);
                }
                else
                {
                    squadControlsMemberMovement = ShouldSquadControlMemberMovement(
                        squadMoveMode, memberCurrentState, reachedDesired, reachedForCombat, memberHasEnemy);
                }
            }

            if (squadControlsMemberMovement && !reachedDesired)
            {
                RefreshSquadFlowFieldIfNeeded(
                    ship, squadEntity, members, anchor, flowFieldRepathDistanceSq,
                    ref squadPathSize, ref squadPathSizeValid, ref squadPathSizeComputed,
                    ref groupManagerLookup, ref unitGroupLookup,
                    ref squadPathSizeLookup, ref pathSizeClassLookup, entityStorageLookup,
                    positionWriteEpsilonSq);
            }

            if (squadControlsMemberMovement && moverLookup.TryGetComponent(ship, out UnitMover mover))
            {
                bool moverChanged = false;
                if (!NearlyEqual(mover.targetPos, desiredPos, positionWriteEpsilonSq))
                {
                    mover.targetPos = desiredPos;
                    moverChanged = true;
                }

                if (!NearlyEqual(mover.fightTarget, desiredPos, positionWriteEpsilonSq))
                {
                    mover.fightTarget = desiredPos;
                    moverChanged = true;
                }

                if (moverChanged) { moverLookup[ship] = mover; }
            }

            if (hasState)
            {
                if (isCarrierReturning)
                {
                    shipState.mode = FireMode.HoldFire;
                    shipState.moveMode = MoveMode.HoldPosition;
                    shipState.forcedTarget = Entity.Null;
                    SetMemberTravelState(ref shipState, reachedDesired);
                }
                else
                {
                    shipState.mode = squadFireMode;
                    shipState.moveMode = squadMoveMode;

                    if (shipState.currentState != ShipState.Following)
                    {
                        if (squadMoveMode == MoveMode.HoldPosition)
                        {
                            shipState.forcedTarget = Entity.Null;
                            SetMemberTravelState(ref shipState, reachedDesired);
                        }
                        else if (shipState.currentState == ShipState.InCombat)
                        {
                            // combat pattern owns movement while in combat
                        }
                        else if (squadControlsMemberMovement)
                        {
                            SetMemberTravelState(ref shipState, reachedDesired);
                        }
                        else if (squadMoveMode == MoveMode.MoveAndEngage && reachedForCombat)
                        {
                            SetMemberTravelState(ref shipState, true);
                        }
                    }
                }

                if (!ShipStateEquals(stateOriginal, shipState)) { stateLookup[ship] = shipState; }
            }

            if (hintLookup.TryGetComponent(ship, out ShipPriorityHint hint))
            {
                Entity newTarget = isCarrierReturning ? Entity.Null : priorityTarget;
                if (hint.target != newTarget || math.abs(hint.weight - hintWeight) > floatWriteEpsilon)
                {
                    hint.target = newTarget;
                    hint.weight = hintWeight;
                    hintLookup[ship] = hint;
                }
            }
        }
    }

    private static float2 GetCachedFormationOffset(
        int memberIndex,
        ref SquadMember member,
        bool formationCacheChanged,
        bool isCarrierReturning,
        FormationType formation,
        int formationMemberCount,
        float spacing,
        DynamicBuffer<SquadMember> members)
    {
        if (isCarrierReturning)
        {
            if (member.formationOffsetValid != 0 || !NearlyEqual(member.formationOffset, float2.zero, 0.000001f))
            {
                member.formationOffset = float2.zero;
                member.cachedFormationSlotIndex = member.formationSlotIndex;
                member.formationOffsetValid = 0;
                members[memberIndex] = member;
            }

            return float2.zero;
        }

        bool memberCacheInvalid = formationCacheChanged ||
            member.formationOffsetValid == 0 ||
            member.cachedFormationSlotIndex != member.formationSlotIndex;

        if (memberCacheInvalid)
        {
            member.formationOffset = FormationUtility.GetSlotOffset(
                formation,
                member.formationSlotIndex,
                formationMemberCount,
                spacing);
            member.cachedFormationSlotIndex = member.formationSlotIndex;
            member.formationOffsetValid = 1;
            members[memberIndex] = member;
        }

        return member.formationOffset;
    }

    private static bool ShouldSquadControlMemberMovement(
        MoveMode moveMode, ShipState currentState,
        bool reachedDesiredPosition, bool reachedForCombat, bool hasEnemy)
    {
        if (currentState == ShipState.Following) { return false; }
        if (moveMode == MoveMode.HoldPosition) { return true; }

        if (currentState == ShipState.InCombat)
        {
            return MoveModeCombatRules.ShouldForceFormationMovement(
                moveMode, currentState, reachedDesiredPosition);
        }

        if (moveMode == MoveMode.AttackMove && hasEnemy) { return false; }
        if (moveMode == MoveMode.MoveAndEngage && reachedForCombat && hasEnemy) { return false; }

        return true;
    }

    private static void SetMemberTravelState(ref ShipStateComponent shipState, bool reachedDesiredPosition)
    {
        ShipState nextState = reachedDesiredPosition ? ShipState.Idle : ShipState.MovingToTarget;
        if (shipState.currentState != nextState)
        {
            shipState.previousState = shipState.currentState;
            shipState.currentState = nextState;
        }
    }

    private static bool ShipStateEquals(in ShipStateComponent a, in ShipStateComponent b)
    {
        return a.currentState == b.currentState &&
            a.previousState == b.previousState &&
            a.mode == b.mode &&
            a.moveMode == b.moveMode &&
            a.forcedTarget == b.forcedTarget;
    }

    private static void RefreshSquadFlowFieldIfNeeded(
        Entity ship, Entity squadEntity, DynamicBuffer<SquadMember> members,
        float2 flowFieldTarget, float repathDistanceSq,
        ref PathfindingSizeClass squadPathSize, ref bool squadPathSizeValid, ref bool squadPathSizeComputed,
        ref ComponentLookup<GroupManagerComponent> groupManagerLookup,
        ref ComponentLookup<UnitGroup> unitGroupLookup,
        ref ComponentLookup<SquadPathSizeClass> squadPathSizeLookup,
        ref ComponentLookup<PathfindingSizeClassComponent> pathSizeClassLookup,
        EntityStorageInfoLookup entityStorageLookup,
        float positionWriteEpsilonSq)
    {
        if (!groupManagerLookup.TryGetComponent(ship, out GroupManagerComponent groupManager)) { return; }

        bool shouldRequest = false;

        if (math.distancesq(groupManager.position, flowFieldTarget) > repathDistanceSq)
        {
            shouldRequest = true;
        }

        bool hasUnitGroup = unitGroupLookup.TryGetComponent(ship, out UnitGroup unitGroup);
        if (hasUnitGroup)
        {
            if (unitGroup.GroupEntity == Entity.Null) { shouldRequest = true; }
        }
        else
        {
            shouldRequest = true;
        }

        if (!shouldRequest) { return; }

        if (!squadPathSizeComputed)
        {
            squadPathSizeValid = TryGetSquadPathSize(
                squadEntity, members, ref squadPathSizeLookup, ref pathSizeClassLookup,
                entityStorageLookup, out squadPathSize);
            squadPathSizeComputed = true;
        }

        bool groupManagerChanged = false;

        if (!NearlyEqual(groupManager.position, flowFieldTarget, positionWriteEpsilonSq))
        {
            groupManager.position = flowFieldTarget;
            groupManagerChanged = true;
        }

        if (!groupManager.addOrCreateGroup)
        {
            groupManager.addOrCreateGroup = true;
            groupManagerChanged = true;
        }

        if (groupManager.setTargetWithoutGroup)
        {
            groupManager.setTargetWithoutGroup = false;
            groupManagerChanged = true;
        }

        if (groupManager.partOfSwarm)
        {
            groupManager.partOfSwarm = false;
            groupManagerChanged = true;
        }

        if (squadPathSizeValid)
        {
            if (!groupManager.useOverrideSizeClass || groupManager.overrideSizeClass != squadPathSize)
            {
                groupManager.overrideSizeClass = squadPathSize;
                groupManager.useOverrideSizeClass = true;
                groupManagerChanged = true;
            }
        }
        else
        {
            if (groupManager.useOverrideSizeClass || groupManager.overrideSizeClass != default)
            {
                groupManager.overrideSizeClass = default;
                groupManager.useOverrideSizeClass = false;
                groupManagerChanged = true;
            }
        }

        if (groupManagerChanged) { groupManagerLookup[ship] = groupManager; }

        if (hasUnitGroup && unitGroup.GroupEntity != Entity.Null)
        {
            unitGroup.GroupEntity = Entity.Null;
            unitGroupLookup[ship] = unitGroup;
        }
    }

    private static bool TryGetSquadPathSize(
        Entity squadEntity, DynamicBuffer<SquadMember> members,
        ref ComponentLookup<SquadPathSizeClass> squadPathSizeLookup,
        ref ComponentLookup<PathfindingSizeClassComponent> pathSizeClassLookup,
        EntityStorageInfoLookup entityStorageLookup,
        out PathfindingSizeClass squadPathSize)
    {
        if (squadPathSizeLookup.TryGetComponent(squadEntity, out SquadPathSizeClass cached) && cached.Valid)
        {
            squadPathSize = cached.Value;
            return true;
        }

        squadPathSize = PathfindingSizeClass.Small;
        bool hasAnyMember = false;
        int squadRank = -1;

        for (int i = 0; i < members.Length; i++)
        {
            Entity member = members[i].ship;
            if (member == Entity.Null || !entityStorageLookup.Exists(member)) { continue; }

            PathfindingSizeClass memberSize = pathSizeClassLookup.TryGetComponent(member, out PathfindingSizeClassComponent psc)
                ? psc.Value
                : PathfindingSizeClass.Medium;

            int memberRank = GetPathSizeRank(memberSize);
            if (!hasAnyMember || memberRank > squadRank)
            {
                squadPathSize = memberSize;
                squadRank = memberRank;
            }

            hasAnyMember = true;

            if (squadRank >= 2) { break; }
        }

        if (!hasAnyMember) { squadPathSize = PathfindingSizeClass.Medium; }

        return hasAnyMember;
    }

    private static bool NearlyEqual(float2 valueA, float2 valueB, float epsilonSq)
    {
        return math.distancesq(valueA, valueB) <= epsilonSq;
    }

    private static int GetPathSizeRank(PathfindingSizeClass sizeClass) => sizeClass switch
    {
        PathfindingSizeClass.Small => 0,
        PathfindingSizeClass.Medium => 1,
        PathfindingSizeClass.Large => 2,
        _ => 1,
    };

    private static float GetEntityStagger(Entity entity, float window)
    {
        uint hash = math.hash(new uint2((uint)entity.Index, (uint)entity.Version));
        return ((hash & 1023u) / 1023f) * window;
    }
}
