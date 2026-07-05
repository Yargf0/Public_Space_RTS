using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// caches flow field groups by quantized target + size class
// safe for SubScene switching
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CommandDequeueSystem))]
[UpdateBefore(typeof(FlowFieldUpdateSystem))]
partial struct GroupManagerSystem : ISystem
{
    private ComponentLookup<FlowFieldData> flowFieldDataLookup;
    private ComponentLookup<PathfindingSizeClassComponent> pathSizeClassLookup;

    private Entity gridEntity;
    private Entity gridEntityDatabaseEntity;

    // small group direct-move optimization is off
    // 0 = all groups use flow field like before
    private const int SmallGroupThreshold = 0;

    // original tighter quantization
    private const int QuantizationSize = 2;

    private struct GroupCacheEntry
    {
        public Entity GroupEntity;

        // 1 = entity made by ECB this frame, must assign through same ECB
        // or temp id is not remapped in UnitGroup
        public byte RequiresEcbRemap;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowFieldData>();
        state.RequireForUpdate<GridEntityDatabase>();

        flowFieldDataLookup = state.GetComponentLookup<FlowFieldData>(isReadOnly: true);
        pathSizeClassLookup = state.GetComponentLookup<PathfindingSizeClassComponent>(isReadOnly: true);

        gridEntity = Entity.Null;
        gridEntityDatabaseEntity = Entity.Null;
    }

    public void OnUpdate(ref SystemState state)
    {
        flowFieldDataLookup.Update(ref state);
        pathSizeClassLookup.Update(ref state);

        Entity currentGridEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();
        Entity currentDatabaseEntity = SystemAPI.GetSingletonEntity<GridEntityDatabase>();

        bool flowFieldSceneChanged = gridEntity != Entity.Null && gridEntity != currentGridEntity;

        gridEntity = currentGridEntity;
        gridEntityDatabaseEntity = currentDatabaseEntity;

        if (!state.EntityManager.Exists(gridEntity) || !flowFieldDataLookup.HasComponent(gridEntity))
            return;

        if (!state.EntityManager.Exists(gridEntityDatabaseEntity) ||
            !state.EntityManager.HasBuffer<GridEntityElement>(gridEntityDatabaseEntity))
        {
            return;
        }

        if (flowFieldSceneChanged)
        {
            ClearRuntimeFlowFieldGroups(ref state);

            // ECB playback above did structural changes,
            // all cached lookups are invalid now, refresh them
            flowFieldDataLookup.Update(ref state);
            pathSizeClassLookup.Update(ref state);
        }

        if (!state.EntityManager.Exists(gridEntity) || !flowFieldDataLookup.HasComponent(gridEntity))
            return;

        if (!state.EntityManager.Exists(gridEntityDatabaseEntity) ||
            !state.EntityManager.HasBuffer<GridEntityElement>(gridEntityDatabaseEntity))
        {
            return;
        }

        FlowFieldData flowFieldData = flowFieldDataLookup.GetRefRO(gridEntity).ValueRO;

        MovementPathDebugLogSettings debugSettings = SystemAPI.HasSingleton<MovementPathDebugLogSettings>()
            ? SystemAPI.GetSingleton<MovementPathDebugLogSettings>()
            : MovementPathDebugLogSettings.Disabled();

        if (flowFieldData.CellSize <= 0f)
            return;

        int bufferSize = flowFieldData.GridSize.x * flowFieldData.GridSize.y;
        if (bufferSize <= 0)
            return;

        NativeHashMap<int2, int> commandCounts = new NativeHashMap<int2, int>(1024, Allocator.Temp);

        bool hasAnyWork = false;
        bool hasFlowFieldRequests = false;

        // first pass: count real requests by quantized target
        // also finds cheap direct-target work so second pass can be skipped
        foreach ((RefRO<GroupManagerComponent> gmc, Entity entity)
            in SystemAPI.Query<RefRO<GroupManagerComponent>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            if (gmc.ValueRO.setTargetWithoutGroup)
                hasAnyWork = true;

            if (!gmc.ValueRO.addOrCreateGroup)
                continue;

            hasAnyWork = true;
            hasFlowFieldRequests = true;

            int2 qPos = QuantizePosition(gmc.ValueRO.position, flowFieldData.StartCoordinate, flowFieldData.CellSize);

            if (commandCounts.TryGetValue(qPos, out int count))
                commandCounts[qPos] = count + 1;
            else
                commandCounts.Add(qPos, 1);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugSettings.Enabled && debugSettings.LogGroupManager && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
                Debug.Log($"[MovePath 05 GM_COUNT] ship={entity.Index}:{entity.Version} target=({gmc.ValueRO.position.x},{gmc.ValueRO.position.y}) q=({qPos.x},{qPos.y})");
#endif
        }

        if (!hasAnyWork)
        {
            commandCounts.Dispose();
            return;
        }

        NativeHashMap<int3, Entity> existingGroups = new NativeHashMap<int3, Entity>(1024, Allocator.Temp);
        NativeHashMap<int3, GroupCacheEntry> groupsThisFrame = new NativeHashMap<int3, GroupCacheEntry>(1024, Allocator.Temp);

        if (hasFlowFieldRequests)
            BuildExistingGroupLookup(ref state, ref existingGroups);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        bool hasEcbCommands = false;

        foreach ((
            RefRW<ShipStateComponent> shipState,
            RefRW<UnitMover> unitMover,
            RefRW<GroupManagerComponent> groupManagerComponent,
            RefRW<UnitGroup> unitGroup,
            Entity entity)
            in SystemAPI.Query<
                    RefRW<ShipStateComponent>,
                    RefRW<UnitMover>,
                    RefRW<GroupManagerComponent>,
                    RefRW<UnitGroup>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            if (groupManagerComponent.ValueRO.setTargetWithoutGroup)
            {
                groupManagerComponent.ValueRW.setTargetWithoutGroup = false;
                unitMover.ValueRW.targetPos = groupManagerComponent.ValueRO.position;

                UnitGroup directUnitGroup = unitGroup.ValueRO;
                directUnitGroup.GroupEntity = Entity.Null;
                unitGroup.ValueRW = directUnitGroup;

                shipState.ValueRW.previousState = shipState.ValueRO.currentState;
                shipState.ValueRW.currentState = ShipState.MovingToTarget;
            }

            if (!groupManagerComponent.ValueRO.addOrCreateGroup)
                continue;

            groupManagerComponent.ValueRW.addOrCreateGroup = false;

            float2 targetPosition = groupManagerComponent.ValueRO.position;
            int2 qPos = QuantizePosition(targetPosition, flowFieldData.StartCoordinate, flowFieldData.CellSize);
            int groupSize = commandCounts.TryGetValue(qPos, out int count) ? count : 1;
            PathfindingSizeClass sizeClass = ResolveSizeClass(entity, groupManagerComponent.ValueRO);
            int3 cacheKey = new int3(qPos.x, qPos.y, (int)sizeClass);

            if (groupSize <= SmallGroupThreshold && !groupManagerComponent.ValueRO.partOfSwarm)
            {
                unitMover.ValueRW.targetPos = targetPosition;

                UnitGroup directMoveUnitGroup = unitGroup.ValueRO;
                directMoveUnitGroup.GroupEntity = Entity.Null;
                unitGroup.ValueRW = directMoveUnitGroup;

                shipState.ValueRW.previousState = shipState.ValueRO.currentState;
                shipState.ValueRW.currentState = ShipState.MovingToTarget;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugSettings.Enabled && debugSettings.LogGroupManager && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
                    Debug.Log($"[MovePath 06 GM_DIRECT_SMALL] ship={entity.Index}:{entity.Version} groupSize={groupSize} threshold={SmallGroupThreshold} target=({targetPosition.x},{targetPosition.y}) reason=smallGroup GroupEntity=NULL");
#endif

                continue;
            }

            GroupCacheEntry cacheEntry;
            if (!groupsThisFrame.TryGetValue(cacheKey, out cacheEntry))
            {
                if (existingGroups.TryGetValue(cacheKey, out Entity existingGroupEntity) && IsValidGroupEntity(ref state, existingGroupEntity))
                {
                    cacheEntry = new GroupCacheEntry
                    {
                        GroupEntity = existingGroupEntity,
                        RequiresEcbRemap = 0
                    };

                    RefreshExistingGroupIfNeeded(ref state, existingGroupEntity, targetPosition, sizeClass, flowFieldData);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (debugSettings.Enabled && debugSettings.LogGroupManager && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
                    {
                        string mode = groupManagerComponent.ValueRO.partOfSwarm ? "swarm" : "normal";
                        Debug.Log($"[MovePath 06 GM_REUSE_GROUP] ship={entity.Index}:{entity.Version} mode={mode} group={existingGroupEntity.Index}:{existingGroupEntity.Version} q=({qPos.x},{qPos.y}) size={(int)sizeClass} target=({targetPosition.x},{targetPosition.y})");
                    }
#endif
                }
                else
                {
                    CreateGroupEntity(ref ecb, out Entity newGroupEntity, targetPosition, bufferSize, sizeClass);
                    hasEcbCommands = true;

                    ecb.AppendToBuffer(gridEntityDatabaseEntity, new GridEntityElement
                    {
                        Key = qPos,
                        SizeClass = (byte)sizeClass,
                        Value = newGroupEntity
                    });

                    cacheEntry = new GroupCacheEntry
                    {
                        GroupEntity = newGroupEntity,
                        RequiresEcbRemap = 1
                    };

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (debugSettings.Enabled && debugSettings.LogGroupManager && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
                    {
                        string mode = groupManagerComponent.ValueRO.partOfSwarm ? "swarm" : "normal";
                        Debug.Log($"[MovePath 06 GM_CREATE_GROUP] ship={entity.Index}:{entity.Version} mode={mode} tempGroup={newGroupEntity.Index}:{newGroupEntity.Version} q=({qPos.x},{qPos.y}) size={(int)sizeClass} target=({targetPosition.x},{targetPosition.y}) bufferSize={bufferSize}");
                    }
#endif
                }

                groupsThisFrame.Add(cacheKey, cacheEntry);
            }

            unitMover.ValueRW.targetPos = targetPosition;

            UnitGroup flowFieldUnitGroup = unitGroup.ValueRO;
            flowFieldUnitGroup.GroupEntity = cacheEntry.GroupEntity;

            if (cacheEntry.RequiresEcbRemap != 0)
            {
                ecb.SetComponent(entity, flowFieldUnitGroup);
                hasEcbCommands = true;
            }
            else
            {
                unitGroup.ValueRW = flowFieldUnitGroup;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugSettings.Enabled && debugSettings.LogGroupManager && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
                Debug.Log($"[MovePath 07 GM_ASSIGN_FLOWFIELD] ship={entity.Index}:{entity.Version} group={cacheEntry.GroupEntity.Index}:{cacheEntry.GroupEntity.Version} groupSize={groupSize} size={(int)sizeClass} target=({targetPosition.x},{targetPosition.y})");
#endif

            shipState.ValueRW.previousState = shipState.ValueRO.currentState;
            shipState.ValueRW.currentState = ShipState.MovingToTarget;
        }

        commandCounts.Dispose();
        existingGroups.Dispose();
        groupsThisFrame.Dispose();

        if (hasEcbCommands)
            ecb.Playback(state.EntityManager);

        ecb.Dispose();
    }

    private void BuildExistingGroupLookup(ref SystemState state, ref NativeHashMap<int3, Entity> existingGroups)
    {
        if (!state.EntityManager.Exists(gridEntityDatabaseEntity) ||
            !state.EntityManager.HasBuffer<GridEntityElement>(gridEntityDatabaseEntity))
        {
            return;
        }

        DynamicBuffer<GridEntityElement> buffer = state.EntityManager.GetBuffer<GridEntityElement>(gridEntityDatabaseEntity);

        for (int i = buffer.Length - 1; i >= 0; i--)
        {
            GridEntityElement element = buffer[i];
            if (!IsValidGroupEntity(ref state, element.Value))
            {
                buffer.RemoveAt(i);
                continue;
            }
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            GridEntityElement element = buffer[i];
            int3 key = new int3(element.Key.x, element.Key.y, element.SizeClass);
            existingGroups.TryAdd(key, element.Value);
        }
    }

    private void RefreshExistingGroupIfNeeded(
        ref SystemState state,
        Entity groupEntity,
        float2 targetPosition,
        PathfindingSizeClass sizeClass,
        in FlowFieldData flowFieldData)
    {
        GroupGridCell groupGridCell = state.EntityManager.GetComponentData<GroupGridCell>(groupEntity);

        if (CanKeepExistingGroupReady(groupGridCell, targetPosition, sizeClass, flowFieldData))
            return;

        groupGridCell.TargetPosition = targetPosition;
        groupGridCell.NeedsUpdate = true;
        groupGridCell.ReadyToMove = false;
        groupGridCell.SizeClass = sizeClass;
        groupGridCell.FailedAttempts = 0;

        state.EntityManager.SetComponentData(groupEntity, groupGridCell);
    }

    private bool CanKeepExistingGroupReady(
        in GroupGridCell groupGridCell,
        float2 targetPosition,
        PathfindingSizeClass sizeClass,
        in FlowFieldData flowFieldData)
    {
        if (groupGridCell.NeedsUpdate || !groupGridCell.ReadyToMove || !groupGridCell.HasCachedFlowField)
            return false;

        if (groupGridCell.SizeClass != sizeClass || groupGridCell.CachedSizeClass != sizeClass)
            return false;

        if (groupGridCell.CachedObstacleBakeVersion != flowFieldData.ObstacleBakeVersion)
            return false;

        float2 requestedTargetPosition = FlowFieldUtility.ClampToFlowFieldGrid(
            targetPosition,
            flowFieldData.StartCoordinate,
            flowFieldData.GridSize,
            flowFieldData.CellSize);

        int2 requestedTargetCell = FlowFieldUtility.FlowFieldGridPos(
            requestedTargetPosition,
            flowFieldData.StartCoordinate,
            flowFieldData.CellSize);

        return math.all(groupGridCell.CachedTargetCell == requestedTargetCell);
    }

    private PathfindingSizeClass ResolveSizeClass(Entity entity, in GroupManagerComponent gmc)
    {
        if (gmc.useOverrideSizeClass)
            return gmc.overrideSizeClass;

        if (pathSizeClassLookup.HasComponent(entity))
            return pathSizeClassLookup[entity].Value;

        return PathfindingSizeClass.Medium;
    }

    private GroupGridCell CreateGroupGridCell(float2 targetPosition, PathfindingSizeClass sizeClass)
    {
        return new GroupGridCell
        {
            TargetPosition = targetPosition,
            NeedsUpdate = true,
            ReadyToMove = false,
            SizeClass = sizeClass,
            FailedAttempts = 0,
            HasCachedFlowField = false,
            CachedTargetCell = new int2(int.MinValue, int.MinValue),
            CachedResolvedTargetCell = new int2(int.MinValue, int.MinValue),
            CachedSizeClass = sizeClass,
            CachedObstacleBakeVersion = uint.MaxValue,
        };
    }

    private void ClearRuntimeFlowFieldGroups(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach ((RefRO<GroupGridCell> _, Entity entity)
            in SystemAPI.Query<RefRO<GroupGridCell>>().WithEntityAccess())
        {
            ecb.DestroyEntity(entity);
        }

        if (state.EntityManager.Exists(gridEntityDatabaseEntity) &&
            state.EntityManager.HasBuffer<GridEntityElement>(gridEntityDatabaseEntity))
        {
            DynamicBuffer<GridEntityElement> buffer = state.EntityManager.GetBuffer<GridEntityElement>(gridEntityDatabaseEntity);
            buffer.Clear();
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private int2 QuantizePosition(float2 position, float2 startCoordinat, float cellSize)
    {
        int2 gridPos = FlowFieldUtility.FlowFieldGridPos(position, startCoordinat, cellSize);
        return new int2(gridPos.x / QuantizationSize, gridPos.y / QuantizationSize);
    }

    private bool IsValidGroupEntity(ref SystemState state, Entity entity)
    {
        return entity != Entity.Null &&
               state.EntityManager.Exists(entity) &&
               state.EntityManager.HasComponent<GroupGridCell>(entity);
    }

    private void CreateGroupEntity(
        ref EntityCommandBuffer ecb,
        out Entity groupEntity,
        float2 targetPosition,
        int bufferSize,
        PathfindingSizeClass sizeClass)
    {
        groupEntity = ecb.CreateEntity();
        ecb.AddComponent(groupEntity, CreateGroupGridCell(targetPosition, sizeClass));

        DynamicBuffer<GroupCell> buffer = ecb.AddBuffer<GroupCell>(groupEntity);
        buffer.Resize(bufferSize, NativeArrayOptions.ClearMemory);
    }
}
