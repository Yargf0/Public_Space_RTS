using Unity.Entities;
using Unity.Mathematics;

public static class FlowFieldDirectionUtility
{

    public static float2 GetFlowFieldDirection(
        float2 position,
        float2 targetPos,
        int2 gridPos,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<PathGridCellSmall> pathLayer,
        FlowFieldData flowFieldData)
    {
        if (position.x < startCoordinat.x ||
            position.y < startCoordinat.y ||
            position.x >= startCoordinat.x + cellSize * gridSize.x ||
            position.y >= startCoordinat.y + cellSize * gridSize.y)
        {
            int2 clampedGrid = new int2(math.clamp(gridPos.x, 0, gridSize.x - 1), math.clamp(gridPos.y, 0, gridSize.y - 1));
            float2 targetWorld = FlowFieldUtility.FlowFieldGridToWorld(clampedGrid, startCoordinat, cellSize);
            return math.normalizesafe(targetWorld - position);
        }

        int cellIndex = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        if (cellIndex < 0 || cellIndex >= groupCells.Length)
            return float2.zero;

        float2 stableDir = FlowFieldSamplingUtility.SampleStableFlowDirection(position, groupCells, pathLayer, flowFieldData);
        if (math.lengthsq(stableDir) > 0.0001f)
            return stableDir;

        return math.normalizesafe(groupCells[cellIndex].Direction);
    }

    public static float2 GetFlowFieldDirection(
        float2 position,
        float2 targetPos,
        int2 gridPos,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<PathGridCellMedium> pathLayer,
        FlowFieldData flowFieldData)
    {
        if (position.x < startCoordinat.x ||
            position.y < startCoordinat.y ||
            position.x >= startCoordinat.x + cellSize * gridSize.x ||
            position.y >= startCoordinat.y + cellSize * gridSize.y)
        {
            int2 clampedGrid = new int2(math.clamp(gridPos.x, 0, gridSize.x - 1), math.clamp(gridPos.y, 0, gridSize.y - 1));
            float2 targetWorld = FlowFieldUtility.FlowFieldGridToWorld(clampedGrid, startCoordinat, cellSize);
            return math.normalizesafe(targetWorld - position);
        }

        int cellIndex = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        if (cellIndex < 0 || cellIndex >= groupCells.Length)
            return float2.zero;

        float2 stableDir = FlowFieldSamplingUtility.SampleStableFlowDirection(position, groupCells, pathLayer, flowFieldData);
        if (math.lengthsq(stableDir) > 0.0001f)
            return stableDir;

        return math.normalizesafe(groupCells[cellIndex].Direction);
    }

    public static float2 GetFlowFieldDirection(
        float2 position,
        float2 targetPos,
        int2 gridPos,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<PathGridCellLarge> pathLayer,
        FlowFieldData flowFieldData)
    {
        if (position.x < startCoordinat.x ||
            position.y < startCoordinat.y ||
            position.x >= startCoordinat.x + cellSize * gridSize.x ||
            position.y >= startCoordinat.y + cellSize * gridSize.y)
        {
            int2 clampedGrid = new int2(math.clamp(gridPos.x, 0, gridSize.x - 1), math.clamp(gridPos.y, 0, gridSize.y - 1));
            float2 targetWorld = FlowFieldUtility.FlowFieldGridToWorld(clampedGrid, startCoordinat, cellSize);
            return math.normalizesafe(targetWorld - position);
        }

        int cellIndex = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        if (cellIndex < 0 || cellIndex >= groupCells.Length)
            return float2.zero;

        float2 stableDir = FlowFieldSamplingUtility.SampleStableFlowDirection(position, groupCells, pathLayer, flowFieldData);
        if (math.lengthsq(stableDir) > 0.0001f)
            return stableDir;

        return math.normalizesafe(groupCells[cellIndex].Direction);
    }
}
