using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// draws FlowField routes in game view for demo
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class AiDemoFlowFieldVisualSystem : SystemBase
{
    // colors for grid and target
    private static readonly Color GridColor = new Color(0.05f, 0.45f, 0.10f, 0.55f);
    private static readonly Color ArrowColor = new Color(0.20f, 1.00f, 0.30f, 0.80f);
    private static readonly Color TraceColor = new Color(0.30f, 1.00f, 0.40f, 1.00f);
    private static readonly Color TargetColor = new Color(0.30f, 1.00f, 1.00f, 1.00f);
    private static readonly Color ShipColor = new Color(1.00f, 1.00f, 1.00f, 0.95f);

    protected override void OnCreate()
    {
        RequireForUpdate<AiDemoFlowFieldDebugSettings>();
        RequireForUpdate<FlowFieldData>();
    }

    protected override void OnUpdate()
    {
        AiDemoFlowFieldDebugSettings settings = SystemAPI.GetSingleton<AiDemoFlowFieldDebugSettings>();
        if (settings.enabled == 0) return;
        if (!AiDemoLineRenderer.IsAvailable) return;

        Entity flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();

        BufferLookup<GridCell> gridCellLookup = GetBufferLookup<GridCell>(isReadOnly: true);
        BufferLookup<GroupCell> groupCellLookup = GetBufferLookup<GroupCell>(isReadOnly: true);

        if (!gridCellLookup.HasBuffer(flowFieldEntity)) return;

        DynamicBuffer<GridCell> gridCells = gridCellLookup[flowFieldEntity];
        if (gridCells.Length == 0) return;

        FlowFieldData fieldData = SystemAPI.GetSingleton<FlowFieldData>();

        // base grid is shared, draw once per frame
        if (settings.drawGrid != 0)
        {
            DrawGrid(fieldData, settings.renderZ);
        }

        // pick which group to draw
        if (settings.allGroupsInsteadOfLast != 0)
        {
            // show all groups if enabled
            foreach ((RefRO<GroupGridCell> ggc, Entity ge) in
                     SystemAPI.Query<RefRO<GroupGridCell>>().WithEntityAccess())
            {
                if (!groupCellLookup.HasBuffer(ge)) continue;
                DrawForGroup(ge, ggc.ValueRO, groupCellLookup[ge], gridCells, fieldData, settings);
            }
        }
        else
        {
            // default - only last built group
            Entity lastGroup = FindLastBuiltGroupOrFallback();
            if (lastGroup == Entity.Null) return;

            GroupGridCell ggc = EntityManager.GetComponentData<GroupGridCell>(lastGroup);
            if (!groupCellLookup.HasBuffer(lastGroup)) return;
            DrawForGroup(lastGroup, ggc, groupCellLookup[lastGroup], gridCells, fieldData, settings);
        }
    }

    // Last built group.

    private Entity FindLastBuiltGroupOrFallback()
    {
        if (SystemAPI.HasSingleton<AiDemoFlowFieldDebugSettings>())
        {
            AiDemoFlowFieldDebugSettings settings = SystemAPI.GetSingleton<AiDemoFlowFieldDebugSettings>();
            Entity groupEntity = settings.lastBuiltGroupEntity;

            if (groupEntity != Entity.Null && EntityManager.Exists(groupEntity) && EntityManager.HasComponent<GroupGridCell>(groupEntity))
            {
                return groupEntity;
            }
        }

        return FindLastGroup();
    }

    private Entity FindLastGroup()
    {
        Entity result = Entity.Null;
        int maxIndex = -1;

        // manual query, we only need Entity here
        EntityQuery q = SystemAPI.QueryBuilder().WithAll<GroupGridCell>().Build();
        NativeArray<Entity> ents = q.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < ents.Length; i++)
        {
            if (ents[i].Index > maxIndex)
            {
                maxIndex = ents[i].Index;
                result = ents[i];
            }
        }
        ents.Dispose();
        return result;
    }

    // Group rendering.

    private void DrawForGroup(
        Entity groupEntity,
        in GroupGridCell groupGridCell,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        in AiDemoFlowFieldDebugSettings settings)
    {
        if (groupCells.Length == 0) return;

        // Target.
        if (settings.drawTarget != 0)
        {
            DrawTargetCell(groupGridCell, fieldData, settings.renderZ);
        }

        // arrows for every cell, slow on big grids
        if (settings.drawAllArrows != 0)
        {
            DrawAllArrows(groupCells, gridCells, fieldData, settings);
        }

        // Ship traces for this group.
        if (settings.drawShipTraces != 0 || settings.drawShipCells != 0)
        {
            DrawShipsOfGroup(groupEntity, groupCells, fieldData, settings);
        }
    }

    // Dense arrow field.

    private static void DrawAllArrows(
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        in AiDemoFlowFieldDebugSettings settings)
    {
        int width = fieldData.GridSize.x;
        int height = fieldData.GridSize.y;
        int cellCount = math.min(groupCells.Length, gridCells.Length);
        int maxArrows = math.max(1, settings.maxArrowsPerFrame);
        int drawn = 0;

        for (int index = 0; index < cellCount; index++)
        {
            if (drawn >= maxArrows) return;

            int2 cell = new int2(index % width, index / width);
            if (cell.y < 0 || cell.y >= height) continue;

            GridCell gc = gridCells[index];
            GroupCell pc = groupCells[index];

            bool reachable = pc.IntegrationValue != int.MaxValue;
            bool hasDir = math.lengthsq(pc.Direction) > 0.0001f;

            if (!reachable || !gc.Walkable) continue;

            if (hasDir)
            {
                DrawDirectionArrow(cell, pc.Direction, fieldData, settings, ArrowColor);
                drawn++;
            }
        }
    }

    // Group ships and traces.

    private void DrawShipsOfGroup(
        Entity groupEntity,
        DynamicBuffer<GroupCell> groupCells,
        in FlowFieldData fieldData,
        in AiDemoFlowFieldDebugSettings settings)
    {
        foreach (var (transform, unitGroup) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitGroup>>())
        {
            if (unitGroup.ValueRO.GroupEntity != groupEntity) continue;

            float2 shipPos = transform.ValueRO.Position.xy;
            int2 shipCell = FlowFieldUtility.FlowFieldGridPos(shipPos, fieldData.StartCoordinate, fieldData.CellSize);
            if (!FlowFieldUtility.InBounds(shipCell, fieldData.GridSize)) continue;

            // outline of current ship cell
            if (settings.drawShipCells != 0)
            {
                DrawCellOutline(shipCell, fieldData, settings.renderZ, ShipColor);
            }

            // Path trace.
            if (settings.drawShipTraces != 0)
            {
                TraceRoute(shipCell, groupCells, fieldData, settings);
            }
        }
    }

    private static void TraceRoute(
        int2 startCell,
        DynamicBuffer<GroupCell> groupCells,
        in FlowFieldData fieldData,
        in AiDemoFlowFieldDebugSettings settings)
    {
        int width = fieldData.GridSize.x;
        int maxSteps = math.max(2, settings.maxTraceSteps);

        int2 current = startCell;
        float2 currentWorld = FlowFieldUtility.FlowFieldGridToWorld(current, fieldData.StartCoordinate, fieldData.CellSize);

        for (int step = 0; step < maxSteps; step++)
        {
            int idx = FlowFieldUtility.PosToIndex(current, width);
            if (idx < 0 || idx >= groupCells.Length) return;

            GroupCell gc = groupCells[idx];

            // Target reached.
            if (gc.IntegrationValue == 0) return;

            // dead end or no direction
            if (math.lengthsq(gc.Direction) < 0.0001f) return;

            float2 dir = math.normalizesafe(gc.Direction);

            // go to next cell by dominant direction
            int dx = math.abs(dir.x) > 0.3f ? (dir.x > 0f ? 1 : -1) : 0;
            int dy = math.abs(dir.y) > 0.3f ? (dir.y > 0f ? 1 : -1) : 0;
            if (dx == 0 && dy == 0) return;

            int2 next = current + new int2(dx, dy);
            if (!FlowFieldUtility.InBounds(next, fieldData.GridSize)) return;

            float2 nextWorld = FlowFieldUtility.FlowFieldGridToWorld(next, fieldData.StartCoordinate, fieldData.CellSize);

            // Trace segment between cell centers.
            AiDemoLineRenderer.AddLine(
                new Vector3(currentWorld.x, currentWorld.y, settings.renderZ),
                new Vector3(nextWorld.x, nextWorld.y, settings.renderZ),
                TraceColor);

            current = next;
            currentWorld = nextWorld;
        }
    }

    // Grid.

    private static void DrawGrid(in FlowFieldData fieldData, float z)
    {
        float2 origin = fieldData.StartCoordinate;
        int w = fieldData.GridSize.x;
        int h = fieldData.GridSize.y;
        float cs = fieldData.CellSize;

        for (int x = 0; x <= w; x++)
        {
            float wx = origin.x + x * cs;
            AiDemoLineRenderer.AddLine(
                new Vector3(wx, origin.y, z),
                new Vector3(wx, origin.y + h * cs, z),
                GridColor);
        }
        for (int y = 0; y <= h; y++)
        {
            float wy = origin.y + y * cs;
            AiDemoLineRenderer.AddLine(
                new Vector3(origin.x, wy, z),
                new Vector3(origin.x + w * cs, wy, z),
                GridColor);
        }
    }

    // Target cell.

    private static void DrawTargetCell(in GroupGridCell groupGridCell, in FlowFieldData fieldData, float z)
    {
        int2 targetCell = FlowFieldUtility.FlowFieldGridPos(
            groupGridCell.TargetPosition,
            fieldData.StartCoordinate,
            fieldData.CellSize);

        if (!FlowFieldUtility.InBounds(targetCell, fieldData.GridSize)) return;

        DrawCellOutline(targetCell, fieldData, z, TargetColor);
        DrawCellCross(targetCell, fieldData, z, fieldData.CellSize * 0.4f, TargetColor);
    }

    // Primitives.

    private static void DrawDirectionArrow(
        int2 cell,
        float2 direction,
        in FlowFieldData fieldData,
        in AiDemoFlowFieldDebugSettings settings,
        Color color)
    {
        float2 center = FlowFieldUtility.FlowFieldGridToWorld(cell, fieldData.StartCoordinate, fieldData.CellSize);
        float2 dir = math.normalizesafe(direction);
        float length = fieldData.CellSize * math.max(0.05f, settings.arrowScale);

        float2 from2 = center - dir * length * 0.4f;
        float2 to2 = center + dir * length * 0.4f;

        Vector3 from = new Vector3(from2.x, from2.y, settings.renderZ);
        Vector3 to = new Vector3(to2.x, to2.y, settings.renderZ);

        AiDemoLineRenderer.AddLine(from, to, color);

        // Arrow fins.
        float2 side = new float2(-dir.y, dir.x);
        float2 back = to2 - dir * length * 0.25f;
        float2 left = back + side * length * 0.18f;
        float2 right = back - side * length * 0.18f;

        AiDemoLineRenderer.AddLine(to, new Vector3(left.x, left.y, settings.renderZ), color);
        AiDemoLineRenderer.AddLine(to, new Vector3(right.x, right.y, settings.renderZ), color);
    }

    private static void DrawCellOutline(int2 cell, in FlowFieldData fieldData, float z, Color color)
    {
        float2 min = fieldData.StartCoordinate + (float2)cell * fieldData.CellSize;
        float2 max = min + new float2(fieldData.CellSize, fieldData.CellSize);

        Vector3 a = new Vector3(min.x, min.y, z);
        Vector3 b = new Vector3(max.x, min.y, z);
        Vector3 c = new Vector3(max.x, max.y, z);
        Vector3 d = new Vector3(min.x, max.y, z);

        AiDemoLineRenderer.AddLine(a, b, color);
        AiDemoLineRenderer.AddLine(b, c, color);
        AiDemoLineRenderer.AddLine(c, d, color);
        AiDemoLineRenderer.AddLine(d, a, color);
    }

    private static void DrawCellCross(int2 cell, in FlowFieldData fieldData, float z, float size, Color color)
    {
        float2 center = FlowFieldUtility.FlowFieldGridToWorld(cell, fieldData.StartCoordinate, fieldData.CellSize);
        AiDemoLineRenderer.AddLine(
            new Vector3(center.x - size, center.y - size, z),
            new Vector3(center.x + size, center.y + size, z),
            color);
        AiDemoLineRenderer.AddLine(
            new Vector3(center.x - size, center.y + size, z),
            new Vector3(center.x + size, center.y - size, z),
            color);
    }
}