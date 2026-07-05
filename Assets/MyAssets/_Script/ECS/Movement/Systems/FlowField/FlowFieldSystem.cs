using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Builds flow field for movement groups on their size layer.
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GroupManagerSystem))]
[UpdateAfter(typeof(ObstacleBakeSystem))]
[UpdateBefore(typeof(SetTotalVelocitySystem))]
public partial struct FlowFieldUpdateSystem : ISystem
{
    private const int MaxBfsPerFrame = 2;
    private const int MaxFailedAttempts = 3;

    private BufferLookup<PathGridCellSmall> smallLookup;
    private BufferLookup<PathGridCellMedium> mediumLookup;
    private BufferLookup<PathGridCellLarge> largeLookup;

    static readonly int2[] directions4 =
    {
        new(0, 1),
        new(1, 0),
        new(0, -1),
        new(-1, 0)
    };

    static readonly int2[] directions8 =
    {
        new(0, 1),   new(1, 1),   new(1, 0),  new(1, -1),
        new(0, -1),  new(-1, -1), new(-1, 0), new(-1, 1)
    };

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        smallLookup = state.GetBufferLookup<PathGridCellSmall>(isReadOnly: true);
        mediumLookup = state.GetBufferLookup<PathGridCellMedium>(isReadOnly: true);
        largeLookup = state.GetBufferLookup<PathGridCellLarge>(isReadOnly: true);
        state.RequireForUpdate<FlowFieldData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<FlowFieldData>(out FlowFieldData flowFieldData))
            return;

        smallLookup.Update(ref state);
        mediumLookup.Update(ref state);
        largeLookup.Update(ref state);

        Entity flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();
        if (!smallLookup.HasBuffer(flowFieldEntity) || !mediumLookup.HasBuffer(flowFieldEntity) || !largeLookup.HasBuffer(flowFieldEntity))
            return;

        MovementPathDebugLogSettings debugSettings = SystemAPI.HasSingleton<MovementPathDebugLogSettings>()
            ? SystemAPI.GetSingleton<MovementPathDebugLogSettings>()
            : MovementPathDebugLogSettings.Disabled();

        int gridWidth = flowFieldData.GridSize.x;
        int2 gridSize = flowFieldData.GridSize;
        int totalCells = gridSize.x * gridSize.y;
        if (gridWidth <= 0 || gridSize.y <= 0 || totalCells <= 0)
            return;

        int bfsThisFrame = 0;

        foreach ((RefRW<GroupGridCell> groupGridCell, Entity entity)
            in SystemAPI.Query<RefRW<GroupGridCell>>().WithEntityAccess())
        {
            if (!groupGridCell.ValueRO.NeedsUpdate)
                continue;

            if (bfsThisFrame >= MaxBfsPerFrame)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugSettings.Enabled && debugSettings.LogFlowFieldUpdate)
                    Debug.Log($"[MovePath 08 FF_WAIT_LIMIT] group={entity.Index}:{entity.Version} maxBfsPerFrame={MaxBfsPerFrame}");
#endif
                continue;
            }

            DynamicBuffer<GroupCell> groupCells = SystemAPI.GetBuffer<GroupCell>(entity);
            bool bufferWasValid = groupCells.Length == totalCells;
            if (!bufferWasValid)
                groupCells.ResizeUninitialized(totalCells);

            PathfindingSizeClass sizeClass = groupGridCell.ValueRO.SizeClass;
            if (sizeClass != PathfindingSizeClass.Small &&
                sizeClass != PathfindingSizeClass.Medium &&
                sizeClass != PathfindingSizeClass.Large)
            {
                sizeClass = PathfindingSizeClass.Medium;
            }

            float2 requestedTargetPosition = FlowFieldUtility.ClampToFlowFieldGrid(
                groupGridCell.ValueRO.TargetPosition,
                flowFieldData.StartCoordinate,
                flowFieldData.GridSize,
                flowFieldData.CellSize);

            int2 requestedTargetCell = FlowFieldUtility.FlowFieldGridPos(
                requestedTargetPosition,
                flowFieldData.StartCoordinate,
                flowFieldData.CellSize);

            bool cacheHit =
                bufferWasValid &&
                groupGridCell.ValueRO.HasCachedFlowField &&
                math.all(groupGridCell.ValueRO.CachedTargetCell == requestedTargetCell) &&
                groupGridCell.ValueRO.CachedSizeClass == sizeClass &&
                groupGridCell.ValueRO.CachedObstacleBakeVersion == flowFieldData.ObstacleBakeVersion;

            if (cacheHit)
            {
                int2 resolvedCell = groupGridCell.ValueRO.CachedResolvedTargetCell;

                groupGridCell.ValueRW.NeedsUpdate = false;
                groupGridCell.ValueRW.ReadyToMove = true;
                groupGridCell.ValueRW.SizeClass = sizeClass;
                groupGridCell.ValueRW.TargetPosition = math.all(resolvedCell == requestedTargetCell)
                    ? requestedTargetPosition
                    : FlowFieldUtility.FlowFieldGridToWorld(resolvedCell, flowFieldData.StartCoordinate, flowFieldData.CellSize);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugSettings.Enabled && debugSettings.LogFlowFieldUpdate)
                    Debug.Log($"[MovePath 08 FF_CACHE_HIT] group={entity.Index}:{entity.Version} size={(int)sizeClass} targetCell=({requestedTargetCell.x},{requestedTargetCell.y}) resolvedCell=({resolvedCell.x},{resolvedCell.y}) obstacleVersion={flowFieldData.ObstacleBakeVersion}");
#endif
                continue;
            }

            NativeArray<byte> walkable = new NativeArray<byte>(totalCells, Allocator.Temp);
            NativeArray<int> costs = new NativeArray<int>(totalCells, Allocator.Temp);

            switch (sizeClass)
            {
                case PathfindingSizeClass.Small:
                    CopyLayer(smallLookup[flowFieldEntity], walkable, costs);
                    break;
                case PathfindingSizeClass.Large:
                    CopyLayer(largeLookup[flowFieldEntity], walkable, costs);
                    break;
                case PathfindingSizeClass.Medium:
                default:
                    sizeClass = PathfindingSizeClass.Medium;
                    CopyLayer(mediumLookup[flowFieldEntity], walkable, costs);
                    break;
            }

            bfsThisFrame++;
            groupGridCell.ValueRW.NeedsUpdate = false;
            groupGridCell.ValueRW.ReadyToMove = false;
            groupGridCell.ValueRW.SizeClass = sizeClass;
            groupGridCell.ValueRW.TargetPosition = requestedTargetPosition;

            int2 targetGridPos = requestedTargetCell;
            int targetIndex = FlowFieldUtility.PosToIndex(targetGridPos, gridWidth);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugSettings.Enabled && debugSettings.LogFlowFieldUpdate)
            {
                Debug.Log($"[MovePath 08 FF_START] group={entity.Index}:{entity.Version} size={(int)sizeClass} target=({groupGridCell.ValueRO.TargetPosition.x},{groupGridCell.ValueRO.TargetPosition.y}) targetCell=({targetGridPos.x},{targetGridPos.y}) targetIndex={targetIndex} bufferLen={groupCells.Length}");
            }
#endif

            if (targetIndex < 0 || targetIndex >= totalCells || walkable[targetIndex] == 0)
            {
                int2 originalTargetCell = targetGridPos;

                if (!TryFindNearestWalkableCell(targetGridPos, walkable, costs, gridSize, gridWidth, out targetGridPos))
                {
                    int failedAttempts = groupGridCell.ValueRO.FailedAttempts + 1;
                    groupGridCell.ValueRW.FailedAttempts = failedAttempts;
                    groupGridCell.ValueRW.ReadyToMove = false;
                    groupGridCell.ValueRW.NeedsUpdate = failedAttempts < MaxFailedAttempts;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (debugSettings.Enabled && debugSettings.LogFlowFieldUpdate)
                    {
                        if (failedAttempts >= MaxFailedAttempts)
                            Debug.LogWarning($"[MovePath 08 FF_GROUP_STUCK] group={entity.Index}:{entity.Version} size={(int)sizeClass} originalCell=({originalTargetCell.x},{originalTargetCell.y}) failedAttempts={failedAttempts}");
                        else
                            Debug.LogWarning($"[MovePath 08 FF_NO_WALKABLE_TARGET] group={entity.Index}:{entity.Version} size={(int)sizeClass} originalCell=({originalTargetCell.x},{originalTargetCell.y}) failedAttempts={failedAttempts} retryNextFrame=1");
                    }
#endif
                    groupGridCell.ValueRW.HasCachedFlowField = false;

                    walkable.Dispose();
                    costs.Dispose();
                    continue;
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugSettings.Enabled && debugSettings.LogFlowFieldUpdate)
                    Debug.Log($"[MovePath 08 FF_TARGET_FALLBACK] group={entity.Index}:{entity.Version} size={(int)sizeClass} fromCell=({originalTargetCell.x},{originalTargetCell.y}) toCell=({targetGridPos.x},{targetGridPos.y})");
#endif

                targetIndex = FlowFieldUtility.PosToIndex(targetGridPos, gridWidth);
                groupGridCell.ValueRW.TargetPosition = FlowFieldUtility.FlowFieldGridToWorld(
                    targetGridPos,
                    flowFieldData.StartCoordinate,
                    flowFieldData.CellSize);
            }

            for (int i = 0; i < groupCells.Length; i++)
            {
                groupCells[i] = new GroupCell
                {
                    IntegrationValue = int.MaxValue,
                    Direction = float2.zero
                };
            }

            NativeQueue<int2> queue = new NativeQueue<int2>(Allocator.Temp);
            groupCells[targetIndex] = new GroupCell { IntegrationValue = 0, Direction = float2.zero };
            queue.Enqueue(targetGridPos);

            while (queue.Count > 0)
            {
                int2 currentPos = queue.Dequeue();
                int currentIndex = FlowFieldUtility.PosToIndex(currentPos, gridWidth);
                int currentCost = groupCells[currentIndex].IntegrationValue;

                for (int i = 0; i < 4; i++)
                {
                    int2 neighborPos = currentPos + directions4[i];
                    if (!FlowFieldUtility.InBounds(neighborPos, gridSize))
                        continue;

                    int neighborIndex = FlowFieldUtility.PosToIndex(neighborPos, gridWidth);
                    if (walkable[neighborIndex] == 0)
                        continue;

                    int newCost = currentCost + math.max(1, costs[neighborIndex]);
                    if (newCost >= groupCells[neighborIndex].IntegrationValue)
                        continue;

                    groupCells[neighborIndex] = new GroupCell { IntegrationValue = newCost, Direction = float2.zero };
                    queue.Enqueue(neighborPos);
                }
            }

            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    int cellIndex = x + y * gridWidth;
                    if (walkable[cellIndex] == 0 || groupCells[cellIndex].IntegrationValue == 0)
                        continue;

                    int minCost = int.MaxValue;
                    float2 bestDirection = float2.zero;
                    int2 currentCell = new int2(x, y);

                    for (int i = 0; i < 8; i++)
                    {
                        int2 direction = directions8[i];
                        int2 neighborPos = currentCell + direction;

                        if (!FlowFieldUtility.InBounds(neighborPos, gridSize))
                            continue;

                        int neighborIndex = FlowFieldUtility.PosToIndex(neighborPos, gridWidth);
                        if (walkable[neighborIndex] == 0)
                            continue;

                        if (direction.x != 0 && direction.y != 0 &&
                            !CanUseDiagonalDirection(currentCell, direction, walkable, costs, gridSize, gridWidth))
                        {
                            continue;
                        }

                        if (groupCells[neighborIndex].IntegrationValue < minCost)
                        {
                            minCost = groupCells[neighborIndex].IntegrationValue;
                            bestDirection = direction;
                        }
                    }

                    groupCells[cellIndex] = new GroupCell
                    {
                        IntegrationValue = groupCells[cellIndex].IntegrationValue,
                        Direction = bestDirection
                    };
                }
            }

            queue.Dispose();
            walkable.Dispose();
            costs.Dispose();

            groupGridCell.ValueRW.ReadyToMove = true;
            groupGridCell.ValueRW.FailedAttempts = 0;
            groupGridCell.ValueRW.HasCachedFlowField = true;
            groupGridCell.ValueRW.CachedTargetCell = requestedTargetCell;
            groupGridCell.ValueRW.CachedResolvedTargetCell = targetGridPos;
            groupGridCell.ValueRW.CachedSizeClass = sizeClass;
            groupGridCell.ValueRW.CachedObstacleBakeVersion = flowFieldData.ObstacleBakeVersion;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (SystemAPI.HasSingleton<AiDemoFlowFieldDebugSettings>())
            {
                RefRW<AiDemoFlowFieldDebugSettings> visualSettings = SystemAPI.GetSingletonRW<AiDemoFlowFieldDebugSettings>();
                visualSettings.ValueRW.lastBuiltGroupEntity = entity;
                visualSettings.ValueRW.lastBuiltBuildSequence++;
            }


            if (debugSettings.Enabled && debugSettings.LogFlowFieldUpdate)
                Debug.Log($"[MovePath 09 FF_READY] group={entity.Index}:{entity.Version} size={(int)sizeClass} targetCell=({targetGridPos.x},{targetGridPos.y}) targetIndex={targetIndex} ready=1");
#endif
        }
    }

    private static void CopyLayer(DynamicBuffer<PathGridCellSmall> source, NativeArray<byte> walkable, NativeArray<int> costs)
    {
        int len = math.min(source.Length, walkable.Length);
        for (int i = 0; i < len; i++)
        {
            walkable[i] = source[i].Walkable ? (byte)1 : (byte)0;
            costs[i] = math.max(1, source[i].Cost);
        }
    }

    private static void CopyLayer(DynamicBuffer<PathGridCellMedium> source, NativeArray<byte> walkable, NativeArray<int> costs)
    {
        int len = math.min(source.Length, walkable.Length);
        for (int i = 0; i < len; i++)
        {
            walkable[i] = source[i].Walkable ? (byte)1 : (byte)0;
            costs[i] = math.max(1, source[i].Cost);
        }
    }

    private static void CopyLayer(DynamicBuffer<PathGridCellLarge> source, NativeArray<byte> walkable, NativeArray<int> costs)
    {
        int len = math.min(source.Length, walkable.Length);
        for (int i = 0; i < len; i++)
        {
            walkable[i] = source[i].Walkable ? (byte)1 : (byte)0;
            costs[i] = math.max(1, source[i].Cost);
        }
    }

    private static bool CanUseDiagonalDirection(
        int2 currentCell,
        int2 direction,
        NativeArray<byte> walkable,
        NativeArray<int> costs,
        int2 gridSize,
        int gridWidth)
    {
        int2 sideCellA = currentCell + new int2(direction.x, 0);
        int2 sideCellB = currentCell + new int2(0, direction.y);

        if (!FlowFieldUtility.InBounds(sideCellA, gridSize) || !FlowFieldUtility.InBounds(sideCellB, gridSize))
            return false;

        int sideIndexA = FlowFieldUtility.PosToIndex(sideCellA, gridWidth);
        int sideIndexB = FlowFieldUtility.PosToIndex(sideCellB, gridWidth);

        if (sideIndexA < 0 || sideIndexA >= walkable.Length || sideIndexB < 0 || sideIndexB >= walkable.Length)
            return false;

        return walkable[sideIndexA] != 0 && costs[sideIndexA] <= 1 && walkable[sideIndexB] != 0 && costs[sideIndexB] <= 1;
    }

    private static bool TryFindNearestWalkableCell(
        int2 targetGridPos,
        NativeArray<byte> walkable,
        NativeArray<int> costs,
        int2 gridSize,
        int gridWidth,
        out int2 nearestWalkableCell)
    {
        nearestWalkableCell = targetGridPos;

        int maxRadius = math.max(4, math.min(gridSize.x, gridSize.y) / 2);
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            bool foundInThisRing = false;
            int bestCost = int.MaxValue;
            int bestDistanceSq = int.MaxValue;
            int2 bestCell = targetGridPos;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.abs(x) != radius && math.abs(y) != radius)
                        continue;

                    int2 cell = targetGridPos + new int2(x, y);
                    if (!FlowFieldUtility.InBounds(cell, gridSize))
                        continue;

                    int index = FlowFieldUtility.PosToIndex(cell, gridWidth);
                    if (index < 0 || index >= walkable.Length || walkable[index] == 0)
                        continue;

                    int cost = math.max(1, costs[index]);
                    int distanceSq = x * x + y * y;
                    if (!foundInThisRing || cost < bestCost || (cost == bestCost && distanceSq < bestDistanceSq))
                    {
                        foundInThisRing = true;
                        bestCost = cost;
                        bestDistanceSq = distanceSq;
                        bestCell = cell;
                    }
                }
            }

            if (foundInThisRing)
            {
                nearestWalkableCell = bestCell;
                return true;
            }
        }

        return false;
    }
}
