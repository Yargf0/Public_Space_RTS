using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedFindTargetSystem))]
[UpdateAfter(typeof(EmbeddedWeaponAimSystem))]
[UpdateAfter(typeof(SearchlightSpotSystem))]
[UpdateAfter(typeof(MoveVelocitySystem))]
[UpdateAfter(typeof(RotateToMovementSystem))]
[UpdateBefore(typeof(BallisticFireRequestExecutionSystem))]
[UpdateBefore(typeof(RocketFireRequestExecutionSystem))]
[UpdateBefore(typeof(HitscanFireRequestExecutionSystem))]
public partial struct EmbeddedWeaponFireRequestBuildSystem : ISystem
{
    private ComponentLookup<Velocity> velocityLookup;
    private ComponentLookup<Unit> unitLookup;
    private ComponentLookup<ShipStateComponent> shipStateLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<UseFogOfWar> useFogOfWarLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private ComponentLookup<Visibility> visibilityLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<UnitCollisionRadius> collisionRadiusLookup;
    private ComponentLookup<Health> healthLookup;
    private BufferLookup<EmbeddedWeaponSlot> slotsLookup;
    private BufferLookup<EmbeddedWeaponHardpoint> hardpointsLookup;
    private EntityQuery ballisticQueueQuery;
    private EntityQuery rocketQueueQuery;
    private EntityQuery hitscanQueueQuery;
    private EntityQuery pressureRebuildRequestQuery;

    public void OnCreate(ref SystemState state)
    {
        velocityLookup = state.GetComponentLookup<Velocity>(true);
        unitLookup = state.GetComponentLookup<Unit>(true);
        shipStateLookup = state.GetComponentLookup<ShipStateComponent>(true);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(true);
        useFogOfWarLookup = state.GetComponentLookup<UseFogOfWar>(true);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(true);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(true);
        visibilityLookup = state.GetComponentLookup<Visibility>(true);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        collisionRadiusLookup = state.GetComponentLookup<UnitCollisionRadius>(true);
        healthLookup = state.GetComponentLookup<Health>(true);
        slotsLookup = state.GetBufferLookup<EmbeddedWeaponSlot>(false);
        hardpointsLookup = state.GetBufferLookup<EmbeddedWeaponHardpoint>(true);
        ballisticQueueQuery = state.GetEntityQuery(ComponentType.ReadOnly<WeaponFireRequestQueueSingleton>(), ComponentType.ReadWrite<BallisticWeaponFireRequestElement>());
        rocketQueueQuery = state.GetEntityQuery(ComponentType.ReadOnly<WeaponFireRequestQueueSingleton>(), ComponentType.ReadWrite<RocketWeaponFireRequestElement>());
        hitscanQueueQuery = state.GetEntityQuery(ComponentType.ReadOnly<WeaponFireRequestQueueSingleton>(), ComponentType.ReadWrite<HitscanWeaponFireRequestElement>());
        pressureRebuildRequestQuery = state.GetEntityQuery(ComponentType.ReadOnly<EmbeddedWeaponPressureRebuildRequest>());
        state.RequireForUpdate<WeaponProfileDatabase>();
        state.RequireForUpdate<WeaponFireRequestQueueSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<WeaponProfileDatabase>(out WeaponProfileDatabase database))
        {
            return;
        }

        velocityLookup.Update(ref state);
        unitLookup.Update(ref state);
        shipStateLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        useFogOfWarLookup.Update(ref state);
        empStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);
        visibilityLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        collisionRadiusLookup.Update(ref state);
        healthLookup.Update(ref state);
        slotsLookup.Update(ref state);
        hardpointsLookup.Update(ref state);

        ref WeaponProfileDatabaseBlob root = ref database.Value.Value;
        // timers are absolute, 0 = ready. don't decrease every frame, it dirties buffer
        float now = (float)SystemAPI.Time.ElapsedTime;
        if (!ballisticQueueQuery.TryGetSingletonBuffer<BallisticWeaponFireRequestElement>(out DynamicBuffer<BallisticWeaponFireRequestElement> ballisticRequests) ||
            !rocketQueueQuery.TryGetSingletonBuffer<RocketWeaponFireRequestElement>(out DynamicBuffer<RocketWeaponFireRequestElement> rocketRequests) ||
            !hitscanQueueQuery.TryGetSingletonBuffer<HitscanWeaponFireRequestElement>(out DynamicBuffer<HitscanWeaponFireRequestElement> hitscanRequests))
        {
            return;
        }

        ballisticRequests.Clear();
        rocketRequests.Clear();
        hitscanRequests.Clear();

        EntityCommandBuffer vfxEcb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        bool needsPressureRebuild = false;

        foreach ((RefRO<LocalTransform> shipLocal,
                  RefRW<EmbeddedWeaponFireRuntime> fireRuntime,
                  Entity shipEntity)
            in SystemAPI.Query<RefRO<LocalTransform>, RefRW<EmbeddedWeaponFireRuntime>>()
                .WithAll<EmbeddedWeaponHost>()
                .WithAll<EmbeddedWeaponSlot>()
                .WithAll<EmbeddedWeaponHardpoint>()
                .WithEntityAccess())
        {
            if (fireRuntime.ValueRO.hasDamageSlots == 0 ||
                fireRuntime.ValueRO.nextReadyFireTime > now)
            {
                continue;
            }

            DynamicBuffer<EmbeddedWeaponSlot> slots = slotsLookup[shipEntity];
            if (!HasReadyDamageSlot(slots, now))
            {
                RefreshFireRuntimeIfChanged(fireRuntime, slots);
                continue;
            }

            if (EmbeddedActionStatusUtility.AreWeaponsDisabled(shipEntity, ref empStatusLookup, ref debuffStatusLookup))
            {
                continue;
            }

            if (!unitLookup.TryGetComponent(shipEntity, out Unit ownerUnit) ||
                !shipStateLookup.TryGetComponent(shipEntity, out ShipStateComponent shipState))
            {
                continue;
            }

            DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints = hardpointsLookup[shipEntity];
            bool wasHit = false;
            if (shipAgroLookup.TryGetComponent(shipEntity, out ShipAgro agro))
            {
                wasHit = agro.wasHit;
            }

            bool hasForcedTarget = shipState.forcedTarget != Entity.Null;
            bool fireAllowed = (hasForcedTarget && shipState.currentState == ShipState.InCombat)
                            || shipState.mode == FireMode.FireAtWill
                            || (shipState.mode == FireMode.ReturnFire && wasHit);

            Faction ownerFaction = ownerUnit.faction;
            Faction targetFaction = VisibilityUtility.Opposite(ownerFaction);
            bool ownerUsesFogOfWar = useFogOfWarLookup.HasComponent(shipEntity);
            quaternion shipRot = shipLocal.ValueRO.Rotation;
            float3 shipPos = shipLocal.ValueRO.Position;
            float2 ownerVelocity = float2.zero;
            if (velocityLookup.TryGetComponent(shipEntity, out Velocity ownerVelocityComponent))
            {
                ownerVelocity = ownerVelocityComponent.velocity;
            }

            bool slotsChanged = false;
            for (int i = 0; i < slots.Length; i++)
            {
                EmbeddedWeaponSlot slot = slots[i];

                if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
                {
                    continue;
                }

                // Not ready = no write to slots[i].
                if (!fireAllowed || slot.targetEntity == Entity.Null || !IsSlotReady(in slot, now))
                {
                    continue;
                }

                if ((uint)slot.profileIndex >= (uint)root.Profiles.Length)
                {
                    continue;
                }

                ref WeaponProfileBlob profile = ref root.Profiles[slot.profileIndex];
                WeaponFirePattern firePattern = (WeaponFirePattern)profile.firePattern;
                if (!IsSupportedEmbeddedRequestKind((WeaponRequestKind)profile.requestKind) || !IsSupportedEmbeddedFirePattern(firePattern))
                {
                    continue;
                }

                if (firePattern == WeaponFirePattern.SimultaneousHardpoints)
                {
                    int hardpointCount = math.max(1, slot.hardpointCount);
                    int createdCount = 0;
                    float2 lastTargetPosition = slot.targetPositionWorld;

                    for (int hardpointIndex = 0; hardpointIndex < hardpointCount; hardpointIndex++)
                    {
                        if (!TryGetHardpointOffsetByRelativeIndex(hardpoints, in slot, hardpointIndex, out float3 simultaneousMuzzleOffset, out int simultaneousUsedHardpointIndex))
                        {
                            continue;
                        }

                        EmbeddedWeaponSlot shotSlot = slot;
                        shotSlot.shotCounter = slot.shotCounter + (uint)createdCount;

                        bool created = TryBuildRequest(
                            shipEntity,
                            in shipRot,
                            shipPos,
                            in shotSlot,
                            simultaneousMuzzleOffset,
                            simultaneousUsedHardpointIndex,
                            in profile,
                            ownerFaction,
                            targetFaction,
                            ownerUsesFogOfWar,
                            ownerVelocity,
                            ref localTransformLookup,
                            ref unitLookup,
                            ref visibilityLookup,
                            ref collisionRadiusLookup,
                            ref healthLookup,
                            ref velocityLookup,
                            out WeaponFireRequest fireRequest,
                            out float2 targetPosition);

                        if (created)
                        {
                            AddRequestToQueue(in fireRequest, ballisticRequests, rocketRequests, hitscanRequests);
                            CombatVfxRequestUtility.EnqueueMuzzleFlash(ref vfxEcb, in fireRequest, in profile);
                            createdCount++;
                            lastTargetPosition = targetPosition;
                        }
                    }

                    if (createdCount == 0)
                    {
                        if (ClearSlotTarget(ref slot))
                        {
                            slots[i] = slot;
                            slotsChanged = true;
                            needsPressureRebuild = true;
                        }

                        continue;
                    }

                    slot.targetPositionWorld = lastTargetPosition;
                    slot.shotCounter += (uint)createdCount;
                    slot.rngState = math.hash(new uint2(slot.rngState, slot.shotCounter)) | 1u;
                    ApplyPatternAfterShot(ref slot, in profile, firePattern, now);
                    slots[i] = slot;
                    slotsChanged = true;
                    continue;
                }

                if (!TryGetNextHardpointOffset(hardpoints, in slot, firePattern, out float3 muzzleLocalOffset, out int usedHardpointIndex))
                {
                    continue;
                }

                bool createdSingleOrSequential = TryBuildRequest(
                    shipEntity,
                    in shipRot,
                    shipPos,
                    in slot,
                    muzzleLocalOffset,
                    usedHardpointIndex,
                    in profile,
                    ownerFaction,
                    targetFaction,
                    ownerUsesFogOfWar,
                    ownerVelocity,
                    ref localTransformLookup,
                    ref unitLookup,
                    ref visibilityLookup,
                    ref collisionRadiusLookup,
                    ref healthLookup,
                    ref velocityLookup,
                    out WeaponFireRequest singleFireRequest,
                    out float2 singleTargetPosition);

                if (!createdSingleOrSequential)
                {
                    if (ClearSlotTarget(ref slot))
                    {
                        slots[i] = slot;
                        slotsChanged = true;
                        needsPressureRebuild = true;
                    }

                    continue;
                }

                AddRequestToQueue(in singleFireRequest, ballisticRequests, rocketRequests, hitscanRequests);
                CombatVfxRequestUtility.EnqueueMuzzleFlash(ref vfxEcb, in singleFireRequest, in profile);

                slot.targetPositionWorld = singleTargetPosition;
                slot.shotCounter++;
                slot.rngState = math.hash(new uint2(slot.rngState, slot.shotCounter)) | 1u;
                ApplyPatternAfterShot(ref slot, in profile, firePattern, now);
                slots[i] = slot;
                slotsChanged = true;
            }

            if (slotsChanged)
            {
                RefreshFireRuntimeIfChanged(fireRuntime, slots);
            }
        }

        vfxEcb.Playback(state.EntityManager);

        if (needsPressureRebuild)
        {
            RequestPressureRebuild(ref state);
        }
    }

    private static bool TryBuildRequest(
        Entity shipEntity,
        in quaternion shipRot,
        float3 shipPos,
        in EmbeddedWeaponSlot slot,
        float3 muzzleLocalOffset,
        int usedHardpointIndex,
        in WeaponProfileBlob profile,
        Faction ownerFaction,
        Faction targetFaction,
        bool ownerUsesFogOfWar,
        float2 ownerVelocity,
        ref ComponentLookup<LocalTransform> localTransformLookup,
        ref ComponentLookup<Unit> unitLookup,
        ref ComponentLookup<Visibility> visibilityLookup,
        ref ComponentLookup<UnitCollisionRadius> collisionRadiusLookup,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<Velocity> velocityLookup,
        out WeaponFireRequest fireRequest,
        out float2 targetPosition)
    {
        fireRequest = default;
        targetPosition = float2.zero;

        quaternion slotWorldRot = math.mul(shipRot, quaternion.RotateZ(slot.currentLocalAngle));
        float3 pivotWorld = shipPos + math.rotate(shipRot, slot.pivotLocalPosition);
        float3 muzzleWorld = pivotWorld + math.rotate(slotWorldRot, muzzleLocalOffset);
        muzzleWorld.z = GameConstants.ProjectileZ;

        float2 muzzleFwd = math.normalizesafe(
            math.mul(slotWorldRot, new float3(0f, 1f, 0f)).xy,
            new float2(0f, 1f));

        if (!EmbeddedWeaponTargetValidationUtility.IsTargetValidForSlot(
                slot.targetEntity,
                muzzleWorld.xy,
                ownerFaction,
                targetFaction,
                ownerUsesFogOfWar,
                in profile,
                ref localTransformLookup,
                ref unitLookup,
                ref visibilityLookup,
                ref collisionRadiusLookup,
                ref healthLookup,
                out targetPosition,
                out _,
                out _))
        {
            return false;
        }

        float2 targetVelocity = float2.zero;
        if (velocityLookup.TryGetComponent(slot.targetEntity, out Velocity targetVelocityComponent))
        {
            targetVelocity = targetVelocityComponent.velocity;
        }

        float2 desiredAimDirection = GetBaseShotDirection(
            in profile,
            muzzleFwd,
            muzzleWorld.xy,
            targetPosition,
            ownerVelocity,
            targetVelocity);

        if (!CanFireWithCurrentMuzzleForward(in profile, muzzleFwd, desiredAimDirection, muzzleWorld.xy, targetPosition))
        {
            return false;
        }

        uint shotSeed = math.hash(new uint4(
            math.max(1u, slot.rngState),
            (uint)shipEntity.Index,
            (uint)usedHardpointIndex,
            slot.shotCounter));
        shotSeed = math.max(1u, shotSeed);

        WeaponRequestKind requestKind = (WeaponRequestKind)profile.requestKind;
        float2 baseShotDirection = requestKind == WeaponRequestKind.Hitscan ? desiredAimDirection : muzzleFwd;
        float2 shotDirection = ApplySpread(baseShotDirection, profile.spreadAngle, shotSeed);

        fireRequest = new WeaponFireRequest
        {
            mountEntity = shipEntity,
            ownerEntity = shipEntity,
            ammoEntity = slot.ammoEntity,
            targetEntity = slot.targetEntity,
            targetFaction = targetFaction,
            ownerFaction = ownerFaction,
            ownerUsesFogOfWar = ownerUsesFogOfWar,
            profileIndex = slot.profileIndex,
            requestKind = profile.requestKind,
            spawnPosition = muzzleWorld,
            direction = math.normalizesafe(shotDirection, new float2(0f, 1f)),
            ownerVelocity = ownerVelocity,
            targetPosition = targetPosition,
            randomSeed = shotSeed,
        };

        return true;
    }

    private static void AddRequestToQueue(
        in WeaponFireRequest request,
        DynamicBuffer<BallisticWeaponFireRequestElement> ballisticRequests,
        DynamicBuffer<RocketWeaponFireRequestElement> rocketRequests,
        DynamicBuffer<HitscanWeaponFireRequestElement> hitscanRequests)
    {
        switch ((WeaponRequestKind)request.requestKind)
        {
            case WeaponRequestKind.Ballistic:
                ballisticRequests.Add(new BallisticWeaponFireRequestElement { Value = request });
                break;
            case WeaponRequestKind.Rocket:
                rocketRequests.Add(new RocketWeaponFireRequestElement { Value = request });
                break;
            case WeaponRequestKind.Hitscan:
                hitscanRequests.Add(new HitscanWeaponFireRequestElement { Value = request });
                break;
        }
    }

    private static bool TryGetNextHardpointOffset(
        DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints,
        in EmbeddedWeaponSlot slot,
        WeaponFirePattern firePattern,
        out float3 muzzleLocalOffset,
        out int usedHardpointIndex)
    {
        int count = math.max(1, slot.hardpointCount);
        int relativeIndex = firePattern == WeaponFirePattern.SequentialHardpoints ? slot.nextHardpointIndex : 0;
        if ((uint)relativeIndex >= (uint)count)
        {
            relativeIndex = 0;
        }

        return TryGetHardpointOffsetByRelativeIndex(
            hardpoints,
            in slot,
            relativeIndex,
            out muzzleLocalOffset,
            out usedHardpointIndex);
    }

    private static bool TryGetHardpointOffsetByRelativeIndex(
        DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints,
        in EmbeddedWeaponSlot slot,
        int relativeIndex,
        out float3 muzzleLocalOffset,
        out int usedHardpointIndex)
    {
        muzzleLocalOffset = slot.muzzleLocalOffset;
        usedHardpointIndex = 0;

        int count = math.max(1, slot.hardpointCount);
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
        usedHardpointIndex = relativeIndex;
        return true;
    }

    private static void ApplyPatternAfterShot(ref EmbeddedWeaponSlot slot, in WeaponProfileBlob profile, WeaponFirePattern firePattern, float now)
    {
        if (firePattern == WeaponFirePattern.SequentialHardpoints)
        {
            int hardpointCount = math.max(1, slot.hardpointCount);
            int next = slot.nextHardpointIndex + 1;
            if (next >= hardpointCount)
            {
                slot.nextHardpointIndex = 0;
                slot.patternTimer = 0f;
                slot.cooldownTimer = now + math.max(0f, profile.reloadTime);
            }
            else
            {
                slot.nextHardpointIndex = next;
                slot.patternTimer = now + math.max(0f, profile.burstInterval);
                slot.cooldownTimer = 0f;
            }

            return;
        }

        slot.nextHardpointIndex = 0;
        slot.patternTimer = 0f;
        slot.cooldownTimer = now + math.max(0f, profile.reloadTime);
    }

    private static bool IsSupportedEmbeddedRequestKind(WeaponRequestKind requestKind)
    {
        return requestKind == WeaponRequestKind.Ballistic
            || requestKind == WeaponRequestKind.Rocket
            || requestKind == WeaponRequestKind.Hitscan;
    }

    private static bool HasReadyDamageSlot(DynamicBuffer<EmbeddedWeaponSlot> slots, float now)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            EmbeddedWeaponSlot slot = slots[i];
            if ((EmbeddedWeaponSlotRole)slot.role == EmbeddedWeaponSlotRole.Damage &&
                slot.targetEntity != Entity.Null &&
                IsSlotReady(in slot, now))
            {
                return true;
            }
        }

        return false;
    }

    private static void RefreshFireRuntimeIfChanged(
        RefRW<EmbeddedWeaponFireRuntime> fireRuntime,
        DynamicBuffer<EmbeddedWeaponSlot> slots)
    {
        EmbeddedWeaponFireRuntime next = EmbeddedWeaponFireRuntimeUtility.Build(slots);
        if (fireRuntime.ValueRO.hasDamageSlots != next.hasDamageSlots ||
            fireRuntime.ValueRO.nextReadyFireTime != next.nextReadyFireTime)
        {
            fireRuntime.ValueRW = next;
        }
    }

    private static bool ClearSlotTarget(ref EmbeddedWeaponSlot slot)
    {
        if (slot.targetEntity == Entity.Null && math.all(slot.targetPositionWorld == float2.zero))
        {
            return false;
        }

        slot.targetEntity = Entity.Null;
        slot.targetPositionWorld = float2.zero;
        return true;
    }

    private void RequestPressureRebuild(ref SystemState state)
    {
        if (!pressureRebuildRequestQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        Entity requestEntity = ecb.CreateEntity();
        ecb.AddComponent<EmbeddedWeaponPressureRebuildRequest>(requestEntity);
        ecb.Playback(state.EntityManager);
    }

    private static bool IsSupportedEmbeddedFirePattern(WeaponFirePattern firePattern)
    {
        return firePattern == WeaponFirePattern.Single
            || firePattern == WeaponFirePattern.SequentialHardpoints
            || firePattern == WeaponFirePattern.SimultaneousHardpoints;
    }

    private static bool IsSlotReady(in EmbeddedWeaponSlot slot, float now)
    {
        return slot.cooldownTimer <= now && slot.patternTimer <= now;
    }

    private static bool CanFireWithCurrentMuzzleForward(
        in WeaponProfileBlob profile,
        float2 muzzleForward,
        float2 desiredAimDirection,
        float2 spawnPosition,
        float2 targetPosition)
    {
        if (!profile.rotate)
        {
            const float FixedWeaponConeDegrees = 31.788f;
            float2 toTarget = math.normalizesafe(targetPosition - spawnPosition, muzzleForward);
            return CombatUtility.IsDirectionAlignedWithWeaponForward(muzzleForward, toTarget, FixedWeaponConeDegrees);
        }

        if (CombatUtility.HasLimitedWeaponRotation(in profile))
        {
            return CombatUtility.IsDirectionAlignedWithWeaponForward(muzzleForward, desiredAimDirection);
        }

        return true;
    }

    private static float2 GetBaseShotDirection(
        in WeaponProfileBlob profile,
        float2 forward,
        float2 spawnPosition,
        float2 targetPosition,
        float2 ownerVelocity,
        float2 targetVelocity)
    {
        float2 normalizedForward = math.normalizesafe(forward, new float2(0f, 1f));

        if ((WeaponRequestKind)profile.requestKind == WeaponRequestKind.Hitscan)
        {
            return math.normalizesafe(targetPosition - spawnPosition, normalizedForward);
        }

        if (!profile.rotate)
        {
            return normalizedForward;
        }

        return WeaponAimUtility.ResolveProjectileDirection(
            spawnPosition,
            targetPosition,
            ownerVelocity,
            targetVelocity,
            profile.projectileSpeed,
            normalizedForward);
    }

    private static float2 ApplySpread(float2 forward, float spreadAngle, uint randomSeed)
    {
        float2 normalizedForward = math.normalizesafe(forward, new float2(0f, 1f));
        if (spreadAngle <= 0.001f)
        {
            return normalizedForward;
        }

        Random rng = Random.CreateFromIndex(math.max(1u, randomSeed));
        float offsetRad = math.radians(rng.NextFloat(-spreadAngle, spreadAngle));
        float sin = math.sin(offsetRad);
        float cos = math.cos(offsetRad);
        float2 spreadDirection = new float2(
            normalizedForward.x * cos - normalizedForward.y * sin,
            normalizedForward.x * sin + normalizedForward.y * cos);
        return math.normalizesafe(spreadDirection, normalizedForward);
    }
}
