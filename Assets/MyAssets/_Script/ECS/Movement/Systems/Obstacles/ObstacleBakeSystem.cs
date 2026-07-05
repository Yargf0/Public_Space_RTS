using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// bakes obstacles into raw GridCell and per-size path layers
// GridCell = physical map, path layers add size clearance
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FlowFieldInitSystem))]
[UpdateBefore(typeof(FlowFieldUpdateSystem))]
public partial struct ObstacleBakeSystem : ISystem
{
    private BufferLookup<GridCell> gridCellLookup;
    private BufferLookup<PathGridCellSmall> smallLookup;
    private BufferLookup<PathGridCellMedium> mediumLookup;
    private BufferLookup<PathGridCellLarge> largeLookup;

    private Entity bakedFlowFieldEntity;
    private bool hasBakedFlowFieldEntity;
    private uint lastBakeHash;
    private bool hasLastBakeHash;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        gridCellLookup = state.GetBufferLookup<GridCell>(isReadOnly: false);
        smallLookup = state.GetBufferLookup<PathGridCellSmall>(isReadOnly: false);
        mediumLookup = state.GetBufferLookup<PathGridCellMedium>(isReadOnly: false);
        largeLookup = state.GetBufferLookup<PathGridCellLarge>(isReadOnly: false);

        state.RequireForUpdate<FlowFieldData>();

        bakedFlowFieldEntity = Entity.Null;
        hasBakedFlowFieldEntity = false;
        lastBakeHash = 0u;
        hasLastBakeHash = false;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        gridCellLookup.Update(ref state);
        smallLookup.Update(ref state);
        mediumLookup.Update(ref state);
        largeLookup.Update(ref state);

        Entity flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();


        if (!gridCellLookup.HasBuffer(flowFieldEntity) ||
            !smallLookup.HasBuffer(flowFieldEntity) ||
            !mediumLookup.HasBuffer(flowFieldEntity) ||
            !largeLookup.HasBuffer(flowFieldEntity))
        {
            return;
        }

        DynamicBuffer<GridCell> gridCells = gridCellLookup[flowFieldEntity];
        DynamicBuffer<PathGridCellSmall> small = smallLookup[flowFieldEntity];
        DynamicBuffer<PathGridCellMedium> medium = mediumLookup[flowFieldEntity];
        DynamicBuffer<PathGridCellLarge> large = largeLookup[flowFieldEntity];

        if (gridCells.Length == 0 || small.Length == 0 || medium.Length == 0 || large.Length == 0)
            return;

        FlowFieldData fieldData = SystemAPI.GetSingleton<FlowFieldData>();
        if (fieldData.CellSize <= 0f || fieldData.GridSize.x <= 0 || fieldData.GridSize.y <= 0)
            return;

        ObstacleAvoidanceSettings settings = SystemAPI.HasSingleton<ObstacleAvoidanceSettings>()
            ? SystemAPI.GetSingleton<ObstacleAvoidanceSettings>()
            : ObstacleAvoidanceSettings.CreateDefault();

        float largeClearance = ComputeEffectiveLargeClearance(ref state, fieldData, settings);

        uint bakeHash = ComputeBakeHash(ref state, fieldData, settings, largeClearance);
        if (hasBakedFlowFieldEntity &&
            bakedFlowFieldEntity == flowFieldEntity &&
            hasLastBakeHash &&
            lastBakeHash == bakeHash)
        {
            return;
        }

        ResetGridCell(gridCells);
        ResetSmall(small);
        ResetMedium(medium);
        ResetLarge(large);

        BakeGridLayer(ref state, gridCells, fieldData, settings, 0f);
        BakeSmallLayer(ref state, small, fieldData, settings, math.max(0f, settings.PathClearanceSmall));
        BakeMediumLayer(ref state, medium, fieldData, settings, math.max(0f, settings.PathClearanceMedium));
        BakeLargeLayer(ref state, large, fieldData, settings, largeClearance);

        // bump only after real rebake so cached flow fields rebuild
        fieldData.ObstacleBakeVersion++;
        state.EntityManager.SetComponentData(flowFieldEntity, fieldData);

        InvalidateExistingFlowFields(ref state);

        bakedFlowFieldEntity = flowFieldEntity;
        hasBakedFlowFieldEntity = true;
        lastBakeHash = bakeHash;
        hasLastBakeHash = true;
    }

    [BurstCompile]
    private float ComputeEffectiveLargeClearance(ref SystemState state, in FlowFieldData fieldData, in ObstacleAvoidanceSettings settings)
    {
        float maxExtent = 0f;

        foreach ((RefRO<UnitCollisionRadius> collisionRadius, RefRO<PathfindingSizeClassComponent> sizeClass)
            in SystemAPI.Query<RefRO<UnitCollisionRadius>, RefRO<PathfindingSizeClassComponent>>())
        {
            if (sizeClass.ValueRO.Value != PathfindingSizeClass.Large)
                continue;

            maxExtent = math.max(maxExtent, math.cmax(math.abs(collisionRadius.ValueRO.collisionRadius)));
        }

        float authoredClearance = math.max(0f, settings.PathClearanceLarge);
        if (maxExtent <= 0f)
            return authoredClearance;

        return math.max(authoredClearance, maxExtent + fieldData.CellSize * 0.5f);
    }

    [BurstCompile]
    private uint ComputeBakeHash(ref SystemState state, in FlowFieldData fieldData, in ObstacleAvoidanceSettings settings, float largeClearance)
    {
        uint hash = 2166136261u;

        HashCombine(ref hash, (uint)fieldData.GridSize.x);
        HashCombine(ref hash, (uint)fieldData.GridSize.y);
        HashCombine(ref hash, math.asuint(fieldData.CellSize));
        HashCombine(ref hash, math.asuint(fieldData.StartCoordinate.x));
        HashCombine(ref hash, math.asuint(fieldData.StartCoordinate.y));

        HashCombine(ref hash, (uint)settings.SoftCost);
        HashCombine(ref hash, math.asuint(settings.PathClearanceSmall));
        HashCombine(ref hash, math.asuint(settings.PathClearanceMedium));
        HashCombine(ref hash, math.asuint(largeClearance));

        uint obstacleCount = 0u;
        foreach ((RefRO<ObstacleSourceComponent> obstacle, RefRO<LocalTransform> transform)
            in SystemAPI.Query<RefRO<ObstacleSourceComponent>, RefRO<LocalTransform>>())
        {
            obstacleCount++;
            HashCombine(ref hash, math.asuint(obstacle.ValueRO.Radius));
            HashCombine(ref hash, math.asuint(obstacle.ValueRO.SoftMargin));
            HashCombine(ref hash, math.asuint(transform.ValueRO.Position.x));
            HashCombine(ref hash, math.asuint(transform.ValueRO.Position.y));
        }

        HashCombine(ref hash, obstacleCount);
        return hash;
    }

    private static void HashCombine(ref uint hash, uint value)
    {
        hash ^= value;
        hash *= 16777619u;
    }

    private static void ResetGridCell(DynamicBuffer<GridCell> gridCells)
    {
        for (int i = 0; i < gridCells.Length; i++)
        {
            GridCell cell = gridCells[i];
            cell.Cost = 1;
            cell.Walkable = true;
            gridCells[i] = cell;
        }
    }

    private static void ResetSmall(DynamicBuffer<PathGridCellSmall> layer)
    {
        for (int i = 0; i < layer.Length; i++)
            layer[i] = new PathGridCellSmall { Cost = 1, Walkable = true };
    }

    private static void ResetMedium(DynamicBuffer<PathGridCellMedium> layer)
    {
        for (int i = 0; i < layer.Length; i++)
            layer[i] = new PathGridCellMedium { Cost = 1, Walkable = true };
    }

    private static void ResetLarge(DynamicBuffer<PathGridCellLarge> layer)
    {
        for (int i = 0; i < layer.Length; i++)
            layer[i] = new PathGridCellLarge { Cost = 1, Walkable = true };
    }

    [BurstCompile]
    private void BakeGridLayer(
        ref SystemState state,
        DynamicBuffer<GridCell> layer,
        in FlowFieldData fieldData,
        in ObstacleAvoidanceSettings settings,
        float clearance)
    {
        int softCost = math.max(2, settings.SoftCost);

        foreach ((RefRO<ObstacleSourceComponent> obstacle, RefRO<LocalTransform> transform)
            in SystemAPI.Query<RefRO<ObstacleSourceComponent>, RefRO<LocalTransform>>())
        {
            int2 centerCell = FlowFieldUtility.FlowFieldGridPos(transform.ValueRO.Position.xy, fieldData.StartCoordinate, fieldData.CellSize);
            float hardWorldRadius = math.max(0f, obstacle.ValueRO.Radius + clearance);
            float softWorldRadius = math.max(hardWorldRadius, hardWorldRadius + math.max(0f, obstacle.ValueRO.SoftMargin));
            int hardCellRadius = math.max(1, (int)math.ceil(hardWorldRadius / fieldData.CellSize));
            int softCellRadius = math.max(hardCellRadius, (int)math.ceil(softWorldRadius / fieldData.CellSize));

            for (int dy = -softCellRadius; dy <= softCellRadius; dy++)
            {
                for (int dx = -softCellRadius; dx <= softCellRadius; dx++)
                {
                    int2 cell = centerCell + new int2(dx, dy);
                    if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize))
                        continue;

                    float dist = math.length(new float2(dx, dy));
                    if (dist > softCellRadius)
                        continue;

                    int index = FlowFieldUtility.PosToIndex(cell, fieldData.GridSize.x);
                    if (index < 0 || index >= layer.Length)
                        continue;

                    GridCell oldCell = layer[index];
                    bool walkable = oldCell.Walkable;
                    int cost = oldCell.Cost;

                    if (dist <= hardCellRadius)
                    {
                        walkable = false;
                        cost = math.max(cost, 256);
                    }
                    else
                    {
                        cost = math.max(cost, softCost);
                    }

                    layer[index] = new GridCell
                    {
                        Index = oldCell.Index,
                        Cost = cost,
                        Walkable = walkable,
                    };
                }
            }
        }
    }

    [BurstCompile]
    private void BakeSmallLayer(ref SystemState state, DynamicBuffer<PathGridCellSmall> layer, in FlowFieldData fieldData, in ObstacleAvoidanceSettings settings, float clearance)
    {
        int softCost = math.max(2, settings.SoftCost);
        foreach ((RefRO<ObstacleSourceComponent> obstacle, RefRO<LocalTransform> transform) in SystemAPI.Query<RefRO<ObstacleSourceComponent>, RefRO<LocalTransform>>())
        {
            int2 centerCell = FlowFieldUtility.FlowFieldGridPos(transform.ValueRO.Position.xy, fieldData.StartCoordinate, fieldData.CellSize);
            float hardWorldRadius = math.max(0f, obstacle.ValueRO.Radius + clearance);
            float softWorldRadius = math.max(hardWorldRadius, hardWorldRadius + math.max(0f, obstacle.ValueRO.SoftMargin));
            int hardCellRadius = math.max(1, (int)math.ceil(hardWorldRadius / fieldData.CellSize));
            int softCellRadius = math.max(hardCellRadius, (int)math.ceil(softWorldRadius / fieldData.CellSize));
            for (int dy = -softCellRadius; dy <= softCellRadius; dy++)
            for (int dx = -softCellRadius; dx <= softCellRadius; dx++)
            {
                int2 cell = centerCell + new int2(dx, dy);
                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize)) continue;
                float dist = math.length(new float2(dx, dy));
                if (dist > softCellRadius) continue;
                int index = FlowFieldUtility.PosToIndex(cell, fieldData.GridSize.x);
                if (index < 0 || index >= layer.Length) continue;
                PathGridCellSmall oldCell = layer[index];
                bool walkable = oldCell.Walkable;
                int cost = oldCell.Cost;
                if (dist <= hardCellRadius) { walkable = false; cost = math.max(cost, 256); }
                else { cost = math.max(cost, softCost); }
                layer[index] = new PathGridCellSmall { Cost = cost, Walkable = walkable };
            }
        }
    }

    [BurstCompile]
    private void BakeMediumLayer(ref SystemState state, DynamicBuffer<PathGridCellMedium> layer, in FlowFieldData fieldData, in ObstacleAvoidanceSettings settings, float clearance)
    {
        int softCost = math.max(2, settings.SoftCost);
        foreach ((RefRO<ObstacleSourceComponent> obstacle, RefRO<LocalTransform> transform) in SystemAPI.Query<RefRO<ObstacleSourceComponent>, RefRO<LocalTransform>>())
        {
            int2 centerCell = FlowFieldUtility.FlowFieldGridPos(transform.ValueRO.Position.xy, fieldData.StartCoordinate, fieldData.CellSize);
            float hardWorldRadius = math.max(0f, obstacle.ValueRO.Radius + clearance);
            float softWorldRadius = math.max(hardWorldRadius, hardWorldRadius + math.max(0f, obstacle.ValueRO.SoftMargin));
            int hardCellRadius = math.max(1, (int)math.ceil(hardWorldRadius / fieldData.CellSize));
            int softCellRadius = math.max(hardCellRadius, (int)math.ceil(softWorldRadius / fieldData.CellSize));
            for (int dy = -softCellRadius; dy <= softCellRadius; dy++)
            for (int dx = -softCellRadius; dx <= softCellRadius; dx++)
            {
                int2 cell = centerCell + new int2(dx, dy);
                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize)) continue;
                float dist = math.length(new float2(dx, dy));
                if (dist > softCellRadius) continue;
                int index = FlowFieldUtility.PosToIndex(cell, fieldData.GridSize.x);
                if (index < 0 || index >= layer.Length) continue;
                PathGridCellMedium oldCell = layer[index];
                bool walkable = oldCell.Walkable;
                int cost = oldCell.Cost;
                if (dist <= hardCellRadius) { walkable = false; cost = math.max(cost, 256); }
                else { cost = math.max(cost, softCost); }
                layer[index] = new PathGridCellMedium { Cost = cost, Walkable = walkable };
            }
        }
    }

    [BurstCompile]
    private void BakeLargeLayer(ref SystemState state, DynamicBuffer<PathGridCellLarge> layer, in FlowFieldData fieldData, in ObstacleAvoidanceSettings settings, float clearance)
    {
        int softCost = math.max(2, settings.SoftCost);
        foreach ((RefRO<ObstacleSourceComponent> obstacle, RefRO<LocalTransform> transform) in SystemAPI.Query<RefRO<ObstacleSourceComponent>, RefRO<LocalTransform>>())
        {
            int2 centerCell = FlowFieldUtility.FlowFieldGridPos(transform.ValueRO.Position.xy, fieldData.StartCoordinate, fieldData.CellSize);
            float hardWorldRadius = math.max(0f, obstacle.ValueRO.Radius + clearance);
            float softWorldRadius = math.max(hardWorldRadius, hardWorldRadius + math.max(0f, obstacle.ValueRO.SoftMargin));
            int hardCellRadius = math.max(1, (int)math.ceil(hardWorldRadius / fieldData.CellSize));
            int softCellRadius = math.max(hardCellRadius, (int)math.ceil(softWorldRadius / fieldData.CellSize));
            for (int dy = -softCellRadius; dy <= softCellRadius; dy++)
            for (int dx = -softCellRadius; dx <= softCellRadius; dx++)
            {
                int2 cell = centerCell + new int2(dx, dy);
                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize)) continue;
                float dist = math.length(new float2(dx, dy));
                if (dist > softCellRadius) continue;
                int index = FlowFieldUtility.PosToIndex(cell, fieldData.GridSize.x);
                if (index < 0 || index >= layer.Length) continue;
                PathGridCellLarge oldCell = layer[index];
                bool walkable = oldCell.Walkable;
                int cost = oldCell.Cost;
                if (dist <= hardCellRadius) { walkable = false; cost = math.max(cost, 256); }
                else { cost = math.max(cost, softCost); }
                layer[index] = new PathGridCellLarge { Cost = cost, Walkable = walkable };
            }
        }
    }

    [BurstCompile]
    private void InvalidateExistingFlowFields(ref SystemState state)
    {
        foreach (RefRW<GroupGridCell> group in SystemAPI.Query<RefRW<GroupGridCell>>())
        {
            group.ValueRW.NeedsUpdate = true;
            group.ValueRW.ReadyToMove = false;
            group.ValueRW.FailedAttempts = 0;
        }
    }
}
