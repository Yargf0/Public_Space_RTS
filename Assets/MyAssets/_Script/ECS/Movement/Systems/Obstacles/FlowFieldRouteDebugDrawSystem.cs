#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// debug FlowField route visualizer
// shows which cells block Large/Medium sizes
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class FlowFieldRouteDebugDrawSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<ObstacleDebugSettings>();
        RequireForUpdate<FlowFieldData>();
    }

    protected override void OnUpdate()
    {
        ObstacleDebugSettings debug = SystemAPI.GetSingleton<ObstacleDebugSettings>();
        if (!debug.Enabled || !debug.DrawFlowFieldRoutes)
            return;

        Entity flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();
        FlowFieldData fieldData = SystemAPI.GetSingleton<FlowFieldData>();

        BufferLookup<GroupCell> groupCellLookup = GetBufferLookup<GroupCell>(isReadOnly: true);
        BufferLookup<PathGridCellSmall> smallLookup = GetBufferLookup<PathGridCellSmall>(isReadOnly: true);
        BufferLookup<PathGridCellMedium> mediumLookup = GetBufferLookup<PathGridCellMedium>(isReadOnly: true);
        BufferLookup<PathGridCellLarge> largeLookup = GetBufferLookup<PathGridCellLarge>(isReadOnly: true);
        ComponentLookup<GroupGridCell> groupGridLookup = GetComponentLookup<GroupGridCell>(isReadOnly: true);

        DrawGroupFields(fieldData, flowFieldEntity, groupCellLookup, smallLookup, mediumLookup, largeLookup, debug);

        if (debug.DrawShipFlowCells)
            DrawShipFlowCells(fieldData, flowFieldEntity, groupCellLookup, groupGridLookup, smallLookup, mediumLookup, largeLookup, debug);
    }

    private void DrawGroupFields(
        in FlowFieldData fieldData,
        Entity flowFieldEntity,
        BufferLookup<GroupCell> groupCellLookup,
        BufferLookup<PathGridCellSmall> smallLookup,
        BufferLookup<PathGridCellMedium> mediumLookup,
        BufferLookup<PathGridCellLarge> largeLookup,
        in ObstacleDebugSettings debug)
    {
        int groupsDrawn = 0;

        foreach ((RefRO<GroupGridCell> groupGridCell, Entity groupEntity)
                 in SystemAPI.Query<RefRO<GroupGridCell>>().WithEntityAccess())
        {
            if (!groupCellLookup.HasBuffer(groupEntity))
                continue;

            DynamicBuffer<GroupCell> groupCells = groupCellLookup[groupEntity];
            if (groupCells.Length == 0)
                continue;

            DrawSingleGroupField(
                groupGridCell.ValueRO,
                groupCells,
                fieldData,
                flowFieldEntity,
                smallLookup,
                mediumLookup,
                largeLookup,
                debug);

            groupsDrawn++;
            if (debug.DrawOnlyFirstFlowFieldGroup && groupsDrawn >= 1)
                return;
        }
    }

    private static void DrawSingleGroupField(
        in GroupGridCell groupGridCell,
        DynamicBuffer<GroupCell> groupCells,
        in FlowFieldData fieldData,
        Entity flowFieldEntity,
        BufferLookup<PathGridCellSmall> smallLookup,
        BufferLookup<PathGridCellMedium> mediumLookup,
        BufferLookup<PathGridCellLarge> largeLookup,
        in ObstacleDebugSettings debug)
    {
        int maxCells = math.max(1, debug.MaxRouteCellsPerFrame);
        int drawnCells = 0;
        int drawnLabels = 0;

        if (debug.DrawFlowFieldTargets)
            DrawTargetCell(groupGridCell, fieldData, debug);

        int width = fieldData.GridSize.x;
        int height = fieldData.GridSize.y;
        int cellCount = groupCells.Length;

        for (int index = 0; index < cellCount; index++)
        {
            if (drawnCells >= maxCells)
                return;

            int2 cell = new int2(index % width, index / width);
            if (cell.y < 0 || cell.y >= height)
                continue;

            bool hasPathCell = TryGetPathLayerCell(
                index,
                groupGridCell.SizeClass,
                flowFieldEntity,
                smallLookup,
                mediumLookup,
                largeLookup,
                out bool pathWalkable,
                out int pathCost);

            bool pathBlocked = hasPathCell && !pathWalkable;
            bool pathSoft = hasPathCell && pathWalkable && pathCost > 1;

            if (pathBlocked)
            {
                // red cross = path layer blocks this cell
                DrawCellOutline(cell, fieldData, debug.DebugZ, Color.red);
                DrawCellCross(cell, fieldData, debug.DebugZ, fieldData.CellSize * 0.25f, Color.red);
                drawnCells++;
                continue;
            }

            GroupCell groupCell = groupCells[index];

            bool reachable = groupCell.IntegrationValue != int.MaxValue;
            bool hasDirection = math.lengthsq(groupCell.Direction) > 0.0001f;

            if (debug.DrawFlowFieldIntegration && reachable)
            {
                // green = wave reached cell, orange = walkable but extra cost
                DrawCellOutline(cell, fieldData, debug.DebugZ, pathSoft ? new Color(1f, 0.65f, 0f, 1f) : new Color(0f, 0.8f, 0.25f, 1f));
                drawnCells++;
            }

            if (debug.DrawFlowFieldDirections && reachable)
            {
                if (hasDirection)
                {
                    DrawDirectionArrow(cell, groupCell.Direction, fieldData, debug, pathSoft ? new Color(1f, 0.65f, 0f, 1f) : Color.green);
                    drawnCells++;
                }
                else if (groupCell.IntegrationValue == 0)
                {
                    // zero direction + IntegrationValue 0 usually = target cell
                    DrawCellCross(cell, fieldData, debug.DebugZ, fieldData.CellSize * 0.25f, Color.cyan);
                    drawnCells++;
                }
            }

#if UNITY_EDITOR
            if (debug.DrawFlowFieldIntegrationLabels && reachable &&
                drawnLabels < math.max(0, debug.MaxIntegrationLabelsPerFrame))
            {
                DrawIntegrationLabel(cell, groupCell.IntegrationValue, fieldData, debug.DebugZ);
                drawnLabels++;
            }
#endif
        }
    }

    private void DrawShipFlowCells(
        in FlowFieldData fieldData,
        Entity flowFieldEntity,
        BufferLookup<GroupCell> groupCellLookup,
        ComponentLookup<GroupGridCell> groupGridLookup,
        BufferLookup<PathGridCellSmall> smallLookup,
        BufferLookup<PathGridCellMedium> mediumLookup,
        BufferLookup<PathGridCellLarge> largeLookup,
        in ObstacleDebugSettings debug)
    {
        int drawnShips = 0;
        int maxShips = math.max(1, debug.MaxRouteShipsPerFrame);

        foreach ((RefRO<LocalTransform> transform, RefRO<UnitGroup> unitGroup)
                 in SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitGroup>>())
        {
            if (drawnShips >= maxShips)
                return;

            Entity groupEntity = unitGroup.ValueRO.GroupEntity;
            if (groupEntity == Entity.Null || !EntityManager.Exists(groupEntity))
                continue;

            if (!groupCellLookup.HasBuffer(groupEntity) || !groupGridLookup.HasComponent(groupEntity))
                continue;

            GroupGridCell groupGridCell = groupGridLookup[groupEntity];

            float2 position = transform.ValueRO.Position.xy;
            int2 shipCell = FlowFieldUtility.FlowFieldGridPos(position, fieldData.StartCoordinate, fieldData.CellSize);

            if (!FlowFieldUtility.InBounds(shipCell, fieldData.GridSize))
                continue;

            DynamicBuffer<GroupCell> groupCells = groupCellLookup[groupEntity];
            int index = FlowFieldUtility.PosToIndex(shipCell, fieldData.GridSize.x);
            if (index < 0 || index >= groupCells.Length)
                continue;

            bool hasPathCell = TryGetPathLayerCell(
                index,
                groupGridCell.SizeClass,
                flowFieldEntity,
                smallLookup,
                mediumLookup,
                largeLookup,
                out bool pathWalkable,
                out int pathCost);

            bool pathBlocked = hasPathCell && !pathWalkable;
            bool pathSoft = hasPathCell && pathWalkable && pathCost > 1;
            Color shipCellColor = pathBlocked ? Color.red : pathSoft ? new Color(1f, 0.65f, 0f, 1f) : Color.white;

            DrawCellOutline(shipCell, fieldData, debug.DebugZ, shipCellColor);
            if (pathBlocked)
                DrawCellCross(shipCell, fieldData, debug.DebugZ, fieldData.CellSize * 0.25f, Color.red);

            GroupCell groupCell = groupCells[index];
            float2 direction = groupCell.Direction;

            if (!pathBlocked && math.lengthsq(direction) > 0.0001f)
            {
                float2 dir = math.normalizesafe(direction);
                Vector3 from = new Vector3(position.x, position.y, debug.DebugZ);
                float2 to2 = position + dir * fieldData.CellSize * math.max(0.05f, debug.DirectionArrowScale);
                Vector3 to = new Vector3(to2.x, to2.y, debug.DebugZ);
                Debug.DrawLine(from, to, pathSoft ? new Color(1f, 0.65f, 0f, 1f) : Color.magenta);
            }
            else
            {
                DrawCross(position, debug.DebugZ, fieldData.CellSize * 0.2f, pathBlocked ? Color.red : Color.gray);
            }

            drawnShips++;
        }
    }

    private static bool TryGetPathLayerCell(
        int index,
        PathfindingSizeClass sizeClass,
        Entity flowFieldEntity,
        BufferLookup<PathGridCellSmall> smallLookup,
        BufferLookup<PathGridCellMedium> mediumLookup,
        BufferLookup<PathGridCellLarge> largeLookup,
        out bool walkable,
        out int cost)
    {
        walkable = true;
        cost = 1;

        switch (sizeClass)
        {
            case PathfindingSizeClass.Small:
                if (!smallLookup.HasBuffer(flowFieldEntity))
                    return false;
                DynamicBuffer<PathGridCellSmall> small = smallLookup[flowFieldEntity];
                if (index < 0 || index >= small.Length)
                    return false;
                walkable = small[index].Walkable;
                cost = small[index].Cost;
                return true;

            case PathfindingSizeClass.Large:
                if (!largeLookup.HasBuffer(flowFieldEntity))
                    return false;
                DynamicBuffer<PathGridCellLarge> large = largeLookup[flowFieldEntity];
                if (index < 0 || index >= large.Length)
                    return false;
                walkable = large[index].Walkable;
                cost = large[index].Cost;
                return true;

            case PathfindingSizeClass.Medium:
            default:
                if (!mediumLookup.HasBuffer(flowFieldEntity))
                    return false;
                DynamicBuffer<PathGridCellMedium> medium = mediumLookup[flowFieldEntity];
                if (index < 0 || index >= medium.Length)
                    return false;
                walkable = medium[index].Walkable;
                cost = medium[index].Cost;
                return true;
        }
    }

    private static void DrawTargetCell(in GroupGridCell groupGridCell, in FlowFieldData fieldData, in ObstacleDebugSettings debug)
    {
        int2 targetCell = FlowFieldUtility.FlowFieldGridPos(
            groupGridCell.TargetPosition,
            fieldData.StartCoordinate,
            fieldData.CellSize);

        if (!FlowFieldUtility.InBounds(targetCell, fieldData.GridSize))
            return;

        // cyan square = target cell after fallback to nearest walkable
        DrawCellOutline(targetCell, fieldData, debug.DebugZ, Color.cyan);
        DrawCellCross(targetCell, fieldData, debug.DebugZ, fieldData.CellSize * 0.35f, Color.cyan);

        Vector3 targetPos = new Vector3(groupGridCell.TargetPosition.x, groupGridCell.TargetPosition.y, debug.DebugZ);
        Debug.DrawLine(
            targetPos + new Vector3(-fieldData.CellSize * 0.25f, 0f, 0f),
            targetPos + new Vector3( fieldData.CellSize * 0.25f, 0f, 0f),
            Color.cyan);
        Debug.DrawLine(
            targetPos + new Vector3(0f, -fieldData.CellSize * 0.25f, 0f),
            targetPos + new Vector3(0f,  fieldData.CellSize * 0.25f, 0f),
            Color.cyan);
    }

    private static void DrawDirectionArrow(
        int2 cell,
        float2 direction,
        in FlowFieldData fieldData,
        in ObstacleDebugSettings debug,
        Color color)
    {
        float2 center = FlowFieldUtility.FlowFieldGridToWorld(cell, fieldData.StartCoordinate, fieldData.CellSize);
        float2 dir = math.normalizesafe(direction);
        float length = fieldData.CellSize * math.max(0.05f, debug.DirectionArrowScale);

        Vector3 from = new Vector3(center.x, center.y, debug.DebugZ);
        float2 end2 = center + dir * length;
        Vector3 to = new Vector3(end2.x, end2.y, debug.DebugZ);

        Debug.DrawLine(from, to, color);

        // small fins so direction is visible
        float2 side = new float2(-dir.y, dir.x);
        float2 back = end2 - dir * length * 0.25f;
        float2 left = back + side * length * 0.15f;
        float2 right = back - side * length * 0.15f;

        Debug.DrawLine(to, new Vector3(left.x, left.y, debug.DebugZ), color);
        Debug.DrawLine(to, new Vector3(right.x, right.y, debug.DebugZ), color);
    }

    private static void DrawCellOutline(int2 cell, in FlowFieldData fieldData, float z, Color color)
    {
        float2 min = fieldData.StartCoordinate + (float2)cell * fieldData.CellSize;
        float2 max = min + new float2(fieldData.CellSize, fieldData.CellSize);

        Vector3 a = new Vector3(min.x, min.y, z);
        Vector3 b = new Vector3(max.x, min.y, z);
        Vector3 c = new Vector3(max.x, max.y, z);
        Vector3 d = new Vector3(min.x, max.y, z);

        Debug.DrawLine(a, b, color);
        Debug.DrawLine(b, c, color);
        Debug.DrawLine(c, d, color);
        Debug.DrawLine(d, a, color);
    }

    private static void DrawCellCross(int2 cell, in FlowFieldData fieldData, float z, float size, Color color)
    {
        float2 center = FlowFieldUtility.FlowFieldGridToWorld(cell, fieldData.StartCoordinate, fieldData.CellSize);
        DrawCross(center, z, size, color);
    }

    private static void DrawCross(float2 pos, float z, float size, Color color)
    {
        Vector3 a = new Vector3(pos.x - size, pos.y - size, z);
        Vector3 b = new Vector3(pos.x + size, pos.y + size, z);
        Vector3 c = new Vector3(pos.x - size, pos.y + size, z);
        Vector3 d = new Vector3(pos.x + size, pos.y - size, z);

        Debug.DrawLine(a, b, color);
        Debug.DrawLine(c, d, color);
    }

#if UNITY_EDITOR
    private static void DrawIntegrationLabel(int2 cell, int value, in FlowFieldData fieldData, float z)
    {
        float2 center = FlowFieldUtility.FlowFieldGridToWorld(cell, fieldData.StartCoordinate, fieldData.CellSize);
        Vector3 labelPos = new Vector3(center.x, center.y, z);
        Handles.Label(labelPos, value.ToString());
    }
#endif
}
#endif
