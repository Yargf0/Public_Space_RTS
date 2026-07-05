using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// aura effects around owner/pivot
// V6: full scan every tick, budget fields are ignored
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedBeamActionSystem))]
[UpdateBefore(typeof(EmbeddedActionVisualSystem))]
public partial struct EmbeddedAuraActionSystem : ISystem
{
    private ComponentLookup<GridData> gridDataLookup;
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
    private NativeParallelHashSet<Entity> processedTargets;
    private float nextCapacityRefreshTime;
    private int cachedTargetCapacity;

    public void OnCreate(ref SystemState state)
    {
        gridDataLookup = state.GetComponentLookup<GridData>(true);
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
        processedTargets = new NativeParallelHashSet<Entity>(128, Allocator.Persistent);
        cachedTargetCapacity = 128;
        nextCapacityRefreshTime = 0f;

        state.RequireForUpdate<GridData>();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (pendingEffects.IsCreated) pendingEffects.Dispose();
        if (pendingEmp.IsCreated) pendingEmp.Dispose();
        if (pendingBuff.IsCreated) pendingBuff.Dispose();
        if (pendingDebuff.IsCreated) pendingDebuff.Dispose();
        if (processedTargets.IsCreated) processedTargets.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<GridData>(out Entity gridEntity))
        {
            return;
        }

        float now = (float)SystemAPI.Time.ElapsedTime;
        RefreshCapacityIfNeeded(now);

        gridDataLookup.Update(ref state);
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

        if (!gridDataLookup.TryGetComponent(gridEntity, out GridData gridData))
        {
            return;
        }

        foreach ((RefRO<LocalTransform> shipLocal,
                  DynamicBuffer<EmbeddedWeaponSlot> slots,
                  DynamicBuffer<EmbeddedActionSlot> actions,
                  DynamicBuffer<EmbeddedActionVisualRuntime> visuals,
                  RefRW<EmbeddedActionHostRuntime> hostRuntime,
                  Entity shipEntity) in
                 SystemAPI.Query<RefRO<LocalTransform>, DynamicBuffer<EmbeddedWeaponSlot>, DynamicBuffer<EmbeddedActionSlot>, DynamicBuffer<EmbeddedActionVisualRuntime>, RefRW<EmbeddedActionHostRuntime>>()
                     .WithAll<EmbeddedWeaponHost>()
                     .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedWeaponSlot> slotBuffer = slots;
            DynamicBuffer<EmbeddedActionSlot> actionBuffer = actions;
            DynamicBuffer<EmbeddedActionVisualRuntime> visualBuffer = visuals;

            if (hostRuntime.ValueRO.hasAuraActions == 0 ||
                !EmbeddedActionRuntimeUtility.IsActionHostWorkDue(hostRuntime.ValueRO.nextAuraWorkTime, now))
            {
                continue;
            }

            if (!unitLookup.TryGetComponent(shipEntity, out Unit ownerUnit))
            {
                hostRuntime.ValueRW.nextAuraWorkTime = now + 0.25f;
                continue;
            }

            if (healthLookup.TryGetComponent(shipEntity, out Health ownerHealth) && ownerHealth.healthAmount <= 0f)
            {
                hostRuntime.ValueRW.nextAuraWorkTime = now + 0.25f;
                continue;
            }

            bool ownerUsesFogOfWar = useFogOfWarLookup.HasComponent(shipEntity);
            bool ownerWeaponsDisabled = EmbeddedActionStatusUtility.AreWeaponsDisabled(shipEntity, ref empStatusLookup, ref debuffStatusLookup);
            bool fireAllowed = EmbeddedActionRulesUtility.IsFireAllowed(shipEntity, ref shipStateLookup, ref shipAgroLookup);

            LocalTransform shipTransform = shipLocal.ValueRO;
            quaternion shipRot = shipTransform.Rotation;
            float3 shipPos = shipTransform.Position;
            Faction ownerFaction = ownerUnit.faction;
            int count = math.min(slotBuffer.Length, actionBuffer.Length);

            bool visualWorkDue = false;

            for (int i = 0; i < count; i++)
            {
                EmbeddedWeaponSlot slot = slotBuffer[i];
                EmbeddedActionSlot action = actionBuffer[i];

                if ((EmbeddedWeaponSlotRole)slot.role == EmbeddedWeaponSlotRole.Damage ||
                    !EmbeddedActionRuntimeUtility.IsAuraAction(in action))
                {
                    continue;
                }

                bool visualDue = now >= action.visualTimer;
                bool tickDue = now >= action.timer;

                if (!visualDue && !tickDue)
                {
                    continue;
                }

                EmbeddedActionTargetFilter targetFilter = (EmbeddedActionTargetFilter)action.targetFilter;
                EmbeddedActionEffectKind effectKind = (EmbeddedActionEffectKind)action.effectKind;
                bool actionChanged = false;

                if (EmbeddedActionRulesUtility.IsOffensiveAction(targetFilter, effectKind) && (!fireAllowed || ownerWeaponsDisabled))
                {
                    if (visualDue)
                    {
                        action.visualTimer = now + math.max(0.01f, action.visualInterval);
                        actionChanged = true;
                    }

                    if (tickDue)
                    {
                        action.timer = now + math.max(0.01f, action.tickInterval);
                        actionChanged = true;
                    }

                    if (actionChanged)
                    {
                        actionBuffer[i] = action;
                    }

                    continue;
                }

                float3 pivotWorld = shipPos + math.rotate(shipRot, slot.pivotLocalPosition);

                if (visualDue)
                {
                    if (EmbeddedActionRuntimeUtility.RequestAuraVisual(ref visualBuffer, i, in action, pivotWorld, now))
                    {
                        visualWorkDue = true;
                    }

                    action.visualTimer = now + math.max(0.01f, action.visualInterval);
                    actionChanged = true;
                }

                if (tickDue)
                {
                    action.timer = now + math.max(0.01f, action.tickInterval);
                    actionChanged = true;

                    ProcessAuraTick(
                        shipEntity,
                        pivotWorld.xy,
                        ownerFaction,
                        ownerUsesFogOfWar,
                        targetFilter,
                        effectKind,
                        ref action,
                        in gridData);
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
    private void ProcessAuraTick(
        Entity owner,
        float2 center,
        Faction ownerFaction,
        bool ownerUsesFogOfWar,
        EmbeddedActionTargetFilter targetFilter,
        EmbeddedActionEffectKind effectKind,
        ref EmbeddedActionSlot action,
        in GridData gridData)
    {
        if (targetFilter == EmbeddedActionTargetFilter.Self)
        {
            if (EmbeddedActionRuntimeUtility.IsValidActionTarget(
                    owner,
                    owner,
                    center,
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
                    out _,
                    out _))
            {
                EmbeddedActionRuntimeUtility.AccumulateInstantEffect(
                    owner,
                    owner,
                    in action,
                    action.tickInterval,
                    ref pendingEffects,
                    ref pendingEmp,
                    ref pendingBuff,
                    ref pendingDebuff,
                    ref buffStatusLookup,
                    ref debuffStatusLookup);
            }

            return;
        }

        NativeParallelMultiHashMap<int2, Grid> map = targetFilter == EmbeddedActionTargetFilter.Enemy
            ? CombatUtility.GetEntityMap(in gridData, VisibilityUtility.Opposite(ownerFaction))
            : CombatUtility.GetEntityMap(in gridData, ownerFaction);

        float range = math.max(0.01f, action.range);
        int2 minCell = GridUtility.WorldToSmallCell(center - range);
        int2 maxCell = GridUtility.WorldToSmallCell(center + range);
        int cellCountX = math.max(1, maxCell.x - minCell.x + 1);
        int cellCountY = math.max(1, maxCell.y - minCell.y + 1);
        int totalCells = math.max(1, cellCountX * cellCountY);

        // full scan: whole radius, no target cap, matches the visual circle
        int maxCells = totalCells;
        int maxTargets = int.MaxValue;

        EnsureProcessedCapacity(math.max(128, cachedTargetCapacity));
        processedTargets.Clear();

        int processedCells = 0;
        int processedTargetsCount = 0;

        while (processedCells < maxCells && processedTargetsCount < maxTargets)
        {
            int linear = processedCells;
            int x = minCell.x + linear % cellCountX;
            int y = minCell.y + linear / cellCountX;
            processedCells++;

            int2 cell = new int2(x, y);
            if (!map.TryGetFirstValue(cell, out Grid candidate, out var iterator))
            {
                continue;
            }

            do
            {
                Entity candidateEntity = candidate.Entity;
                if (!processedTargets.Add(candidateEntity))
                {
                    continue;
                }

                if (!EmbeddedActionRuntimeUtility.IsValidActionTarget(
                        owner,
                        candidateEntity,
                        center,
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
                        out _,
                        out _))
                {
                    continue;
                }

                EmbeddedActionRuntimeUtility.AccumulateInstantEffect(
                    owner,
                    candidateEntity,
                    in action,
                    action.tickInterval,
                    ref pendingEffects,
                    ref pendingEmp,
                    ref pendingBuff,
                    ref pendingDebuff,
                    ref buffStatusLookup,
                    ref debuffStatusLookup);

                processedTargetsCount++;
                if (processedTargetsCount >= maxTargets)
                {
                    break;
                }
            }
            while (map.TryGetNextValue(out candidate, ref iterator));
        }

        action.scanCursor = 0;
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

    private void EnsureProcessedCapacity(int capacity)
    {
        if (processedTargets.Capacity >= capacity)
        {
            return;
        }

        processedTargets.Dispose();
        processedTargets = new NativeParallelHashSet<Entity>(capacity, Allocator.Persistent);
    }
}
