using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// finds targets for weapon slots. heavy grid searches are spread over frames
// so big battles don't get lag spikes

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ShipToGridSystem))]
[UpdateAfter(typeof(SearchlightSpotSystem))]
[UpdateAfter(typeof(MoveVelocitySystem))]
[UpdateAfter(typeof(RotateToMovementSystem))]
[UpdateBefore(typeof(EmbeddedWeaponFireRequestBuildSystem))]
public partial struct EmbeddedFindTargetSystem : ISystem
{
    private const float DefaultEmbeddedFindTargetInterval = 0.2f;
    private const float DefaultEmbeddedBetterTargetInterval = 0.9f;

    // max grid searches per frame. forced target check is cheap, not counted
    private const int MaxHeavyGridSearchesPerFrame = 248;
    private const int InitialPressureCapacity = 1024;
    private const float BudgetRetryDelayMin = 0.05f;
    private const float BudgetRetryDelayMax = 0.12f;

    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<ShipStateComponent> shipStateLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<Unit> unitLookup;
    private ComponentLookup<UseFogOfWar> useFogOfWarLookup;
    private ComponentLookup<Visibility> visibilityLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<UnitCollisionRadius> collisionRadiusLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<DisableWeaponTargetDistribution> disableTargetDistributionLookup;
    private ComponentLookup<EmbeddedWeaponFireRuntime> fireRuntimeLookup;
    private BufferLookup<EmbeddedWeaponSlot> slotsLookup;
    private BufferLookup<EmbeddedWeaponHardpoint> hardpointsLookup;
    private EntityStorageInfoLookup entityStorageLookup;
    private EntityQuery pressureRebuildRequestQuery;

    private NativeParallelHashMap<Entity, WeaponTargetPressure> pressureByTarget;
    private Entity pressureGridEntity;
    private bool pressureInitialized;
    private bool pressureNeedsRebuild;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        gridDataLookup = state.GetComponentLookup<GridData>(true);
        shipStateLookup = state.GetComponentLookup<ShipStateComponent>(true);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(true);
        unitLookup = state.GetComponentLookup<Unit>(true);
        useFogOfWarLookup = state.GetComponentLookup<UseFogOfWar>(true);
        visibilityLookup = state.GetComponentLookup<Visibility>(true);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        collisionRadiusLookup = state.GetComponentLookup<UnitCollisionRadius>(true);
        healthLookup = state.GetComponentLookup<Health>(true);
        disableTargetDistributionLookup = state.GetComponentLookup<DisableWeaponTargetDistribution>(true);
        fireRuntimeLookup = state.GetComponentLookup<EmbeddedWeaponFireRuntime>(false);
        slotsLookup = state.GetBufferLookup<EmbeddedWeaponSlot>(false);
        hardpointsLookup = state.GetBufferLookup<EmbeddedWeaponHardpoint>(true);
        entityStorageLookup = state.GetEntityStorageInfoLookup();
        pressureRebuildRequestQuery = state.GetEntityQuery(ComponentType.ReadOnly<EmbeddedWeaponPressureRebuildRequest>());

        pressureByTarget = new NativeParallelHashMap<Entity, WeaponTargetPressure>(InitialPressureCapacity, Allocator.Persistent);
        pressureGridEntity = Entity.Null;
        pressureInitialized = false;
        pressureNeedsRebuild = false;

        state.RequireForUpdate<GridData>();
        state.RequireForUpdate<WeaponProfileDatabase>();
    }


    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (pressureByTarget.IsCreated)
        {
            pressureByTarget.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<GridData>(out Entity gridEntity) ||
            !SystemAPI.TryGetSingleton<WeaponProfileDatabase>(out WeaponProfileDatabase database))
        {
            return;
        }

        // consume pressure rebuild requests before refreshing lookups:
        // destroying request entities is structural change, it breaks all lookups below
        bool pressureRebuildRequested = !pressureRebuildRequestQuery.IsEmptyIgnoreFilter;
        if (pressureRebuildRequested)
        {
            state.EntityManager.DestroyEntity(pressureRebuildRequestQuery);
        }

        gridDataLookup.Update(ref state);
        shipStateLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        unitLookup.Update(ref state);
        useFogOfWarLookup.Update(ref state);
        visibilityLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        collisionRadiusLookup.Update(ref state);
        healthLookup.Update(ref state);
        disableTargetDistributionLookup.Update(ref state);
        fireRuntimeLookup.Update(ref state);
        slotsLookup.Update(ref state);
        hardpointsLookup.Update(ref state);
        entityStorageLookup.Update(ref state);

        if (!gridDataLookup.HasComponent(gridEntity))
        {
            return;
        }

        GridData gridData = gridDataLookup[gridEntity];
        ref WeaponProfileDatabaseBlob root = ref database.Value.Value;
        float now = (float)SystemAPI.Time.ElapsedTime;

        WeaponTargetDistributionSettings distributionSettings = WeaponTargetDistributionDefaults.Create();
        if (SystemAPI.TryGetSingleton(out WeaponTargetDistributionSettings singletonDistributionSettings))
        {
            distributionSettings = singletonDistributionSettings;
        }

        if (!pressureByTarget.IsCreated)
        {
            pressureByTarget = new NativeParallelHashMap<Entity, WeaponTargetPressure>(InitialPressureCapacity, Allocator.Persistent);
            pressureInitialized = false;
            pressureNeedsRebuild = false;
        }

        if (!pressureInitialized || pressureNeedsRebuild || pressureGridEntity != gridEntity || pressureRebuildRequested)
        {
            RebuildPressureMap(ref state, ref root, gridEntity);
        }

        int remainingHeavyGridSearchBudget = MaxHeavyGridSearchesPerFrame;

        foreach ((RefRO<LocalTransform> shipLocal, Entity shipEntity)
            in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<EmbeddedWeaponHost>()
                .WithAll<EmbeddedWeaponSlot>()
                .WithAll<EmbeddedWeaponHardpoint>()
                .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedWeaponSlot> slots = slotsLookup[shipEntity];

            if (!unitLookup.TryGetComponent(shipEntity, out Unit ownerUnit) ||
                !shipStateLookup.TryGetComponent(shipEntity, out ShipStateComponent shipState))
            {
                if (ClearTargetsIfNeeded(slots, ref root, ref pressureByTarget))
                {
                    RefreshFireRuntimeIfChanged(shipEntity, slots);
                }

                continue;
            }

            bool wasHit = false;
            Entity preferredAgroTarget = Entity.Null;
            if (shipAgroLookup.TryGetComponent(shipEntity, out ShipAgro agro))
            {
                wasHit = agro.wasHit;
                if (shipAgroLookup.IsComponentEnabled(shipEntity))
                {
                    preferredAgroTarget = agro.targetEntity;
                }
            }

            bool hasForcedTarget = shipState.forcedTarget != Entity.Null;
            bool active;
            if (hasForcedTarget && shipState.currentState == ShipState.InCombat)
            {
                active = true;
            }
            else if (shipState.mode == FireMode.HoldFire)
            {
                active = false;
            }
            else if (shipState.mode == FireMode.ReturnFire && !wasHit)
            {
                active = false;
            }
            else
            {
                active = true;
            }

            if (!active)
            {
                if (ClearTargetsIfNeeded(slots, ref root, ref pressureByTarget))
                {
                    RefreshFireRuntimeIfChanged(shipEntity, slots);
                }

                continue;
            }

            bool forcedCombatForHost = hasForcedTarget && shipState.currentState == ShipState.InCombat;
            if (!HostNeedsWork(
                    slots,
                    now,
                    forcedCombatForHost,
                    shipState.forcedTarget,
                    ref entityStorageLookup,
                    ref localTransformLookup,
                    ref healthLookup))
            {
                continue;
            }

            DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints = hardpointsLookup[shipEntity];
            Faction ownerFaction = ownerUnit.faction;
            Faction targetFaction = VisibilityUtility.Opposite(ownerFaction);
            bool ownerUsesFogOfWar = useFogOfWarLookup.HasComponent(shipEntity);
            bool disableTargetDistribution = disableTargetDistributionLookup.HasComponent(shipEntity);
            quaternion shipRot = shipLocal.ValueRO.Rotation;
            float3 shipPos = shipLocal.ValueRO.Position;
            bool hostSlotsChanged = false;

            for (int i = 0; i < slots.Length; i++)
            {
                EmbeddedWeaponSlot slot = slots[i];

                if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
                {
                    continue;
                }

                // findTargetTimer is absolute time. don't decrease it every frame, it dirties buffer
                bool retargetDue = slot.findTargetTimer <= now;
                bool forcedCombat = hasForcedTarget && shipState.currentState == ShipState.InCombat;
                bool forceRetarget = forcedCombat && slot.targetEntity != shipState.forcedTarget;

                // keep current target until next search, drop only dead or missing
                if (!retargetDue && !forceRetarget)
                {
                    if (slot.targetEntity == Entity.Null)
                    {
                        continue;
                    }

                    if (!entityStorageLookup.Exists(slot.targetEntity))
                    {
                        float staleDps = EstimateSlotDpsOrDefault(ref root, in slot);
                        ClearSlotTarget(ref slot, staleDps, ref pressureByTarget);
                        slots[i] = slot;
                        hostSlotsChanged = true;
                        continue;
                    }

                    bool targetAlive =
                        localTransformLookup.HasComponent(slot.targetEntity) &&
                        (!healthLookup.TryGetComponent(slot.targetEntity, out Health targetHealth) ||
                         targetHealth.healthAmount > 0f);

                    if (targetAlive)
                    {
                        continue;
                    }

                    float deadDps = EstimateSlotDpsOrDefault(ref root, in slot);
                    ClearSlotTarget(ref slot, deadDps, ref pressureByTarget);
                    slots[i] = slot;
                    hostSlotsChanged = true;
                    continue;
                }

                // heavy path: forced target check or grid scan
                float estimatedDps = EstimateSlotDpsOrDefault(ref root, in slot);
                if ((uint)slot.profileIndex >= (uint)root.Profiles.Length)
                {
                    ResetSlotTargetAndThrottle(ref slot, now, shipEntity, i, estimatedDps, ref pressureByTarget);
                    slots[i] = slot;
                    hostSlotsChanged = true;
                    continue;
                }

                ref WeaponProfileBlob profile = ref root.Profiles[slot.profileIndex];
                estimatedDps = EstimateSlotDps(in profile, in slot);
                if (!IsSupportedEmbeddedRequestKind((WeaponRequestKind)profile.requestKind) ||
                    !IsSupportedEmbeddedFirePattern((WeaponFirePattern)profile.firePattern))
                {
                    ResetSlotTargetAndThrottle(ref slot, now, shipEntity, i, estimatedDps, ref pressureByTarget);
                    slots[i] = slot;
                    hostSlotsChanged = true;
                    continue;
                }

                if (!TryGetMuzzlePositionXY(
                        hardpoints, shipPos, shipRot, in slot,
                        (WeaponFirePattern)profile.firePattern,
                        out float2 muzzlePosition))
                {
                    ResetSlotTargetAndThrottle(ref slot, now, shipEntity, i, estimatedDps, ref pressureByTarget);
                    slots[i] = slot;
                    hostSlotsChanged = true;
                    continue;
                }

                if (forcedCombat)
                {
                    if (!entityStorageLookup.Exists(shipState.forcedTarget))
                    {
                        ResetSlotTargetAndThrottle(ref slot, now, shipEntity, i, estimatedDps, ref pressureByTarget);
                        slots[i] = slot;
                        hostSlotsChanged = true;
                        continue;
                    }

                    if (EmbeddedWeaponTargetValidationUtility.IsTargetValidForSlot(
                            shipState.forcedTarget,
                            muzzlePosition,
                            ownerFaction,
                            targetFaction,
                            ownerUsesFogOfWar,
                            in profile,
                            ref localTransformLookup,
                            ref unitLookup,
                            ref visibilityLookup,
                            ref collisionRadiusLookup,
                            ref healthLookup,
                            out float2 forcedPosition,
                            out _,
                            out _))
                    {
                        if (!AssignSlotTarget(ref slot, shipState.forcedTarget, forcedPosition, estimatedDps, ref pressureByTarget))
                        {
                            pressureNeedsRebuild = true;
                        }

                        slot.findTargetTimer = now + GetStableBetterTargetInterval(shipEntity, i, distributionSettings);
                        slots[i] = slot;
                        hostSlotsChanged = true;
                        continue;
                    }

                    // no grid search here or broken forcedTarget scans grid every frame
                    ResetSlotTargetAndThrottle(ref slot, now, shipEntity, i, estimatedDps, ref pressureByTarget);
                    slots[i] = slot;
                    hostSlotsChanged = true;
                    continue;
                }

                Entity oldSlotTarget = slot.targetEntity;
                bool currentValid = false;
                float2 currentPosition = float2.zero;
                float currentEdgeDistance = float.MaxValue;
                byte currentShipSize = 0;
                if (slot.targetEntity != Entity.Null && entityStorageLookup.Exists(slot.targetEntity))
                {
                    currentValid = EmbeddedWeaponTargetValidationUtility.IsTargetValidForSlot(
                        slot.targetEntity,
                        muzzlePosition,
                        ownerFaction,
                        targetFaction,
                        ownerUsesFogOfWar,
                        in profile,
                        ref localTransformLookup,
                        ref unitLookup,
                        ref visibilityLookup,
                        ref collisionRadiusLookup,
                        ref healthLookup,
                        out currentPosition,
                        out currentEdgeDistance,
                        out currentShipSize);
                }

                if (!currentValid)
                {
                    ClearSlotTarget(ref slot, estimatedDps, ref pressureByTarget);
                    oldSlotTarget = Entity.Null;
                }

                if (!currentValid &&
                    preferredAgroTarget != Entity.Null &&
                    entityStorageLookup.Exists(preferredAgroTarget) &&
                    EmbeddedWeaponTargetValidationUtility.IsTargetValidForSlot(
                        preferredAgroTarget,
                        muzzlePosition,
                        ownerFaction,
                        targetFaction,
                        ownerUsesFogOfWar,
                        in profile,
                        ref localTransformLookup,
                        ref unitLookup,
                        ref visibilityLookup,
                        ref collisionRadiusLookup,
                        ref healthLookup,
                        out float2 preferredPosition,
                        out _,
                        out _))
                {
                    if (!AssignSlotTarget(ref slot, preferredAgroTarget, preferredPosition, estimatedDps, ref pressureByTarget))
                    {
                        pressureNeedsRebuild = true;
                    }

                    slot.findTargetTimer = now + GetStableBetterTargetInterval(shipEntity, i, distributionSettings);
                    slots[i] = slot;
                    hostSlotsChanged = true;
                    continue;
                }

                // no budget, keep valid target or retry later
                if (remainingHeavyGridSearchBudget <= 0)
                {
                    if (currentValid)
                    {
                        slot.targetPositionWorld = currentPosition;
                    }
                    else
                    {
                        ClearSlotTarget(ref slot, estimatedDps, ref pressureByTarget);
                    }

                    slot.findTargetTimer = now + GetStableBudgetRetryDelay(shipEntity, i);
                    slots[i] = slot;
                    hostSlotsChanged = true;
                    continue;
                }

                remainingHeavyGridSearchBudget--;

                FindBestTarget(
                    in gridData,
                    muzzlePosition,
                    ownerFaction,
                    targetFaction,
                    ownerUsesFogOfWar,
                    in profile,
                    slot.targetEntity,
                    distributionSettings,
                    disableTargetDistribution,
                    ref pressureByTarget,
                    out Entity bestTarget,
                    out float2 bestTargetPosition,
                    out float bestScore);

                if (currentValid)
                {
                    WeaponTargetPressure currentPressure = default;
                    if (!disableTargetDistribution)
                    {
                        pressureByTarget.TryGetValue(slot.targetEntity, out currentPressure);
                    }
                    bool currentIsPriority = (profile.priorityTargets & currentShipSize) != 0;
                    float currentDistanceSq = math.max(0f, currentEdgeDistance) * math.max(0f, currentEdgeDistance);
                    float currentScore = WeaponTargetDistributionUtility.ScoreCandidate(
                        slot.targetEntity,
                        slot.targetEntity,
                        currentDistanceSq,
                        currentIsPriority,
                        currentPressure,
                        distributionSettings);

                    if (bestTarget == Entity.Null ||
                        (bestTarget != slot.targetEntity && bestScore <= currentScore + distributionSettings.switchScoreThreshold))
                    {
                        bestTarget = slot.targetEntity;
                        bestTargetPosition = currentPosition;
                    }
                }

                if (!AssignSlotTarget(ref slot, bestTarget, bestTargetPosition, estimatedDps, ref pressureByTarget))
                {
                    pressureNeedsRebuild = true;
                }

                if (bestTarget != Entity.Null)
                {
                    slot.findTargetTimer = now + GetStableBetterTargetInterval(shipEntity, i, distributionSettings);
                }
                else
                {
                    slot.findTargetTimer = now + GetStableSlotInterval(shipEntity, i);
                }

                slots[i] = slot;
                hostSlotsChanged = true;
            }

            if (hostSlotsChanged)
            {
                RefreshFireRuntimeIfChanged(shipEntity, slots);
            }
        }

    }

    private static bool HostNeedsWork(
        DynamicBuffer<EmbeddedWeaponSlot> slots,
        float now,
        bool forcedCombat,
        Entity forcedTarget,
        ref EntityStorageInfoLookup entityStorageLookup,
        ref ComponentLookup<LocalTransform> localTransformLookup,
        ref ComponentLookup<Health> healthLookup)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            EmbeddedWeaponSlot slot = slots[i];

            if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
            {
                continue;
            }

            if (slot.findTargetTimer <= now)
            {
                return true;
            }

            if (forcedCombat && slot.targetEntity != forcedTarget)
            {
                return true;
            }

            if (slot.targetEntity == Entity.Null)
            {
                continue;
            }

            if (!entityStorageLookup.Exists(slot.targetEntity) ||
                !localTransformLookup.HasComponent(slot.targetEntity))
            {
                return true;
            }

            if (healthLookup.TryGetComponent(slot.targetEntity, out Health targetHealth) &&
                targetHealth.healthAmount <= 0f)
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildPressureMap(ref SystemState state, ref WeaponProfileDatabaseBlob root, Entity gridEntity)
    {
        int embeddedSlotCount = 0;
        foreach ((RefRO<EmbeddedWeaponHost> _, Entity pressureHostEntity)
                 in SystemAPI.Query<RefRO<EmbeddedWeaponHost>>()
                     .WithAll<EmbeddedWeaponSlot>()
                     .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedWeaponSlot> countSlots = slotsLookup[pressureHostEntity];
            for (int countSlotIndex = 0; countSlotIndex < countSlots.Length; countSlotIndex++)
            {
                if ((EmbeddedWeaponSlotRole)countSlots[countSlotIndex].role == EmbeddedWeaponSlotRole.Damage)
                {
                    embeddedSlotCount++;
                }
            }
        }

        int requiredPressureCapacity = math.max(InitialPressureCapacity, embeddedSlotCount * 2);
        if (!pressureByTarget.IsCreated || pressureByTarget.Capacity < requiredPressureCapacity)
        {
            if (pressureByTarget.IsCreated)
            {
                pressureByTarget.Dispose();
            }

            pressureByTarget = new NativeParallelHashMap<Entity, WeaponTargetPressure>(requiredPressureCapacity, Allocator.Persistent);
        }
        else
        {
            pressureByTarget.Clear();
        }

        foreach ((RefRO<EmbeddedWeaponHost> _, Entity pressureHostEntity)
                 in SystemAPI.Query<RefRO<EmbeddedWeaponHost>>()
                     .WithAll<EmbeddedWeaponSlot>()
                     .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedWeaponSlot> pressureSlots = slotsLookup[pressureHostEntity];
            for (int pressureSlotIndex = 0; pressureSlotIndex < pressureSlots.Length; pressureSlotIndex++)
            {
                EmbeddedWeaponSlot pressureSlot = pressureSlots[pressureSlotIndex];
                if ((EmbeddedWeaponSlotRole)pressureSlot.role != EmbeddedWeaponSlotRole.Damage)
                {
                    continue;
                }

                if (pressureSlot.targetEntity == Entity.Null ||
                    !entityStorageLookup.Exists(pressureSlot.targetEntity) ||
                    !localTransformLookup.HasComponent(pressureSlot.targetEntity))
                {
                    continue;
                }

                AddPressure(ref pressureByTarget, pressureSlot.targetEntity, EstimateSlotDpsOrDefault(ref root, in pressureSlot));
            }
        }

        pressureGridEntity = gridEntity;
        pressureInitialized = true;
        pressureNeedsRebuild = false;
    }

    private void RefreshFireRuntimeIfChanged(Entity entity, DynamicBuffer<EmbeddedWeaponSlot> slots)
    {
        if (!fireRuntimeLookup.HasComponent(entity))
        {
            return;
        }

        EmbeddedWeaponFireRuntime next = EmbeddedWeaponFireRuntimeUtility.Build(slots);
        EmbeddedWeaponFireRuntime current = fireRuntimeLookup[entity];
        if (current.hasDamageSlots != next.hasDamageSlots ||
            current.nextReadyFireTime != next.nextReadyFireTime)
        {
            fireRuntimeLookup[entity] = next;
        }
    }

    private static bool IsSupportedEmbeddedRequestKind(WeaponRequestKind requestKind)
    {
        return requestKind == WeaponRequestKind.Ballistic
            || requestKind == WeaponRequestKind.Rocket
            || requestKind == WeaponRequestKind.Hitscan;
    }

    private static bool IsSupportedEmbeddedFirePattern(WeaponFirePattern firePattern)
    {
        return firePattern == WeaponFirePattern.Single
            || firePattern == WeaponFirePattern.SequentialHardpoints
            || firePattern == WeaponFirePattern.SimultaneousHardpoints;
    }

    // one pass over cells, no temp lists. distance check first, it drops most
    private void FindBestTarget(
        in GridData gridData,
        float2 muzzlePosition,
        Faction ownerFaction,
        Faction targetFaction,
        bool ownerUsesFogOfWar,
        in WeaponProfileBlob profile,
        Entity currentTarget,
        WeaponTargetDistributionSettings settings,
        bool disableTargetDistribution,
        ref NativeParallelHashMap<Entity, WeaponTargetPressure> pressureByTarget,
        out Entity bestTarget,
        out float2 bestTargetPosition,
        out float bestScore)
    {
        bestTarget = Entity.Null;
        bestTargetPosition = float2.zero;
        bestScore = float.NegativeInfinity;

        NativeParallelMultiHashMap<int2, Grid> map =
            CombatUtility.GetEntityMap(in gridData, targetFaction);

        float range = profile.attackDistance;
        int2 gridMin = GridUtility.WorldToSmallCell(muzzlePosition - range);
        int2 gridMax = GridUtility.WorldToSmallCell(muzzlePosition + range);

        for (int y = gridMin.y; y <= gridMax.y; y++)
        {
            for (int x = gridMin.x; x <= gridMax.x; x++)
            {
                if (!map.TryGetFirstValue(
                        new int2(x, y),
                        out Grid candidate,
                        out NativeParallelMultiHashMapIterator<int2> iter))
                {
                    continue;
                }

                do
                {
                    if (candidate.Entity == Entity.Null || !entityStorageLookup.Exists(candidate.Entity))
                    {
                        continue;
                    }

                    float maxRadius = math.max(candidate.CollisionRadius.x, candidate.CollisionRadius.y);
                    float reach = range + maxRadius;
                    if (math.distancesq(candidate.Position, muzzlePosition) > reach * reach)
                    {
                        continue;
                    }

                    if (!EmbeddedWeaponTargetValidationUtility.IsGridCandidateValidForSlot(
                            candidate,
                            muzzlePosition,
                            ownerFaction,
                            targetFaction,
                            ownerUsesFogOfWar,
                            in profile,
                            ref localTransformLookup,
                            ref unitLookup,
                            ref visibilityLookup,
                            ref healthLookup,
                            out float edgeDistance,
                            out float2 candidatePosition))
                    {
                        continue;
                    }

                    WeaponTargetPressure pressure = default;
                    if (!disableTargetDistribution)
                    {
                        pressureByTarget.TryGetValue(candidate.Entity, out pressure);
                    }

                    bool isPriority = (profile.priorityTargets & candidate.ShipSize) != 0;
                    float distanceSq = math.max(0f, edgeDistance) * math.max(0f, edgeDistance);
                    float score = WeaponTargetDistributionUtility.ScoreCandidate(
                        candidate.Entity,
                        currentTarget,
                        distanceSq,
                        isPriority,
                        pressure,
                        settings);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTarget = candidate.Entity;
                        bestTargetPosition = candidatePosition;
                    }
                }
                while (map.TryGetNextValue(out candidate, ref iter));
            }
        }
    }

    private static bool TryGetMuzzlePositionXY(
        DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints,
        float3 shipPos,
        quaternion shipRot,
        in EmbeddedWeaponSlot slot,
        WeaponFirePattern firePattern,
        out float2 muzzlePosition)
    {
        muzzlePosition = float2.zero;

        float3 muzzleLocalOffset = slot.muzzleLocalOffset;
        int count = math.max(1, slot.hardpointCount);
        int relativeIndex = firePattern == WeaponFirePattern.SequentialHardpoints
            ? slot.nextHardpointIndex
            : 0;

        if ((uint)relativeIndex >= (uint)count)
        {
            relativeIndex = 0;
        }

        int absoluteIndex = slot.firstHardpointIndex + relativeIndex;
        if ((uint)absoluteIndex >= (uint)hardpoints.Length)
        {
            return false;
        }

        muzzleLocalOffset = hardpoints[absoluteIndex].muzzleLocalOffset;

        quaternion slotWorldRot = math.mul(shipRot, quaternion.RotateZ(slot.currentLocalAngle));
        float3 pivotWorld = shipPos + math.rotate(shipRot, slot.pivotLocalPosition);
        float3 muzzleWorld = pivotWorld + math.rotate(slotWorldRot, muzzleLocalOffset);

        muzzlePosition = muzzleWorld.xy;
        return true;
    }

    private static float EstimateSlotDps(in WeaponProfileBlob profile, in EmbeddedWeaponSlot slot)
    {
        float shotsPerFire = 1f;
        if ((WeaponFirePattern)profile.firePattern == WeaponFirePattern.SimultaneousHardpoints)
        {
            shotsPerFire = math.max(1, slot.hardpointCount);
        }

        return math.max(0.1f, profile.damageAmount * shotsPerFire / math.max(0.05f, profile.reloadTime));
    }

    private static float EstimateSlotDpsOrDefault(ref WeaponProfileDatabaseBlob root, in EmbeddedWeaponSlot slot)
    {
        if ((uint)slot.profileIndex >= (uint)root.Profiles.Length)
        {
            return 0.1f;
        }

        ref WeaponProfileBlob profile = ref root.Profiles[slot.profileIndex];
        return EstimateSlotDps(in profile, in slot);
    }

    private static bool AssignSlotTarget(
        ref EmbeddedWeaponSlot slot,
        Entity target,
        float2 targetPosition,
        float dps,
        ref NativeParallelHashMap<Entity, WeaponTargetPressure> map)
    {
        bool pressureOk = true;
        if (slot.targetEntity != target)
        {
            RemovePressure(ref map, slot.targetEntity, dps);
            pressureOk = AddPressure(ref map, target, dps);
        }

        slot.targetEntity = target;
        slot.targetPositionWorld = target == Entity.Null ? float2.zero : targetPosition;
        return pressureOk;
    }

    private static void ClearSlotTarget(
        ref EmbeddedWeaponSlot slot,
        float dps,
        ref NativeParallelHashMap<Entity, WeaponTargetPressure> map)
    {
        AssignSlotTarget(ref slot, Entity.Null, float2.zero, dps, ref map);
    }

    private static bool AddPressure(ref NativeParallelHashMap<Entity, WeaponTargetPressure> map, Entity target, float dps)
    {
        if (target == Entity.Null)
        {
            return true;
        }

        WeaponTargetPressure pressure = default;
        if (!map.TryGetValue(target, out pressure) && map.Count() >= map.Capacity)
        {
            return false;
        }

        pressure.assignedTurrets++;
        pressure.assignedDps += math.max(0.1f, dps);
        map[target] = pressure;
        return true;
    }

    private static void RemovePressure(ref NativeParallelHashMap<Entity, WeaponTargetPressure> map, Entity target, float dps)
    {
        if (target == Entity.Null || !map.TryGetValue(target, out WeaponTargetPressure pressure))
        {
            return;
        }

        pressure.assignedTurrets = math.max(0, pressure.assignedTurrets - 1);
        pressure.assignedDps = math.max(0f, pressure.assignedDps - math.max(0.1f, dps));
        if (pressure.assignedTurrets == 0)
        {
            map.Remove(target);
            return;
        }

        map[target] = pressure;
    }

    private static float GetStableBetterTargetInterval(
        Entity shipEntity,
        int slotIndex,
        WeaponTargetDistributionSettings settings)
    {
        float minDelay = math.max(DefaultEmbeddedFindTargetInterval, settings.retargetIntervalMin);
        float maxDelay = math.max(minDelay, math.max(settings.retargetIntervalMax, DefaultEmbeddedBetterTargetInterval));

        uint hash = math.hash(new uint3(
            (uint)shipEntity.Index,
            (uint)shipEntity.Version,
            (uint)(slotIndex + 317)));

        float t = (hash & 1023u) / 1023f;
        return math.lerp(minDelay, maxDelay, t);
    }

    private static void ResetSlotTargetAndThrottle(
        ref EmbeddedWeaponSlot slot,
        float now,
        Entity shipEntity,
        int slotIndex,
        float dps,
        ref NativeParallelHashMap<Entity, WeaponTargetPressure> map)
    {
        ClearSlotTarget(ref slot, dps, ref map);
        slot.findTargetTimer = now + GetStableSlotInterval(shipEntity, slotIndex);
    }

    private static float GetStableBudgetRetryDelay(Entity shipEntity, int slotIndex)
    {
        uint hash = math.hash(new uint3(
            (uint)shipEntity.Index,
            (uint)shipEntity.Version,
            (uint)(slotIndex + 15731)));

        float t = (hash & 1023u) / 1023f;
        return math.lerp(BudgetRetryDelayMin, BudgetRetryDelayMax, t);
    }

    private static float GetStableSlotInterval(Entity shipEntity, int slotIndex)
    {
        uint hash = math.hash(new uint3(
            (uint)shipEntity.Index,
            (uint)shipEntity.Version,
            (uint)slotIndex));

        float t = (hash & 1023u) / 1023f;
        return DefaultEmbeddedFindTargetInterval * math.lerp(0.75f, 1.25f, t);
    }

    // write only if something to clear, idle ships don't dirty buffer
    private static bool ClearTargetsIfNeeded(
        DynamicBuffer<EmbeddedWeaponSlot> slots,
        ref WeaponProfileDatabaseBlob root,
        ref NativeParallelHashMap<Entity, WeaponTargetPressure> map)
    {
        bool changed = false;
        for (int i = 0; i < slots.Length; i++)
        {
            EmbeddedWeaponSlot slot = slots[i];
            if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
            {
                continue;
            }

            if (slot.targetEntity == Entity.Null && math.all(slot.targetPositionWorld == float2.zero))
            {
                continue;
            }

            ClearSlotTarget(ref slot, EstimateSlotDpsOrDefault(ref root, in slot), ref map);
            slots[i] = slot;
            changed = true;
        }

        return changed;
    }
}
