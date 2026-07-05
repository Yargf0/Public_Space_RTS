using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// beam over time effects. timers are absolute (nextTickTime / nextVisualTime)
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedActionTargetSystem))]
[UpdateBefore(typeof(EmbeddedAuraActionSystem))]
public partial struct EmbeddedBeamActionSystem : ISystem
{
    private ComponentLookup<Unit> unitLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<ShipStateComponent> shipStateLookup;
    private ComponentLookup<UseFogOfWar> useFogOfWarLookup;
    private ComponentLookup<Visibility> visibilityLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<UnitCollisionRadius> collisionRadiusLookup;
    private EntityQuery targetCapacityQuery;

    private NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator> pendingEffects;
    private NativeParallelHashMap<Entity, EmpStatus> pendingEmp;
    private NativeParallelHashMap<Entity, EmbeddedActionBuffStatus> pendingBuff;
    private NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus> pendingDebuff;
    private float nextCapacityRefreshTime;
    private int cachedTargetCapacity;

    public void OnCreate(ref SystemState state)
    {
        unitLookup = state.GetComponentLookup<Unit>(true);
        healthLookup = state.GetComponentLookup<Health>(false);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(false);
        shipStateLookup = state.GetComponentLookup<ShipStateComponent>(true);
        useFogOfWarLookup = state.GetComponentLookup<UseFogOfWar>(true);
        visibilityLookup = state.GetComponentLookup<Visibility>(true);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(false);
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(false);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(false);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        collisionRadiusLookup = state.GetComponentLookup<UnitCollisionRadius>(true);

        targetCapacityQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<Unit>(),
            ComponentType.ReadOnly<LocalTransform>());

        state.RequireForUpdate<EmbeddedActionEffectFlushSingleton>();

        pendingEffects = new NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator>(128, Allocator.Persistent);
        pendingEmp = new NativeParallelHashMap<Entity, EmpStatus>(64, Allocator.Persistent);
        pendingBuff = new NativeParallelHashMap<Entity, EmbeddedActionBuffStatus>(64, Allocator.Persistent);
        pendingDebuff = new NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus>(64, Allocator.Persistent);
        cachedTargetCapacity = 128;
        nextCapacityRefreshTime = 0f;
    }

    public void OnDestroy(ref SystemState state)
    {
        if (pendingEffects.IsCreated) pendingEffects.Dispose();
        if (pendingEmp.IsCreated) pendingEmp.Dispose();
        if (pendingBuff.IsCreated) pendingBuff.Dispose();
        if (pendingDebuff.IsCreated) pendingDebuff.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float now = (float)SystemAPI.Time.ElapsedTime;
        float dt = SystemAPI.Time.DeltaTime;

        RefreshCapacityIfNeeded(now);

        unitLookup.Update(ref state);
        healthLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        shipStateLookup.Update(ref state);
        useFogOfWarLookup.Update(ref state);
        visibilityLookup.Update(ref state);
        empStatusLookup.Update(ref state);
        buffStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        collisionRadiusLookup.Update(ref state);

        foreach ((RefRO<LocalTransform> shipLocal,
                  DynamicBuffer<EmbeddedWeaponSlot> slots,
                  DynamicBuffer<EmbeddedWeaponHardpoint> hardpoints,
                  DynamicBuffer<EmbeddedActionSlot> actions,
                  DynamicBuffer<EmbeddedActionVisualRuntime> visuals,
                  RefRW<EmbeddedActionHostRuntime> hostRuntime,
                  Entity shipEntity) in
                 SystemAPI.Query<RefRO<LocalTransform>, DynamicBuffer<EmbeddedWeaponSlot>, DynamicBuffer<EmbeddedWeaponHardpoint>, DynamicBuffer<EmbeddedActionSlot>, DynamicBuffer<EmbeddedActionVisualRuntime>, RefRW<EmbeddedActionHostRuntime>>()
                     .WithAll<EmbeddedWeaponHost>()
                     .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedWeaponSlot> slotBuffer = slots;
            DynamicBuffer<EmbeddedActionSlot> actionBuffer = actions;
            DynamicBuffer<EmbeddedActionVisualRuntime> visualBuffer = visuals;

            if (hostRuntime.ValueRO.hasBeamActions == 0 ||
                !EmbeddedActionRuntimeUtility.IsActionHostWorkDue(hostRuntime.ValueRO.nextBeamWorkTime, now))
            {
                continue;
            }

            if (!unitLookup.TryGetComponent(shipEntity, out Unit ownerUnit))
            {
                hostRuntime.ValueRW.nextBeamWorkTime = now + 0.25f;
                continue;
            }

            if (healthLookup.TryGetComponent(shipEntity, out Health ownerHealth) && ownerHealth.healthAmount <= 0f)
            {
                hostRuntime.ValueRW.nextBeamWorkTime = now + 0.25f;
                continue;
            }

            bool ownerUsesFogOfWar = useFogOfWarLookup.HasComponent(shipEntity);
            bool ownerWeaponsDisabled = EmbeddedActionStatusUtility.AreWeaponsDisabled(shipEntity, ref empStatusLookup, ref debuffStatusLookup);
            bool fireAllowed = EmbeddedActionRulesUtility.IsFireAllowed(shipEntity, ref shipStateLookup, ref shipAgroLookup);

            LocalTransform shipTransform = shipLocal.ValueRO;
            quaternion shipRot = shipTransform.Rotation;
            quaternion shipRotInv = math.inverse(shipRot);
            float3 shipPos = shipTransform.Position;
            Faction ownerFaction = ownerUnit.faction;
            int count = math.min(slotBuffer.Length, actionBuffer.Length);

            bool visualWorkDue = false;

            for (int i = 0; i < count; i++)
            {
                EmbeddedWeaponSlot slot = slotBuffer[i];
                EmbeddedActionSlot action = actionBuffer[i];

                if ((EmbeddedWeaponSlotRole)slot.role == EmbeddedWeaponSlotRole.Damage ||
                    !EmbeddedActionRuntimeUtility.IsBeamAction(in action))
                {
                    continue;
                }

                bool rotateEnabled = (action.flags & EmbeddedActionSlotFlags.Rotate) != 0;
                float aimInterval = math.max(0.01f, action.aimInterval > 0f ? action.aimInterval : (1f / 30f));
                bool rotateDue = rotateEnabled && now >= action.aimTimer;
                bool tickDue = now >= action.timer;
                bool visualDue = now >= action.visualTimer;

                if (!rotateDue && !tickDue && !visualDue)
                {
                    continue;
                }

                EmbeddedActionTargetFilter targetFilter = (EmbeddedActionTargetFilter)action.targetFilter;
                EmbeddedActionEffectKind effectKind = (EmbeddedActionEffectKind)action.effectKind;
                bool actionChanged = false;

                if (EmbeddedActionRulesUtility.IsOffensiveAction(targetFilter, effectKind) && (!fireAllowed || ownerWeaponsDisabled))
                {
                    AdvanceDueBeamTimers(ref action, now, aimInterval, rotateDue, tickDue, visualDue);
                    actionBuffer[i] = action;
                    continue;
                }

                float3 pivotWorld = shipPos + math.rotate(shipRot, slot.pivotLocalPosition);
                bool valid = EmbeddedActionRuntimeUtility.IsValidActionTarget(
                    shipEntity,
                    slot.targetEntity,
                    pivotWorld.xy,
                    ownerFaction,
                    ownerUsesFogOfWar,
                    targetFilter,
                    effectKind,
                    in action,
                    ref unitLookup,
                    ref healthLookup,
                    ref localTransformLookup,
                    ref collisionRadiusLookup,
                    ref visibilityLookup,
                    out float2 targetPosition,
                    out _);

                bool slotChanged = false;

                if (!valid)
                {
                    // drop stale target now so TargetSystem searches again instead of rechecking dead one
                    slot.targetEntity = Entity.Null;
                    slot.targetPositionWorld = float2.zero;
                    slotBuffer[i] = slot;

                    action.searchTimer = now + EmbeddedActionRuntimeUtility.GetStableActionOffset(shipEntity, i, 0.05f);
                    AdvanceDueBeamTimers(ref action, now, aimInterval, rotateDue, tickDue, visualDue);
                    actionBuffer[i] = action;

                    continue;
                }

                if (rotateDue)
                {
                    action.aimInterval = aimInterval;
                    action.aimTimer = now + aimInterval;
                    actionChanged = true;

                    float oldAngle = slot.currentLocalAngle;
                    EmbeddedActionRuntimeUtility.AimActionSlot(ref slot, in shipRotInv, pivotWorld, targetPosition, action.rotateSpeed, dt);
                    slotChanged = math.abs(CombatUtility.NormalizeAngleRad(slot.currentLocalAngle - oldAngle)) > 0.00001f;
                }

                if (tickDue)
                {
                    action.timer = now + math.max(0.01f, action.tickInterval);
                    actionChanged = true;

                    EmbeddedActionRuntimeUtility.AccumulateInstantEffect(
                        shipEntity,
                        slot.targetEntity,
                        in action,
                        action.tickInterval,
                        ref pendingEffects,
                        ref pendingEmp,
                        ref pendingBuff,
                        ref pendingDebuff,
                        ref buffStatusLookup,
                        ref debuffStatusLookup);
                }

                if (visualDue)
                {
                    if (EmbeddedActionRuntimeUtility.RequestBeamVisual(
                            ref visualBuffer,
                            i,
                            in slot,
                            in action,
                            in shipRot,
                            shipPos,
                            targetPosition,
                            hardpoints,
                            ref collisionRadiusLookup,
                            now))
                    {
                        visualWorkDue = true;
                    }

                    action.visualTimer = now + math.max(0.01f, action.visualInterval);
                    actionChanged = true;
                }

                if (slotChanged)
                {
                    slotBuffer[i] = slot;
                }

                if (actionChanged)
                {
                    actionBuffer[i] = action;
                }
            }

            EmbeddedActionHostRuntime runtime = hostRuntime.ValueRO;
            EmbeddedActionRuntimeUtility.RefreshHostRuntimeFromActions(ref runtime, actionBuffer);
            if (visualWorkDue)
            {
                EmbeddedActionRuntimeUtility.MarkHostVisualWorkDue(ref runtime, now);
            }
            hostRuntime.ValueRW = runtime;
        }

        EmbeddedActionRuntimeUtility.AppendEffectsToBuffer(
            ref pendingEffects,
            SystemAPI.GetSingletonBuffer<EmbeddedActionPendingEffect>());

        EmbeddedActionRuntimeUtility.AppendStatusesToBuffers(
            ref pendingEmp,
            ref pendingBuff,
            ref pendingDebuff,
            SystemAPI.GetSingletonBuffer<EmbeddedActionPendingEmpStatus>(),
            SystemAPI.GetSingletonBuffer<EmbeddedActionPendingBuffStatus>(),
            SystemAPI.GetSingletonBuffer<EmbeddedActionPendingDebuffStatus>());
    }

    private static void AdvanceDueBeamTimers(ref EmbeddedActionSlot action, float now, float aimInterval, bool rotateDue, bool tickDue, bool visualDue)
    {
        if (rotateDue)
        {
            action.aimInterval = aimInterval;
            action.aimTimer = now + aimInterval;
        }

        if (tickDue)
        {
            action.timer = now + math.max(0.01f, action.tickInterval);
        }

        if (visualDue)
        {
            action.visualTimer = now + math.max(0.01f, action.visualInterval);
        }
    }

    private void RefreshCapacityIfNeeded(float now)
    {
        if (now >= nextCapacityRefreshTime)
        {
            cachedTargetCapacity = math.max(64, targetCapacityQuery.CalculateEntityCount() + 16);
            nextCapacityRefreshTime = now + 0.5f;
        }

        EnsureCapacity(cachedTargetCapacity);
    }

    private void EnsureCapacity(int targetCapacity)
    {
        if (pendingEffects.Capacity < targetCapacity)
        {
            pendingEffects.Dispose();
            pendingEffects = new NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator>(targetCapacity, Allocator.Persistent);
        }
        else
        {
            pendingEffects.Clear();
        }

        int statusCapacity = math.max(64, targetCapacity);

        if (pendingEmp.Capacity < statusCapacity)
        {
            pendingEmp.Dispose();
            pendingEmp = new NativeParallelHashMap<Entity, EmpStatus>(statusCapacity, Allocator.Persistent);
        }
        else
        {
            pendingEmp.Clear();
        }

        if (pendingBuff.Capacity < statusCapacity)
        {
            pendingBuff.Dispose();
            pendingBuff = new NativeParallelHashMap<Entity, EmbeddedActionBuffStatus>(statusCapacity, Allocator.Persistent);
        }
        else
        {
            pendingBuff.Clear();
        }

        if (pendingDebuff.Capacity < statusCapacity)
        {
            pendingDebuff.Dispose();
            pendingDebuff = new NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus>(statusCapacity, Allocator.Persistent);
        }
        else
        {
            pendingDebuff.Clear();
        }
    }
}
