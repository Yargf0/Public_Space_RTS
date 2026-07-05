using Unity.Entities;
using Unity.Mathematics;

// InitSystem runs once after spawn, this one resets state when entity goes dead -> alive again
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedActionRuntimeInitSystem))]
[UpdateBefore(typeof(EmbeddedActionTargetSystem))]
[UpdateBefore(typeof(EmbeddedBeamActionSystem))]
[UpdateBefore(typeof(EmbeddedAuraActionSystem))]
[UpdateBefore(typeof(EmbeddedActionVisualSystem))]
public partial struct EmbeddedActionRuntimeResetSystem : ISystem
{
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private ComponentLookup<EmbeddedWeaponFireRuntime> fireRuntimeLookup;
    private EntityQuery missingDeadStateQuery;
    private EntityQuery changedHealthQuery;
    private EntityQuery pressureRebuildRequestQuery;

    public void OnCreate(ref SystemState state)
    {
        empStatusLookup = state.GetComponentLookup<EmpStatus>(false);
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(false);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(false);
        fireRuntimeLookup = state.GetComponentLookup<EmbeddedWeaponFireRuntime>(false);
        missingDeadStateQuery = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<EmbeddedWeaponHost>(),
                ComponentType.ReadOnly<EmbeddedActionRuntimeInitialized>(),
                ComponentType.ReadWrite<EmbeddedWeaponSlot>(),
                ComponentType.ReadWrite<EmbeddedActionSlot>(),
                ComponentType.ReadWrite<EmbeddedActionVisualRuntime>(),
                ComponentType.ReadWrite<EmbeddedActionHostRuntime>(),
            },
            None = new[]
            {
                ComponentType.ReadOnly<EmbeddedActionRuntimeDeadState>(),
            },
        });

        changedHealthQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadWrite<EmbeddedWeaponSlot>(),
            ComponentType.ReadWrite<EmbeddedActionSlot>(),
            ComponentType.ReadWrite<EmbeddedActionVisualRuntime>(),
            ComponentType.ReadWrite<EmbeddedActionHostRuntime>(),
            ComponentType.ReadWrite<EmbeddedActionRuntimeDeadState>(),
            ComponentType.ReadOnly<EmbeddedWeaponHost>(),
            ComponentType.ReadOnly<EmbeddedActionRuntimeInitialized>());
        changedHealthQuery.SetChangedVersionFilter(ComponentType.ReadOnly<Health>());
        pressureRebuildRequestQuery = state.GetEntityQuery(ComponentType.ReadOnly<EmbeddedWeaponPressureRebuildRequest>());
    }

    public void OnUpdate(ref SystemState state)
    {
        bool hasMissingDeadState = !missingDeadStateQuery.IsEmptyIgnoreFilter;
        bool hasChangedHealth = changedHealthQuery.CalculateEntityCount() > 0;
        if (!hasMissingDeadState && !hasChangedHealth)
        {
            return;
        }

        float now = (float)SystemAPI.Time.ElapsedTime;
        empStatusLookup.Update(ref state);
        buffStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);
        fireRuntimeLookup.Update(ref state);
        bool needsPressureRebuild = false;

        if (hasMissingDeadState)
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            bool addedMissingState = false;

            foreach ((RefRO<Health> healthRef,
                         DynamicBuffer<EmbeddedWeaponSlot> slots,
                         DynamicBuffer<EmbeddedActionSlot> actions,
                         DynamicBuffer<EmbeddedActionVisualRuntime> visuals,
                         RefRW<EmbeddedActionHostRuntime> hostRuntimeRef,
                         Entity shipEntity) in
                     SystemAPI.Query<RefRO<Health>,
                             DynamicBuffer<EmbeddedWeaponSlot>,
                             DynamicBuffer<EmbeddedActionSlot>,
                             DynamicBuffer<EmbeddedActionVisualRuntime>,
                             RefRW<EmbeddedActionHostRuntime>>()
                         .WithAll<EmbeddedWeaponHost>()
                         .WithAll<EmbeddedActionRuntimeInitialized>()
                         .WithNone<EmbeddedActionRuntimeDeadState>()
                         .WithEntityAccess())
            {
                bool isDead = healthRef.ValueRO.healthAmount <= 0f;
                if (isDead)
                {
                    needsPressureRebuild |= ClearSlotTargets(slots);
                    ParkActionTimers(actions);
                    ResetVisualRuntime(visuals);
                    EmbeddedActionHostRuntime parkedRuntime = hostRuntimeRef.ValueRW;
                    DisableHostWork(ref parkedRuntime);
                    hostRuntimeRef.ValueRW = parkedRuntime;
                    RefreshFireRuntimeIfChanged(shipEntity, slots);
                    DisableActionStatuses(shipEntity);
                }

                ecb.AddComponent(shipEntity, new EmbeddedActionRuntimeDeadState
                {
                    wasDead = isDead ? (byte)1 : (byte)0,
                });
                addedMissingState = true;
            }

            if (addedMissingState)
            {
                ecb.Playback(state.EntityManager);
                empStatusLookup.Update(ref state);
                buffStatusLookup.Update(ref state);
                debuffStatusLookup.Update(ref state);
                fireRuntimeLookup.Update(ref state);
            }
        }

        if (!hasChangedHealth)
        {
            if (needsPressureRebuild)
            {
                RequestPressureRebuild(ref state);
            }

            return;
        }

        foreach ((RefRO<Health> healthRef,
                     DynamicBuffer<EmbeddedWeaponSlot> slots,
                     DynamicBuffer<EmbeddedActionSlot> actions,
                     DynamicBuffer<EmbeddedActionVisualRuntime> visuals,
                     RefRW<EmbeddedActionHostRuntime> hostRuntimeRef,
                     RefRW<EmbeddedActionRuntimeDeadState> deadStateRef,
                     Entity shipEntity) in
                 SystemAPI.Query<RefRO<Health>,
                         DynamicBuffer<EmbeddedWeaponSlot>,
                         DynamicBuffer<EmbeddedActionSlot>,
                         DynamicBuffer<EmbeddedActionVisualRuntime>,
                         RefRW<EmbeddedActionHostRuntime>,
                         RefRW<EmbeddedActionRuntimeDeadState>>()
                     .WithAll<EmbeddedWeaponHost>()
                     .WithAll<EmbeddedActionRuntimeInitialized>()
                     .WithChangeFilter<Health>()
                     .WithEntityAccess())
        {
            bool isDead = healthRef.ValueRO.healthAmount <= 0f;
            bool wasDead = deadStateRef.ValueRO.wasDead != 0;

            if (!healthRef.ValueRO.onHealthChanged && !(isDead && !wasDead))
            {
                continue;
            }

            if (isDead)
            {
                if (!wasDead)
                {
                    needsPressureRebuild |= ClearSlotTargets(slots);
                    ParkActionTimers(actions);
                    ResetVisualRuntime(visuals);
                    EmbeddedActionHostRuntime parkedRuntime = hostRuntimeRef.ValueRW;
                    DisableHostWork(ref parkedRuntime);
                    hostRuntimeRef.ValueRW = parkedRuntime;
                    RefreshFireRuntimeIfChanged(shipEntity, slots);
                    DisableActionStatuses(shipEntity);
                    EmbeddedActionRuntimeDeadState deadState = deadStateRef.ValueRW;
                    deadState.wasDead = 1;
                    deadStateRef.ValueRW = deadState;
                }

                continue;
            }

            if (!wasDead)
            {
                continue;
            }

            // entity was reused by pooling, start from clean state
            needsPressureRebuild |= ClearSlotTargets(slots);
            ReinitializeActionTimers(actions, shipEntity, now);
            ResetVisualRuntime(visuals);
            DisableActionStatuses(shipEntity);

            EmbeddedActionHostRuntime hostRuntime = EmbeddedActionRuntimeUtility.BuildHostRuntimeFromActions(actions);
            EmbeddedActionRuntimeUtility.RefreshHostVisualWorkTime(ref hostRuntime, visuals);
            hostRuntimeRef.ValueRW = hostRuntime;
            RefreshFireRuntimeIfChanged(shipEntity, slots);
            EmbeddedActionRuntimeDeadState aliveState = deadStateRef.ValueRW;
            aliveState.wasDead = 0;
            deadStateRef.ValueRW = aliveState;
        }

        if (needsPressureRebuild)
        {
            RequestPressureRebuild(ref state);
        }
    }

    private static bool ClearSlotTargets(DynamicBuffer<EmbeddedWeaponSlot> slots)
    {
        bool clearedDamageTarget = false;
        for (int i = 0; i < slots.Length; i++)
        {
            EmbeddedWeaponSlot slot = slots[i];
            if (slot.targetEntity == Entity.Null && math.all(slot.targetPositionWorld == float2.zero))
            {
                continue;
            }

            if ((EmbeddedWeaponSlotRole)slot.role == EmbeddedWeaponSlotRole.Damage && slot.targetEntity != Entity.Null)
            {
                clearedDamageTarget = true;
            }

            slot.targetEntity = Entity.Null;
            slot.targetPositionWorld = float2.zero;
            slots[i] = slot;
        }

        return clearedDamageTarget;
    }

    private static void ParkActionTimers(DynamicBuffer<EmbeddedActionSlot> actions)
    {
        for (int i = 0; i < actions.Length; i++)
        {
            EmbeddedActionSlot action = actions[i];
            action.timer = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
            action.searchTimer = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
            action.visualTimer = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
            action.aimTimer = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
            actions[i] = action;
        }
    }

    private static void ReinitializeActionTimers(DynamicBuffer<EmbeddedActionSlot> actions, Entity shipEntity, float now)
    {
        for (int i = 0; i < actions.Length; i++)
        {
            EmbeddedActionSlot action = actions[i];

            float tickInterval = math.max(0.01f, action.tickInterval);
            float searchInterval = math.max(0.01f, action.searchInterval);
            float visualInterval = math.max(0.01f, action.visualInterval);
            float aimInterval = math.max(0.01f, action.aimInterval > 0f ? action.aimInterval : (1f / 30f));

            action.timer = now + tickInterval + EmbeddedActionRuntimeUtility.GetStableActionOffset(shipEntity, i, math.min(tickInterval, 0.30f));
            action.searchTimer = now + EmbeddedActionRuntimeUtility.GetStableActionOffset(shipEntity, i + 4096, math.min(searchInterval, 0.50f));
            action.visualTimer = now + EmbeddedActionRuntimeUtility.GetStableActionOffset(shipEntity, i + 8192, math.min(visualInterval, 0.30f));
            action.aimInterval = aimInterval;
            action.aimTimer = now + EmbeddedActionRuntimeUtility.GetStableActionOffset(shipEntity, i + 12288, math.min(aimInterval, 0.20f));
            action.scanCursor = (int)(math.hash(new uint3((uint)shipEntity.Index, (uint)shipEntity.Version, (uint)(i + 1))) & 0x3FFFFFFFu);

            actions[i] = action;
        }
    }

    private static void ResetVisualRuntime(DynamicBuffer<EmbeddedActionVisualRuntime> visuals)
    {
        for (int i = 0; i < visuals.Length; i++)
        {
            EmbeddedActionVisualRuntime visual = visuals[i];
            visual.visibleUntil = 0f;
            visual.startWorld = float2.zero;
            visual.endWorld = float2.zero;
            visual.range = 0f;
            visual.width = 0f;
            visual.kind = (byte)EmbeddedActionVisualKind.None;
            visual.flags = 0;
            visuals[i] = visual;
        }
    }

    private static void DisableHostWork(ref EmbeddedActionHostRuntime runtime)
    {
        runtime.nextTargetWorkTime = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
        runtime.nextBeamWorkTime = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
        runtime.nextAuraWorkTime = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
        runtime.nextVisualWorkTime = EmbeddedActionRuntimeUtility.NoScheduledActionWork;
        runtime.hasBeamActions = 0;
        runtime.hasAuraActions = 0;
    }

    private void DisableActionStatuses(Entity entity)
    {
        if (empStatusLookup.HasComponent(entity))
        {
            empStatusLookup.SetComponentEnabled(entity, false);
        }

        if (buffStatusLookup.HasComponent(entity))
        {
            buffStatusLookup.SetComponentEnabled(entity, false);
        }

        if (debuffStatusLookup.HasComponent(entity))
        {
            debuffStatusLookup.SetComponentEnabled(entity, false);
        }
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
}
