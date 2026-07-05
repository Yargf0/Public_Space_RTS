using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct EmbeddedActionEffectAccumulator
{
    public float damage;
    public float repair;
    public float shieldRestore;
    public float shieldCap;
}

public static class EmbeddedActionRuntimeUtility
{

    public const float NoScheduledActionWork = 3.40282347e38f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsActionHostWorkDue(float nextWorkTime, float now)
    {
        return now + 0.0001f >= nextWorkTime;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetStableActionOffset(Entity entity, int slotIndex, float maxSeconds)
    {
        if (maxSeconds <= 0f)
        {
            return 0f;
        }

        uint hash = (uint)entity.Index * 747796405u;
        hash ^= (uint)entity.Version * 2891336453u;
        hash ^= (uint)slotIndex * 277803737u;
        hash ^= hash >> 16;
        hash *= 2246822519u;
        hash ^= hash >> 13;

        return ((hash & 1023u) / 1023f) * maxSeconds;
    }

    public static EmbeddedActionHostRuntime BuildHostRuntimeFromActions(DynamicBuffer<EmbeddedActionSlot> actions)
    {
        EmbeddedActionHostRuntime runtime = new EmbeddedActionHostRuntime
        {
            nextTargetWorkTime = NoScheduledActionWork,
            nextBeamWorkTime = NoScheduledActionWork,
            nextAuraWorkTime = NoScheduledActionWork,
            nextVisualWorkTime = NoScheduledActionWork,
            hasBeamActions = 0,
            hasAuraActions = 0,
        };

        RefreshHostRuntimeFromActions(ref runtime, actions);
        return runtime;
    }

    public static void RefreshHostRuntimeFromActions(ref EmbeddedActionHostRuntime runtime, DynamicBuffer<EmbeddedActionSlot> actions)
    {
        runtime.nextTargetWorkTime = NoScheduledActionWork;
        runtime.nextBeamWorkTime = NoScheduledActionWork;
        runtime.nextAuraWorkTime = NoScheduledActionWork;
        runtime.hasBeamActions = 0;
        runtime.hasAuraActions = 0;

        for (int i = 0; i < actions.Length; i++)
        {
            EmbeddedActionSlot action = actions[i];

            if (IsBeamAction(in action))
            {
                runtime.hasBeamActions = 1;
                runtime.nextTargetWorkTime = math.min(runtime.nextTargetWorkTime, action.searchTimer);

                float nextBeam = action.timer;

                // don't wake BeamSystem by visualTimer when there is no visual prefab
                // or invisible support beams keep scheduling work
                if (action.visualPrefabEntity != Entity.Null)
                {
                    nextBeam = math.min(nextBeam, action.visualTimer);
                }

                if ((action.flags & EmbeddedActionSlotFlags.Rotate) != 0)
                {
                    nextBeam = math.min(nextBeam, action.aimTimer);
                }

                runtime.nextBeamWorkTime = math.min(runtime.nextBeamWorkTime, nextBeam);
            }
            else if (IsAuraAction(in action))
            {
                runtime.hasAuraActions = 1;

                float nextAura = action.timer;

                // same rule for aura
                if (action.visualPrefabEntity != Entity.Null)
                {
                    nextAura = math.min(nextAura, action.visualTimer);
                }

                runtime.nextAuraWorkTime = math.min(runtime.nextAuraWorkTime, nextAura);
            }
        }
    }

    public static void RefreshHostVisualWorkTime(ref EmbeddedActionHostRuntime runtime, DynamicBuffer<EmbeddedActionVisualRuntime> visuals)
    {
        float nextVisual = NoScheduledActionWork;

        for (int i = 0; i < visuals.Length; i++)
        {
            EmbeddedActionVisualRuntime visual = visuals[i];
            if ((visual.flags & EmbeddedActionVisualRuntimeFlags.Visible) == 0 || visual.visualEntity == Entity.Null)
            {
                continue;
            }

            if ((visual.flags & EmbeddedActionVisualRuntimeFlags.Dirty) != 0)
            {
                nextVisual = 0f;
                break;
            }

            nextVisual = math.min(nextVisual, visual.visibleUntil);
        }

        runtime.nextVisualWorkTime = nextVisual;
    }

    public static void MarkHostVisualWorkDue(ref EmbeddedActionHostRuntime runtime, float now)
    {
        runtime.nextVisualWorkTime = math.min(runtime.nextVisualWorkTime, now);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBeamAction(in EmbeddedActionSlot action)
    {
        return (EmbeddedActionDeliveryKind)action.deliveryKind == EmbeddedActionDeliveryKind.BeamOverTime &&
               EmbeddedActionRulesUtility.IsSupportedAction(in action);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAuraAction(in EmbeddedActionSlot action)
    {
        return (EmbeddedActionDeliveryKind)action.deliveryKind == EmbeddedActionDeliveryKind.Aura &&
               EmbeddedActionRulesUtility.IsSupportedAction(in action);
    }

    public static bool IsValidActionTarget(
        Entity owner,
        Entity targetEntity,
        float2 pivotWorld,
        Faction ownerFaction,
        bool ownerUsesFogOfWar,
        EmbeddedActionTargetFilter targetFilter,
        EmbeddedActionEffectKind effectKind,
        in EmbeddedActionSlot action,
        ref ComponentLookup<Unit> unitLookupRef,
        ref ComponentLookup<Health> healthLookupRef,
        ref ComponentLookup<LocalTransform> localTransformLookupRef,
        ref ComponentLookup<UnitCollisionRadius> collisionRadiusLookupRef,
        ref ComponentLookup<Visibility> visibilityLookupRef,
        out float2 targetPosition,
        out float edgeDistance)
    {
        targetPosition = float2.zero;
        edgeDistance = float.MaxValue;

        if (targetEntity == Entity.Null)
        {
            return false;
        }

        bool canTargetSelf = (action.flags & EmbeddedActionSlotFlags.CanTargetSelf) != 0 ||
                             targetFilter == EmbeddedActionTargetFilter.Self;
        if (!canTargetSelf && targetEntity == owner)
        {
            return false;
        }

        if (!unitLookupRef.TryGetComponent(targetEntity, out Unit targetUnit))
        {
            return false;
        }

        switch (targetFilter)
        {
            case EmbeddedActionTargetFilter.Enemy:
                if (targetUnit.faction == ownerFaction)
                {
                    return false;
                }
                break;

            case EmbeddedActionTargetFilter.Self:
                if (targetEntity != owner)
                {
                    return false;
                }
                break;

            case EmbeddedActionTargetFilter.AllyDamaged:
            case EmbeddedActionTargetFilter.AllyAny:
                if (targetUnit.faction != ownerFaction)
                {
                    return false;
                }
                break;

            default:
                return false;
        }

        if (targetFilter == EmbeddedActionTargetFilter.Enemy && ownerUsesFogOfWar &&
            !VisibilityUtility.IsVisibleToFaction(targetEntity, ownerFaction, ref visibilityLookupRef))
        {
            return false;
        }

        if (!localTransformLookupRef.TryGetComponent(targetEntity, out LocalTransform targetTransform))
        {
            return false;
        }

        targetPosition = targetTransform.Position.xy;
        float targetRadius = 0f;
        if (collisionRadiusLookupRef.TryGetComponent(targetEntity, out UnitCollisionRadius radius))
        {
            targetRadius = math.max(radius.collisionRadius.x, radius.collisionRadius.y);
        }

        float reach = math.max(0.01f, action.range) + targetRadius;
        float distSq = math.distancesq(targetPosition, pivotWorld);
        if (distSq > reach * reach)
        {
            return false;
        }

        edgeDistance = math.sqrt(math.max(0f, distSq)) - targetRadius;
        if (edgeDistance > action.range)
        {
            return false;
        }

        bool hasHealth = healthLookupRef.TryGetComponent(targetEntity, out Health hp);
        if (hasHealth && hp.healthAmount <= 0f)
        {
            return false;
        }

        bool needsHealth = effectKind == EmbeddedActionEffectKind.Damage
                        || effectKind == EmbeddedActionEffectKind.Repair
                        || effectKind == EmbeddedActionEffectKind.ShieldRestore
                        || targetFilter == EmbeddedActionTargetFilter.AllyDamaged;

        if (!needsHealth)
        {
            return true;
        }

        if (!hasHealth)
        {
            return false;
        }

        if (effectKind == EmbeddedActionEffectKind.Damage)
        {
            return hp.healthAmount > 0f;
        }

        if (effectKind == EmbeddedActionEffectKind.Repair)
        {
            return hp.healthAmount < hp.healthAmountMax;
        }

        if (effectKind == EmbeddedActionEffectKind.ShieldRestore)
        {
            float shieldCap = hp.healthAmountMax + math.max(0f, action.maxStoredValue);
            return hp.healthAmount < shieldCap;
        }

        return targetFilter != EmbeddedActionTargetFilter.AllyDamaged || hp.healthAmount < hp.healthAmountMax;
    }

    public static bool TryFindActionTarget(
        Entity owner,
        float2 pivotWorld,
        Faction ownerFaction,
        bool ownerUsesFogOfWar,
        EmbeddedActionTargetFilter targetFilter,
        EmbeddedActionEffectKind effectKind,
        in EmbeddedActionSlot action,
        in GridData gridData,
        ref ComponentLookup<Unit> unitLookupRef,
        ref ComponentLookup<Health> healthLookupRef,
        ref ComponentLookup<LocalTransform> localTransformLookupRef,
        ref ComponentLookup<UnitCollisionRadius> collisionRadiusLookupRef,
        ref ComponentLookup<Visibility> visibilityLookupRef,
        out Entity bestTarget,
        out float2 bestTargetPosition)
    {
        bestTarget = Entity.Null;
        bestTargetPosition = float2.zero;
        float bestScore = float.MaxValue;
        float range = math.max(0.01f, action.range);

        NativeParallelMultiHashMap<int2, Grid> map = targetFilter == EmbeddedActionTargetFilter.Enemy
            ? CombatUtility.GetEntityMap(in gridData, VisibilityUtility.Opposite(ownerFaction))
            : CombatUtility.GetEntityMap(in gridData, ownerFaction);

        int2 minCell = GridUtility.WorldToSmallCell(pivotWorld - range);
        int2 maxCell = GridUtility.WorldToSmallCell(pivotWorld + range);

        for (int y = minCell.y; y <= maxCell.y; y++)
        {
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                int2 cell = new int2(x, y);
                if (!map.TryGetFirstValue(cell, out Grid candidate, out var iterator))
                {
                    continue;
                }

                do
                {
                    Entity candidateEntity = candidate.Entity;
                    if (!IsValidActionTarget(
                            owner,
                            candidateEntity,
                            pivotWorld,
                            ownerFaction,
                            ownerUsesFogOfWar,
                            targetFilter,
                            effectKind,
                            in action,
                            ref unitLookupRef,
                            ref healthLookupRef,
                            ref localTransformLookupRef,
                            ref collisionRadiusLookupRef,
                            ref visibilityLookupRef,
                            out float2 candidatePosition,
                            out float edgeDistance))
                    {
                        continue;
                    }

                    float targetScore = edgeDistance;
                    if (targetFilter == EmbeddedActionTargetFilter.AllyDamaged &&
                        healthLookupRef.TryGetComponent(candidateEntity, out Health hp) &&
                        hp.healthAmountMax > 0.001f)
                    {
                        float missingRatio = math.saturate((hp.healthAmountMax - hp.healthAmount) / hp.healthAmountMax);
                        targetScore -= missingRatio * math.max(1f, action.range);
                    }

                    if (targetScore < bestScore)
                    {
                        bestScore = targetScore;
                        bestTarget = candidateEntity;
                        bestTargetPosition = candidatePosition;
                    }
                }
                while (map.TryGetNextValue(out candidate, ref iterator));
            }
        }

        return bestTarget != Entity.Null;
    }

    public static void AimActionSlot(
        ref EmbeddedWeaponSlot slot,
        in quaternion shipRotInv,
        float3 pivotWorld,
        float2 targetPosition,
        float rotateSpeed,
        float dt)
    {
        float3 toTargetWorld = new float3(targetPosition.x - pivotWorld.x, targetPosition.y - pivotWorld.y, 0f);
        if (math.lengthsq(toTargetWorld) < 0.0001f)
        {
            return;
        }

        float3 aimLocalDirection = math.rotate(shipRotInv, math.normalizesafe(toTargetWorld, new float3(0f, 1f, 0f)));
        aimLocalDirection.z = 0f;
        if (math.lengthsq(aimLocalDirection) < 0.0001f)
        {
            return;
        }

        aimLocalDirection = math.normalizesafe(aimLocalDirection, new float3(0f, 1f, 0f));
        float desiredLocalAngle = math.atan2(aimLocalDirection.y, aimLocalDirection.x) - math.PI * 0.5f;
        float delta = CombatUtility.NormalizeAngleRad(desiredLocalAngle - slot.currentLocalAngle);

        if (rotateSpeed <= 0f || dt <= 0f)
        {
            slot.currentLocalAngle = desiredLocalAngle;
            return;
        }

        float maxStep = rotateSpeed * dt;
        slot.currentLocalAngle = CombatUtility.NormalizeAngleRad(slot.currentLocalAngle + math.clamp(delta, -maxStep, maxStep));
    }

    public static void AccumulateInstantEffect(
        Entity owner,
        Entity target,
        in EmbeddedActionSlot action,
        float durationScale,
        ref NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator> pendingEffects,
        ref NativeParallelHashMap<Entity, EmpStatus> pendingEmp,
        ref NativeParallelHashMap<Entity, EmbeddedActionBuffStatus> pendingBuff,
        ref NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus> pendingDebuff,
        ref ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup,
        ref ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup)
    {
        switch ((EmbeddedActionEffectKind)action.effectKind)
        {
            case EmbeddedActionEffectKind.Damage:
                {
                    float damage = action.valuePerSecond * durationScale;
                    damage *= EmbeddedActionStatusUtility.GetOutgoingEffectMultiplier(owner, ref buffStatusLookup, ref debuffStatusLookup);
                    damage *= EmbeddedActionStatusUtility.GetIncomingDamageMultiplier(target, ref debuffStatusLookup);
                    if (damage > 0f)
                    {
                        AddEffect(ref pendingEffects, target, new EmbeddedActionEffectAccumulator { damage = damage });
                    }
                    break;
                }

            case EmbeddedActionEffectKind.Repair:
                {
                    float repair = action.valuePerSecond * durationScale;
                    if (repair > 0f)
                    {
                        AddEffect(ref pendingEffects, target, new EmbeddedActionEffectAccumulator { repair = repair });
                    }
                    break;
                }

            case EmbeddedActionEffectKind.ShieldRestore:
                {
                    float shieldRestore = action.valuePerSecond * durationScale;
                    if (shieldRestore > 0f)
                    {
                        AddEffect(ref pendingEffects, target, new EmbeddedActionEffectAccumulator
                        {
                            shieldRestore = shieldRestore,
                            shieldCap = math.max(0f, action.maxStoredValue),
                        });
                    }
                    break;
                }

            case EmbeddedActionEffectKind.Emp:
                EmbeddedActionStatusMergeUtility.MergePendingEmp(
                    ref pendingEmp,
                    target,
                    EmbeddedActionStatusMergeUtility.BuildEmpStatus(in action));
                break;

            case EmbeddedActionEffectKind.Buff:
                EmbeddedActionStatusMergeUtility.MergePendingBuff(
                    ref pendingBuff,
                    target,
                    EmbeddedActionStatusMergeUtility.BuildBuffStatus(in action));
                break;

            case EmbeddedActionEffectKind.Debuff:
                {
                    EmbeddedActionDebuffStatus debuff = EmbeddedActionStatusMergeUtility.BuildDebuffStatus(in action);
                    EmbeddedActionStatusMergeUtility.MergePendingDebuff(ref pendingDebuff, target, debuff);

                    if (debuff.disableWeapons || debuff.moveSpeedMultiplier < 0.999f || debuff.accelerationMultiplier < 0.999f)
                    {
                        EmbeddedActionStatusMergeUtility.MergePendingEmp(
                            ref pendingEmp,
                            target,
                            EmbeddedActionStatusMergeUtility.BuildEmpStatus(in action));
                    }
                    break;
                }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddEffect(
        ref NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator> pendingEffects,
        Entity target,
        EmbeddedActionEffectAccumulator incoming)
    {
        if (pendingEffects.TryGetValue(target, out EmbeddedActionEffectAccumulator existing))
        {
            existing.damage += incoming.damage;
            existing.repair += incoming.repair;
            existing.shieldRestore += incoming.shieldRestore;
            existing.shieldCap = math.max(existing.shieldCap, incoming.shieldCap);
            pendingEffects[target] = existing;
            return;
        }

        pendingEffects.TryAdd(target, incoming);
    }

    public static void AppendEffectsToBuffer(
        ref NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator> pendingEffects,
        DynamicBuffer<EmbeddedActionPendingEffect> output)
    {
        NativeArray<Entity> keys = pendingEffects.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < keys.Length; i++)
        {
            Entity target = keys[i];
            if (pendingEffects.TryGetValue(target, out EmbeddedActionEffectAccumulator effect))
            {
                output.Add(new EmbeddedActionPendingEffect
                {
                    target = target,
                    effect = effect,
                });
            }
        }

        keys.Dispose();
        pendingEffects.Clear();
    }

    public static void AppendStatusesToBuffers(
        ref NativeParallelHashMap<Entity, EmpStatus> pendingEmp,
        ref NativeParallelHashMap<Entity, EmbeddedActionBuffStatus> pendingBuff,
        ref NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus> pendingDebuff,
        DynamicBuffer<EmbeddedActionPendingEmpStatus> empOutput,
        DynamicBuffer<EmbeddedActionPendingBuffStatus> buffOutput,
        DynamicBuffer<EmbeddedActionPendingDebuffStatus> debuffOutput)
    {
        NativeArray<Entity> empKeys = pendingEmp.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < empKeys.Length; i++)
        {
            Entity target = empKeys[i];
            if (pendingEmp.TryGetValue(target, out EmpStatus status))
            {
                empOutput.Add(new EmbeddedActionPendingEmpStatus { target = target, status = status });
            }
        }
        empKeys.Dispose();
        pendingEmp.Clear();

        NativeArray<Entity> buffKeys = pendingBuff.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < buffKeys.Length; i++)
        {
            Entity target = buffKeys[i];
            if (pendingBuff.TryGetValue(target, out EmbeddedActionBuffStatus status))
            {
                buffOutput.Add(new EmbeddedActionPendingBuffStatus { target = target, status = status });
            }
        }
        buffKeys.Dispose();
        pendingBuff.Clear();

        NativeArray<Entity> debuffKeys = pendingDebuff.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < debuffKeys.Length; i++)
        {
            Entity target = debuffKeys[i];
            if (pendingDebuff.TryGetValue(target, out EmbeddedActionDebuffStatus status))
            {
                debuffOutput.Add(new EmbeddedActionPendingDebuffStatus { target = target, status = status });
            }
        }
        debuffKeys.Dispose();
        pendingDebuff.Clear();
    }

    public static void FlushEffects(
        ref NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator> pendingEffects,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup)
    {
        NativeArray<Entity> keys = pendingEffects.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < keys.Length; i++)
        {
            Entity target = keys[i];
            if (!pendingEffects.TryGetValue(target, out EmbeddedActionEffectAccumulator effect) ||
                !healthLookup.TryGetComponent(target, out Health hp))
            {
                continue;
            }

            if (hp.healthAmount <= 0f)
            {
                continue;
            }

            bool changed = false;

            // heal first, damage last. old order could revive target after lethal damage
            if (effect.repair > 0f && hp.healthAmount < hp.healthAmountMax)
            {
                hp.healthAmount = math.min(hp.healthAmountMax, hp.healthAmount + effect.repair);
                changed = true;
            }

            if (effect.shieldRestore > 0f)
            {
                float cap = hp.healthAmountMax + math.max(0f, effect.shieldCap);
                if (hp.healthAmount < cap)
                {
                    hp.healthAmount = math.min(cap, hp.healthAmount + effect.shieldRestore);
                    changed = true;
                }
            }

            if (effect.damage > 0f)
            {
                hp.healthAmount -= effect.damage;
                CombatUtility.MarkWasHit(ref shipAgroLookup, target);
                changed = true;

                if (hp.healthAmount <= 0f)
                {
                    hp.healthAmount = 0f;
                    hp.onHealthChanged = true;
                    healthLookup[target] = hp;
                    continue;
                }
            }

            if (!changed)
            {
                continue;
            }

            hp.onHealthChanged = true;
            healthLookup[target] = hp;
        }
        keys.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDeadStatusTarget(Entity entity, ref ComponentLookup<Health> healthLookup)
    {
        return healthLookup.TryGetComponent(entity, out Health hp) && hp.healthAmount <= 0f;
    }

    public static bool FlushStatuses(
        ref NativeParallelHashMap<Entity, EmpStatus> pendingEmp,
        ref NativeParallelHashMap<Entity, EmbeddedActionBuffStatus> pendingBuff,
        ref NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus> pendingDebuff,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup,
        ref ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup,
        ref ComponentLookup<Unit> unitLookup,
        ref ComponentLookup<Health> healthLookup,
        ref EntityCommandBuffer fallbackEcb)
    {
        bool fallbackChanged = false;

        NativeArray<Entity> empKeys = pendingEmp.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < empKeys.Length; i++)
        {
            Entity entity = empKeys[i];
            if (!pendingEmp.TryGetValue(entity, out EmpStatus status))
            {
                continue;
            }

            if (IsDeadStatusTarget(entity, ref healthLookup))
            {
                continue;
            }

            if (!empStatusLookup.HasComponent(entity))
            {
                if (unitLookup.HasComponent(entity))
                {
                    fallbackEcb.AddComponent(entity, status);
                    fallbackChanged = true;
                }

                continue;
            }

            EmpStatus existing = empStatusLookup[entity];
            empStatusLookup[entity] = EmbeddedActionStatusMergeUtility.MergeEmp(existing, status);
            empStatusLookup.SetComponentEnabled(entity, true);
        }
        empKeys.Dispose();

        NativeArray<Entity> buffKeys = pendingBuff.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < buffKeys.Length; i++)
        {
            Entity entity = buffKeys[i];
            if (!pendingBuff.TryGetValue(entity, out EmbeddedActionBuffStatus status))
            {
                continue;
            }

            if (IsDeadStatusTarget(entity, ref healthLookup))
            {
                continue;
            }

            if (!buffStatusLookup.HasComponent(entity))
            {
                if (unitLookup.HasComponent(entity))
                {
                    fallbackEcb.AddComponent(entity, status);
                    fallbackChanged = true;
                }

                continue;
            }

            EmbeddedActionBuffStatus existing = buffStatusLookup[entity];
            buffStatusLookup[entity] = EmbeddedActionStatusMergeUtility.MergeBuff(existing, status);
            buffStatusLookup.SetComponentEnabled(entity, true);
        }
        buffKeys.Dispose();

        NativeArray<Entity> debuffKeys = pendingDebuff.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < debuffKeys.Length; i++)
        {
            Entity entity = debuffKeys[i];
            if (!pendingDebuff.TryGetValue(entity, out EmbeddedActionDebuffStatus status))
            {
                continue;
            }

            if (IsDeadStatusTarget(entity, ref healthLookup))
            {
                continue;
            }

            if (!debuffStatusLookup.HasComponent(entity))
            {
                if (unitLookup.HasComponent(entity))
                {
                    fallbackEcb.AddComponent(entity, status);
                    fallbackChanged = true;
                }

                continue;
            }

            EmbeddedActionDebuffStatus existing = debuffStatusLookup[entity];
            debuffStatusLookup[entity] = EmbeddedActionStatusMergeUtility.MergeDebuff(existing, status);
            debuffStatusLookup.SetComponentEnabled(entity, true);
        }
        debuffKeys.Dispose();

        return fallbackChanged;
    }

    public static bool TryGetFirstHardpointOffset(
        DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints,
        in EmbeddedWeaponSlot slot,
        out float3 muzzleLocalOffset)
    {
        muzzleLocalOffset = float3.zero;
        if (slot.hardpointCount <= 0 || (uint)slot.firstHardpointIndex >= (uint)hardpoints.Length)
        {
            return false;
        }

        muzzleLocalOffset = hardpoints[slot.firstHardpointIndex].muzzleLocalOffset;
        return true;
    }

    public static bool RequestBeamVisual(
        ref DynamicBuffer<EmbeddedActionVisualRuntime> visuals,
        int slotIndex,
        in EmbeddedWeaponSlot slot,
        in EmbeddedActionSlot action,
        in quaternion shipRot,
        float3 shipPos,
        float2 targetPosition,
        DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints,
        ref ComponentLookup<UnitCollisionRadius> collisionRadiusLookup,
        float now)
    {
        if ((uint)slotIndex >= (uint)visuals.Length || action.visualPrefabEntity == Entity.Null)
        {
            return false;
        }

        EmbeddedActionVisualRuntime visual = visuals[slotIndex];
        if (visual.visualEntity == Entity.Null)
        {
            return false;
        }

        if (!TryGetFirstHardpointOffset(hardpoints, in slot, out float3 muzzleLocalOffset))
        {
            muzzleLocalOffset = slot.muzzleLocalOffset;
        }

        quaternion slotWorldRot = math.mul(shipRot, quaternion.RotateZ(slot.currentLocalAngle));
        float3 pivotWorld = shipPos + math.rotate(shipRot, slot.pivotLocalPosition);
        float3 startPos = pivotWorld + math.rotate(slotWorldRot, muzzleLocalOffset);
        float2 start2 = startPos.xy;
        float2 dir = math.normalizesafe(targetPosition - start2, new float2(0f, 1f));
        float2 end2 = targetPosition;

        if (collisionRadiusLookup.TryGetComponent(slot.targetEntity, out UnitCollisionRadius radius))
        {
            float targetRadius = math.max(radius.collisionRadius.x, radius.collisionRadius.y);
            float distanceToCenter = math.distance(start2, targetPosition);
            if (distanceToCenter > targetRadius)
            {
                end2 = targetPosition - dir * targetRadius;
            }
        }

        if (math.distancesq(start2, end2) <= 0.000001f)
        {
            return false;
        }

        visual.startWorld = start2;
        visual.endWorld = end2;
        visual.width = math.max(0.01f, action.visualWidth);
        visual.range = 0f;
        visual.kind = (byte)EmbeddedActionVisualKind.Beam;
        visual.visibleUntil = now + math.max(0.02f, action.visualInterval * 1.25f);
        visual.flags = EmbeddedActionVisualRuntimeFlags.Visible | EmbeddedActionVisualRuntimeFlags.Dirty;
        visuals[slotIndex] = visual;
        return true;
    }

    public static bool RequestAuraVisual(
        ref DynamicBuffer<EmbeddedActionVisualRuntime> visuals,
        int slotIndex,
        in EmbeddedActionSlot action,
        float3 pivotWorld,
        float now)
    {
        if ((uint)slotIndex >= (uint)visuals.Length || action.visualPrefabEntity == Entity.Null)
        {
            return false;
        }

        EmbeddedActionVisualRuntime visual = visuals[slotIndex];
        if (visual.visualEntity == Entity.Null)
        {
            return false;
        }

        visual.startWorld = pivotWorld.xy;
        visual.endWorld = pivotWorld.xy;
        visual.width = math.max(0.01f, action.visualWidth);
        visual.range = math.max(0.01f, action.range);
        visual.kind = (byte)EmbeddedActionVisualKind.Aura;
        visual.visibleUntil = now + math.max(0.04f, action.visualInterval * 1.25f);
        visual.flags = EmbeddedActionVisualRuntimeFlags.Visible | EmbeddedActionVisualRuntimeFlags.Dirty;
        visuals[slotIndex] = visual;
        return true;
    }
}
