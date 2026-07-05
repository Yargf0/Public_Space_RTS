#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// debug drawing for raw obstacles and path layers
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class ObstacleDebugDrawSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<ObstacleDebugSettings>();
        RequireForUpdate<FlowFieldData>();
    }

    protected override void OnUpdate()
    {
        ObstacleDebugSettings debug = SystemAPI.GetSingleton<ObstacleDebugSettings>();
        if (!debug.Enabled)
            return;

        BufferLookup<GridCell> gridCellLookup = GetBufferLookup<GridCell>(isReadOnly: true);
        BufferLookup<PathGridCellSmall> smallLookup = GetBufferLookup<PathGridCellSmall>(isReadOnly: true);
        BufferLookup<PathGridCellMedium> mediumLookup = GetBufferLookup<PathGridCellMedium>(isReadOnly: true);
        BufferLookup<PathGridCellLarge> largeLookup = GetBufferLookup<PathGridCellLarge>(isReadOnly: true);

        Entity flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();
        if (!gridCellLookup.HasBuffer(flowFieldEntity))
            return;

        DynamicBuffer<GridCell> gridCells = gridCellLookup[flowFieldEntity];
        if (gridCells.Length == 0)
            return;

        FlowFieldData fieldData = SystemAPI.GetSingleton<FlowFieldData>();

        if (debug.DrawGridBorder)
            DrawGridBorder(fieldData, debug.DebugZ);

        if (debug.DrawPathLayer)
        {
            switch (debug.DebugLayer)
            {
                case PathfindingSizeClass.Small:
                    if (smallLookup.HasBuffer(flowFieldEntity)) DrawPathGridCells(smallLookup[flowFieldEntity], fieldData, debug);
                    break;
                case PathfindingSizeClass.Large:
                    if (largeLookup.HasBuffer(flowFieldEntity)) DrawPathGridCells(largeLookup[flowFieldEntity], fieldData, debug);
                    break;
                default:
                    if (mediumLookup.HasBuffer(flowFieldEntity)) DrawPathGridCells(mediumLookup[flowFieldEntity], fieldData, debug);
                    break;
            }
        }
        else if (debug.DrawGrid)
        {
            DrawGridCells(gridCells, fieldData, debug);
        }

        if (debug.DrawRawRepulsionVectors || debug.DrawDangerIndicators)
            DrawShipDebug(gridCells, fieldData, debug);
    }

    private static void DrawGridBorder(in FlowFieldData fieldData, float z)
    {
        float2 start = fieldData.StartCoordinate;
        float2 end = fieldData.StartCoordinate + (float2)fieldData.GridSize * fieldData.CellSize;

        Vector3 a = new Vector3(start.x, start.y, z);
        Vector3 b = new Vector3(end.x, start.y, z);
        Vector3 c = new Vector3(end.x, end.y, z);
        Vector3 d = new Vector3(start.x, end.y, z);

        Debug.DrawLine(a, b, Color.yellow);
        Debug.DrawLine(b, c, Color.yellow);
        Debug.DrawLine(c, d, Color.yellow);
        Debug.DrawLine(d, a, Color.yellow);
    }

    private static void DrawGridCells(DynamicBuffer<GridCell> gridCells, in FlowFieldData fieldData, in ObstacleDebugSettings debug)
    {
        int maxCells = math.max(1, debug.MaxCellsPerFrame);
        int drawn = 0;

        for (int y = 0; y < fieldData.GridSize.y; y++)
        {
            for (int x = 0; x < fieldData.GridSize.x; x++)
            {
                int index = x + y * fieldData.GridSize.x;
                if (index < 0 || index >= gridCells.Length)
                    continue;

                GridCell cell = gridCells[index];
                if (cell.Walkable && cell.Cost <= 1)
                    continue;

                Color color = cell.Walkable ? new Color(1f, 0.75f, 0f, 1f) : Color.red;
                DrawCellOutline(new int2(x, y), fieldData, debug.DebugZ, color);

                drawn++;
                if (drawn >= maxCells)
                    return;
            }
        }
    }

    private static void DrawPathGridCells(DynamicBuffer<PathGridCellSmall> gridCells, in FlowFieldData fieldData, in ObstacleDebugSettings debug)
    {
        int maxCells = math.max(1, debug.MaxCellsPerFrame);
        int drawn = 0;
        for (int y = 0; y < fieldData.GridSize.y; y++)
        for (int x = 0; x < fieldData.GridSize.x; x++)
        {
            int index = x + y * fieldData.GridSize.x;
            if (index < 0 || index >= gridCells.Length) continue;
            PathGridCellSmall cell = gridCells[index];
            if (cell.Walkable && cell.Cost <= 1) continue;
            DrawCellOutline(new int2(x, y), fieldData, debug.DebugZ, cell.Walkable ? new Color(1f, 0.75f, 0f, 1f) : Color.red);
            drawn++;
            if (drawn >= maxCells) return;
        }
    }

    private static void DrawPathGridCells(DynamicBuffer<PathGridCellMedium> gridCells, in FlowFieldData fieldData, in ObstacleDebugSettings debug)
    {
        int maxCells = math.max(1, debug.MaxCellsPerFrame);
        int drawn = 0;
        for (int y = 0; y < fieldData.GridSize.y; y++)
        for (int x = 0; x < fieldData.GridSize.x; x++)
        {
            int index = x + y * fieldData.GridSize.x;
            if (index < 0 || index >= gridCells.Length) continue;
            PathGridCellMedium cell = gridCells[index];
            if (cell.Walkable && cell.Cost <= 1) continue;
            DrawCellOutline(new int2(x, y), fieldData, debug.DebugZ, cell.Walkable ? new Color(1f, 0.75f, 0f, 1f) : Color.red);
            drawn++;
            if (drawn >= maxCells) return;
        }
    }

    private static void DrawPathGridCells(DynamicBuffer<PathGridCellLarge> gridCells, in FlowFieldData fieldData, in ObstacleDebugSettings debug)
    {
        int maxCells = math.max(1, debug.MaxCellsPerFrame);
        int drawn = 0;
        for (int y = 0; y < fieldData.GridSize.y; y++)
        for (int x = 0; x < fieldData.GridSize.x; x++)
        {
            int index = x + y * fieldData.GridSize.x;
            if (index < 0 || index >= gridCells.Length) continue;
            PathGridCellLarge cell = gridCells[index];
            if (cell.Walkable && cell.Cost <= 1) continue;
            DrawCellOutline(new int2(x, y), fieldData, debug.DebugZ, cell.Walkable ? new Color(1f, 0.75f, 0f, 1f) : Color.red);
            drawn++;
            if (drawn >= maxCells) return;
        }
    }

    private void DrawShipDebug(DynamicBuffer<GridCell> gridCells, in FlowFieldData fieldData, in ObstacleDebugSettings debug)
    {
        ObstacleAvoidanceSettings settings = SystemAPI.HasSingleton<ObstacleAvoidanceSettings>()
            ? SystemAPI.GetSingleton<ObstacleAvoidanceSettings>()
            : ObstacleAvoidanceSettings.CreateDefault();

        foreach ((RefRO<LocalTransform> transform, RefRO<UnitCollisionRadius> collisionRadius)
            in SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitCollisionRadius>>())
        {
            float2 pos = transform.ValueRO.Position.xy;
            float2 halfExtents = collisionRadius.ValueRO.collisionRadius;
            bool inDanger = ObstacleAvoidanceUtility.IsInDangerZone(pos, halfExtents, gridCells, fieldData, settings);

            if (debug.DrawRawRepulsionVectors)
            {
                float2 repulsion = ObstacleAvoidanceUtility.ComputeRawRepulsion(pos, halfExtents, gridCells, fieldData, settings);
                if (math.lengthsq(repulsion) > 0.0001f)
                {
                    Vector3 from = new Vector3(pos.x, pos.y, debug.DebugZ);
                    float2 to2 = pos + repulsion * debug.VectorScale;
                    Vector3 to = new Vector3(to2.x, to2.y, debug.DebugZ);
                    Debug.DrawLine(from, to, inDanger ? Color.red : new Color(1f, 0.55f, 0f, 1f));
                }
            }

            if (debug.DrawDangerIndicators && inDanger)
                DrawCross(pos, debug.DebugZ, math.max(1f, math.length(halfExtents) * 0.25f), Color.red);
        }
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

    private static void DrawCross(float2 pos, float z, float size, Color color)
    {
        Vector3 a = new Vector3(pos.x - size, pos.y - size, z);
        Vector3 b = new Vector3(pos.x + size, pos.y + size, z);
        Vector3 c = new Vector3(pos.x - size, pos.y + size, z);
        Vector3 d = new Vector3(pos.x + size, pos.y - size, z);

        Debug.DrawLine(a, b, color);
        Debug.DrawLine(c, d, color);
    }
}
#endif
