using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ShipToGridSystem))]
[UpdateAfter(typeof(SearchlightSpotSystem))]
partial struct ShipAgroSystem : ISystem
{
    private Entity gridEntity;
    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<ShipStateComponent> shipStateLookup;
    private ComponentLookup<ShipPriorityHint> hintLookup;
    private ComponentLookup<UseFogOfWar> useFogOfWarLookup;
    private ComponentLookup<Visibility> visibilityLookup;
    private ComponentLookup<LastKnownTarget> lastKnownTargetLookup;
    private BufferLookup<EmbeddedWeaponSlot> embeddedSlotLookup;
    private bool getData;
    private const byte AllShipTargetMask = (byte)(ShipSize.Small | ShipSize.Medium | ShipSize.Big | ShipSize.RocketSmall | ShipSize.RocketBig);

    // max agro scans per frame so big wave don't scan all at once
    private const int MaxAgroScansPerFrame = 256;
    private int remainingScanBudget;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        getData = false;
        gridDataLookup = state.GetComponentLookup<GridData>(isReadOnly: true);
        shipStateLookup = state.GetComponentLookup<ShipStateComponent>(isReadOnly: true);
        hintLookup = state.GetComponentLookup<ShipPriorityHint>(isReadOnly: true);
        useFogOfWarLookup = state.GetComponentLookup<UseFogOfWar>(isReadOnly: true);
        visibilityLookup = state.GetComponentLookup<Visibility>(isReadOnly: true);
        lastKnownTargetLookup = state.GetComponentLookup<LastKnownTarget>(isReadOnly: false);
        embeddedSlotLookup = state.GetBufferLookup<EmbeddedWeaponSlot>(isReadOnly: true);
    }

    // random interval per ship (0.75..1.25) so timers don't fire together
    private static float JitteredInterval(Entity entity, float interval)
    {
        uint hash = math.hash(new uint2((uint)entity.Index, (uint)entity.Version));
        float t = (hash & 1023u) / 1023f;
        return interval * math.lerp(0.75f, 1.25f, t);
    }

    // first timer value, random. must not be 0 or seed check runs again
    private static float InitialTimerSeed(Entity entity, float interval)
    {
        uint hash = math.hash(new uint2((uint)entity.Index * 0x9E3779B9u + 1u, (uint)entity.Version));
        float t = ((hash & 1023u) + 1u) / 1024f;
        return interval * t;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!getData)
        {
            gridEntity = SystemAPI.GetSingletonEntity<GridData>();
            getData = true;
        }

        gridDataLookup.Update(ref state);
        shipStateLookup.Update(ref state);
        hintLookup.Update(ref state);
        useFogOfWarLookup.Update(ref state);
        visibilityLookup.Update(ref state);
        lastKnownTargetLookup.Update(ref state);
        embeddedSlotLookup.Update(ref state);

        GridData gridData = gridDataLookup.GetRefRO(gridEntity).ValueRO;
        float dt = SystemAPI.Time.DeltaTime;
        bool hasWeaponDatabase = SystemAPI.TryGetSingleton(out WeaponProfileDatabase weaponDatabase) && weaponDatabase.Value.IsCreated;
        remainingScanBudget = MaxAgroScansPerFrame;

        foreach ((RefRO<LocalTransform> localTransform, RefRW<ShipAgro> shipAgro, RefRO<Unit> unit, Entity entity)
            in SystemAPI.Query<RefRO<LocalTransform>, RefRW<ShipAgro>, RefRO<Unit>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            FireMode fireMode = FireMode.FireAtWill;
            Entity forcedTarget = Entity.Null;
            ShipStateComponent shipState = default;

            if (shipStateLookup.TryGetComponent(entity, out ShipStateComponent foundShipState))
            {
                shipState = foundShipState;
                fireMode = shipState.mode;
                forcedTarget = shipState.forcedTarget;
            }

            bool useFogOfWar = useFogOfWarLookup.HasComponent(entity);
            Faction observerFaction = unit.ValueRO.faction;
            byte attackableTargetMask = ResolveAttackableTargetMask(
                entity,
                hasWeaponDatabase,
                weaponDatabase,
                ref embeddedSlotLookup);

            // player target is hard override. with fog of war it uses last known position
            if (forcedTarget != Entity.Null && shipState.currentState == ShipState.InCombat)
            {
                if (IsTargetStillValid(ref state, forcedTarget))
                {
                    LocalTransform forcedLT = SystemAPI.GetComponent<LocalTransform>(forcedTarget);

                    if (useFogOfWar && !VisibilityUtility.IsVisibleToFaction(forcedTarget, observerFaction, ref visibilityLookup))
                    {
                        float2 lkpPosition = WriteLastKnownTarget(entity, forcedTarget, forcedLT.Position.xy);
                        shipAgro.ValueRW.targetEntity = Entity.Null;
                        shipAgro.ValueRW.targetPosition = lkpPosition;
                        SystemAPI.SetComponentEnabled<ShipAgro>(entity, true);
                        continue;
                    }

                    ClearLastKnownTarget(entity);
                    shipAgro.ValueRW.targetEntity = forcedTarget;
                    shipAgro.ValueRW.targetPosition = forcedLT.Position.xy;
                    SystemAPI.SetComponentEnabled<ShipAgro>(entity, true);
                    continue;
                }

                shipAgro.ValueRW.targetEntity = Entity.Null;
            }

            if (fireMode == FireMode.HoldFire)
            {
                shipAgro.ValueRW.targetEntity = Entity.Null;
                ClearLastKnownTarget(entity);
                SystemAPI.SetComponentEnabled<ShipAgro>(entity, false);
                continue;
            }

            if (fireMode == FireMode.ReturnFire && !shipAgro.ValueRO.wasHit)
            {
                shipAgro.ValueRW.targetEntity = Entity.Null;
                ClearLastKnownTarget(entity);
                SystemAPI.SetComponentEnabled<ShipAgro>(entity, false);
                continue;
            }

            if (shipAgro.ValueRO.needDistance)
            {
                UpdateWithRadius(
                    ref state,
                    localTransform,
                    shipAgro,
                    unit,
                    entity,
                    in gridData,
                    dt,
                    useFogOfWar,
                    observerFaction,
                    attackableTargetMask);
            }
            else
            {
                UpdateWithoutRadius(
                    ref state,
                    shipAgro,
                    localTransform,
                    unit,
                    entity,
                    in gridData,
                    dt,
                    useFogOfWar,
                    observerFaction,
                    attackableTargetMask);
            }
        }
    }

    [BurstCompile]
    private void UpdateWithRadius(
        ref SystemState state,
        RefRO<LocalTransform> localTransform,
        RefRW<ShipAgro> shipAgro,
        RefRO<Unit> unit,
        Entity entity,
        in GridData gridData,
        float dt,
        bool useFogOfWar,
        Faction observerFaction,
        byte attackableTargetMask)
    {
        if (IsTargetStillValid(ref state, shipAgro.ValueRO.targetEntity))
        {
            LocalTransform targetLT = SystemAPI.GetComponent<LocalTransform>(shipAgro.ValueRO.targetEntity);

            if (!IsTargetAttackableByMask(ref state, shipAgro.ValueRO.targetEntity, attackableTargetMask))
            {
                shipAgro.ValueRW.targetEntity = Entity.Null;
            }
            else
            {
                if (useFogOfWar && !VisibilityUtility.IsVisibleToFaction(shipAgro.ValueRO.targetEntity, observerFaction, ref visibilityLookup))
                {
                    float2 lkpPosition = WriteLastKnownTarget(entity, shipAgro.ValueRO.targetEntity, targetLT.Position.xy);
                    shipAgro.ValueRW.targetPosition = lkpPosition;
                    shipAgro.ValueRW.targetEntity = Entity.Null;
                    SystemAPI.SetComponentEnabled<ShipAgro>(entity, true);
                    return;
                }

                if (math.distance(targetLT.Position, localTransform.ValueRO.Position) > shipAgro.ValueRO.detectionRadius)
                {
                    shipAgro.ValueRW.targetEntity = Entity.Null;
                }
                else
                {
                    shipAgro.ValueRW.targetPosition = targetLT.Position.xy;
                    return;
                }
            }
        }
        else if (shipAgro.ValueRO.targetEntity != Entity.Null)
        {
            shipAgro.ValueRW.targetEntity = Entity.Null;
        }

        // new ship, timer 0. random start so wave don't scan together
        if (shipAgro.ValueRO.timer == 0f && shipAgro.ValueRO.detectionTime > 0f)
        {
            shipAgro.ValueRW.timer = InitialTimerSeed(entity, shipAgro.ValueRO.detectionTime);
            return;
        }

        shipAgro.ValueRW.timer -= dt;
        if (shipAgro.ValueRO.timer > 0f)
        {
            return;
        }

        // no budget, retry next frame
        if (remainingScanBudget <= 0)
        {
            return;
        }

        remainingScanBudget--;
        shipAgro.ValueRW.timer = JitteredInterval(entity, shipAgro.ValueRO.detectionTime);

        float2 localPos = localTransform.ValueRO.Position.xy;
        float radius = shipAgro.ValueRO.detectionRadius;

        bool hasHint = false;
        int hintTargetIndex = 0;
        int hintTargetVersion = 0;
        float hintWeight = 0f;

        if (hintLookup.TryGetComponent(entity, out ShipPriorityHint hint) && hint.target != Entity.Null)
        {
            hasHint = true;
            hintTargetIndex = hint.target.Index;
            hintTargetVersion = hint.target.Version;
            hintWeight = hint.weight;
        }

        Entity found = Entity.Null;
        float bestScore = float.MinValue;

        if (unit.ValueRO.faction != Faction.Enemy)
        {
            FindBestInRadius(
                in gridData.EnemyEntityBigMap,
                in localPos,
                radius,
                hasHint,
                hintTargetIndex,
                hintTargetVersion,
                hintWeight,
                useFogOfWar,
                observerFaction,
                attackableTargetMask,
                ref visibilityLookup,
                ref found,
                ref bestScore);
        }

        if (unit.ValueRO.faction != Faction.Friendly)
        {
            FindBestInRadius(
                in gridData.FriendlyEntityBigMap,
                in localPos,
                radius,
                hasHint,
                hintTargetIndex,
                hintTargetVersion,
                hintWeight,
                useFogOfWar,
                observerFaction,
                attackableTargetMask,
                ref visibilityLookup,
                ref found,
                ref bestScore);
        }

        shipAgro.ValueRW.targetEntity = found;

        if (found != Entity.Null)
        {
            if (SystemAPI.HasComponent<LocalTransform>(found))
                shipAgro.ValueRW.targetPosition = SystemAPI.GetComponent<LocalTransform>(found).Position.xy;

            ClearLastKnownTarget(entity);
        }

        bool hasLkp = lastKnownTargetLookup.HasComponent(entity) && lastKnownTargetLookup.IsComponentEnabled(entity);
        SystemAPI.SetComponentEnabled<ShipAgro>(entity, found != Entity.Null || hasLkp);
    }

    [BurstCompile]
    private void UpdateWithoutRadius(
        ref SystemState state,
        RefRW<ShipAgro> shipAgro,
        RefRO<LocalTransform> localTransform,
        RefRO<Unit> unit,
        Entity entity,
        in GridData gridData,
        float dt,
        bool useFogOfWar,
        Faction observerFaction,
        byte attackableTargetMask)
    {
        if (IsTargetStillValid(ref state, shipAgro.ValueRO.targetEntity))
        {
            LocalTransform targetLT = SystemAPI.GetComponent<LocalTransform>(shipAgro.ValueRO.targetEntity);

            if (!IsTargetAttackableByMask(ref state, shipAgro.ValueRO.targetEntity, attackableTargetMask))
            {
                shipAgro.ValueRW.targetEntity = Entity.Null;
            }
            else
            {
                if (useFogOfWar && !VisibilityUtility.IsVisibleToFaction(shipAgro.ValueRO.targetEntity, observerFaction, ref visibilityLookup))
                {
                    float2 lkpPosition = WriteLastKnownTarget(entity, shipAgro.ValueRO.targetEntity, targetLT.Position.xy);
                    shipAgro.ValueRW.targetPosition = lkpPosition;
                    shipAgro.ValueRW.targetEntity = Entity.Null;
                    SystemAPI.SetComponentEnabled<ShipAgro>(entity, true);
                    return;
                }

                shipAgro.ValueRW.targetPosition = targetLT.Position.xy;
                return;
            }
        }

        // new ship, timer 0. random start so wave don't scan together
        if (shipAgro.ValueRO.timer == 0f && shipAgro.ValueRO.detectionTime > 0f)
        {
            shipAgro.ValueRW.timer = InitialTimerSeed(entity, shipAgro.ValueRO.detectionTime);
            return;
        }

        shipAgro.ValueRW.timer -= dt;
        if (shipAgro.ValueRO.timer > 0f)
        {
            return;
        }

        // no budget, retry next frame
        if (remainingScanBudget <= 0)
        {
            return;
        }

        remainingScanBudget--;
        shipAgro.ValueRW.timer = JitteredInterval(entity, shipAgro.ValueRO.detectionTime);

        float2 localPos = localTransform.ValueRO.Position.xy;

        bool hasHint = false;
        int hintTargetIndex = 0;
        int hintTargetVersion = 0;
        float hintWeight = 0f;

        if (hintLookup.TryGetComponent(entity, out ShipPriorityHint hint) && hint.target != Entity.Null)
        {
            hasHint = true;
            hintTargetIndex = hint.target.Index;
            hintTargetVersion = hint.target.Version;
            hintWeight = hint.weight;
        }

        Entity best = Entity.Null;
        float bestScore = float.MinValue;

        if (unit.ValueRO.faction != Faction.Enemy)
        {
            FindBestInMap(
                in gridData.EnemyEntityBigMap,
                in localPos,
                hasHint,
                hintTargetIndex,
                hintTargetVersion,
                hintWeight,
                useFogOfWar,
                observerFaction,
                attackableTargetMask,
                ref visibilityLookup,
                ref best,
                ref bestScore);
        }

        if (unit.ValueRO.faction != Faction.Friendly)
        {
            FindBestInMap(
                in gridData.FriendlyEntityBigMap,
                in localPos,
                hasHint,
                hintTargetIndex,
                hintTargetVersion,
                hintWeight,
                useFogOfWar,
                observerFaction,
                attackableTargetMask,
                ref visibilityLookup,
                ref best,
                ref bestScore);
        }

        shipAgro.ValueRW.targetEntity = best;

        if (best != Entity.Null)
        {
            if (SystemAPI.HasComponent<LocalTransform>(best))
                shipAgro.ValueRW.targetPosition = SystemAPI.GetComponent<LocalTransform>(best).Position.xy;

            ClearLastKnownTarget(entity);
        }

        bool hasLkp = lastKnownTargetLookup.HasComponent(entity) && lastKnownTargetLookup.IsComponentEnabled(entity);
        SystemAPI.SetComponentEnabled<ShipAgro>(entity, best != Entity.Null || hasLkp);
    }

    [BurstCompile]
    private bool IsTargetStillValid(ref SystemState state, Entity target)
    {
        return target != Entity.Null
            && SystemAPI.Exists(target)
            && SystemAPI.HasComponent<LocalTransform>(target);
    }

    [BurstCompile]
    private bool IsTargetAttackableByMask(ref SystemState state, Entity target, byte attackableTargetMask)
    {
        return target != Entity.Null
            && SystemAPI.HasComponent<Unit>(target)
            && (attackableTargetMask & SystemAPI.GetComponent<Unit>(target).shipSize) != 0;
    }

    [BurstCompile]
    private float2 WriteLastKnownTarget(Entity owner, Entity target, float2 lastKnownPosition)
    {
        if (!lastKnownTargetLookup.HasComponent(owner))
        {
            return lastKnownPosition;
        }

        LastKnownTarget lastKnownTarget = lastKnownTargetLookup[owner];

        if (lastKnownTargetLookup.IsComponentEnabled(owner) && lastKnownTarget.target == target)
        {
            return lastKnownTarget.lastKnownPosition;
        }

        lastKnownTarget.target = target;
        lastKnownTarget.lastKnownPosition = lastKnownPosition;
        lastKnownTarget.searchTimer = GameConstants.LkpSearchDuration;
        lastKnownTargetLookup[owner] = lastKnownTarget;
        lastKnownTargetLookup.SetComponentEnabled(owner, true);

        return lastKnownPosition;
    }

    [BurstCompile]
    private void ClearLastKnownTarget(Entity owner)
    {
        if (lastKnownTargetLookup.HasComponent(owner))
        {
            lastKnownTargetLookup.SetComponentEnabled(owner, false);
        }
    }

    [BurstCompile]
    private static void FindBestInRadius(
        in NativeParallelMultiHashMap<int2, Grid> map,
        in float2 pos,
        float radius,
        bool hasHint,
        int hintTargetIndex,
        int hintTargetVersion,
        float hintWeight,
        bool useFogOfWar,
        Faction observerFaction,
        byte attackableTargetMask,
        ref ComponentLookup<Visibility> visibilityLookup,
        ref Entity result,
        ref float bestScore)
    {
        int2 min = GridUtility.WorldToBigCell(pos - radius);
        int2 max = GridUtility.WorldToBigCell(pos + radius);

        for (int y = min.y; y <= max.y; y++)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                if (map.TryGetFirstValue(new int2(x, y), out Grid grid, out NativeParallelMultiHashMapIterator<int2> it))
                {
                    do
                    {
                        if (useFogOfWar && !VisibilityUtility.IsVisibleToFaction(grid.Entity, observerFaction, ref visibilityLookup))
                        {
                            continue;
                        }

                        if ((attackableTargetMask & grid.ShipSize) == 0)
                        {
                            continue;
                        }

                        float dist = math.distance(grid.Position, pos);
                        if (dist > radius)
                        {
                            continue;
                        }

                        float score = -dist;

                        if (hasHint &&
                            grid.Entity.Index == hintTargetIndex &&
                            grid.Entity.Version == hintTargetVersion)
                        {
                            score += hintWeight;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            result = grid.Entity;
                        }
                    }
                    while (map.TryGetNextValue(out grid, ref it));
                }
            }
        }
    }

    [BurstCompile]
    private static void FindBestInMap(
        in NativeParallelMultiHashMap<int2, Grid> map,
        in float2 pos,
        bool hasHint,
        int hintTargetIndex,
        int hintTargetVersion,
        float hintWeight,
        bool useFogOfWar,
        Faction observerFaction,
        byte attackableTargetMask,
        ref ComponentLookup<Visibility> visibilityLookup,
        ref Entity best,
        ref float bestScore)
    {
        NativeArray<int2> keys = map.GetKeyArray(Allocator.Temp);

        for (int i = 0; i < keys.Length; i++)
        {
            if (map.TryGetFirstValue(keys[i], out Grid grid, out NativeParallelMultiHashMapIterator<int2> it))
            {
                do
                {
                    if (useFogOfWar && !VisibilityUtility.IsVisibleToFaction(grid.Entity, observerFaction, ref visibilityLookup))
                    {
                        continue;
                    }

                    if ((attackableTargetMask & grid.ShipSize) == 0)
                    {
                        continue;
                    }

                    float dist = math.distance(grid.Position, pos);
                    float score = -dist;

                    if (hasHint &&
                        grid.Entity.Index == hintTargetIndex &&
                        grid.Entity.Version == hintTargetVersion)
                    {
                        score += hintWeight;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = grid.Entity;
                    }
                }
                while (map.TryGetNextValue(out grid, ref it));
            }
        }

        keys.Dispose();
    }

    private static byte ResolveAttackableTargetMask(
        Entity entity,
        bool hasWeaponDatabase,
        WeaponProfileDatabase weaponDatabase,
        ref BufferLookup<EmbeddedWeaponSlot> embeddedSlotLookup)
    {
        if (!hasWeaponDatabase || !embeddedSlotLookup.HasBuffer(entity))
        {
            return AllShipTargetMask;
        }

        ref WeaponProfileDatabaseBlob root = ref weaponDatabase.Value.Value;
        DynamicBuffer<EmbeddedWeaponSlot> slots = embeddedSlotLookup[entity];
        byte mask = 0;

        for (int i = 0; i < slots.Length; i++)
        {
            EmbeddedWeaponSlot slot = slots[i];
            if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
            {
                continue;
            }

            if ((uint)slot.profileIndex >= (uint)root.Profiles.Length)
            {
                continue;
            }

            mask |= root.Profiles[slot.profileIndex].allowedTargets;
        }

        return mask != 0 ? mask : AllShipTargetMask;
    }

    public void OnDestroy(ref SystemState state)
    {
    }
}
