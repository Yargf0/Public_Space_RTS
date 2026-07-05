using Unity.Entities;
using Unity.Mathematics;

public static class PathLayerMovementResolver
{

    public static PathLayerMoveResult Resolve(
        float2 position,
        float2 targetPos,
        int2 gridPos,
        int2 targetGridPos,
        in GroupGridCell gridData,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<GridCell> rawGridCells,
        DynamicBuffer<PathGridCellSmall> pathLayer,
        bool hasRawGrid,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        in FlowFieldData flowFieldData,
        float flowFieldWeight)
    {
        PathLayerMoveResult result = new PathLayerMoveResult
        {
            FlowVelocity = float2.zero,
            SetIdle = false,
        };

        if (PathLayerRawGridUtility.TryGetBlockedCellEscapeDirection(position, gridPos, rawGridCells, gridSize, startCoordinat, cellSize, hasRawGrid, out float2 escapeDir, out _))
        {
            result.FlowVelocity = escapeDir * flowFieldWeight;
            return result;
        }

        if (PathLayerRawGridUtility.IsRawWalkable(gridPos, rawGridCells, gridSize, hasRawGrid) && !PathLayerTargetUtility.IsPathWalkable(gridPos, pathLayer, gridSize))
        {
            if (PathLayerTargetUtility.TryGetSoftPathLayerEscape(position, gridPos, pathLayer, groupCells, gridSize, startCoordinat, cellSize, out float2 softDir, out _))
                result.FlowVelocity = softDir * flowFieldWeight;
            else
                result.FlowVelocity = math.normalizesafe(targetPos - position) * flowFieldWeight;
            return result;
        }

        if (gridPos.x == targetGridPos.x && gridPos.y == targetGridPos.y)
        {
            result.SetIdle = true;
            result.FlowVelocity = float2.zero;
            return result;
        }

        float finalApproachDistance = math.max(cellSize * 2f, math.distance(gridData.TargetPosition, targetPos) + cellSize);
        if (math.distancesq(position, gridData.TargetPosition) <= finalApproachDistance * finalApproachDistance)
        {
            float2 safeTarget = PathLayerTargetUtility.ClampTargetToPathLayer(targetPos, pathLayer, gridSize, startCoordinat, cellSize);

            int2 safeTargetGridPos = FlowFieldUtility.FlowFieldGridPos(safeTarget, startCoordinat, cellSize);
            if (!PathLayerTargetUtility.IsPathWalkable(safeTargetGridPos, pathLayer, gridSize))
            {
                result.SetIdle = true;
                result.FlowVelocity = float2.zero;
                return result;
            }

            if (math.distancesq(position, safeTarget) <= 1f)
            {
                result.SetIdle = true;
                result.FlowVelocity = float2.zero;
                return result;
            }

            result.FlowVelocity = math.normalizesafe(safeTarget - position) * flowFieldWeight;
            return result;
        }

        float2 flowDir = FlowFieldDirectionUtility.GetFlowFieldDirection(position, targetPos, gridPos, gridSize, startCoordinat, cellSize, groupCells, pathLayer, flowFieldData);
        result.FlowVelocity = flowDir * flowFieldWeight;
        return result;
    }

    public static PathLayerMoveResult Resolve(
        float2 position,
        float2 targetPos,
        int2 gridPos,
        int2 targetGridPos,
        in GroupGridCell gridData,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<GridCell> rawGridCells,
        DynamicBuffer<PathGridCellMedium> pathLayer,
        bool hasRawGrid,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        in FlowFieldData flowFieldData,
        float flowFieldWeight)
    {
        PathLayerMoveResult result = new PathLayerMoveResult
        {
            FlowVelocity = float2.zero,
            SetIdle = false,
        };

        if (PathLayerRawGridUtility.TryGetBlockedCellEscapeDirection(position, gridPos, rawGridCells, gridSize, startCoordinat, cellSize, hasRawGrid, out float2 escapeDir, out _))
        {
            result.FlowVelocity = escapeDir * flowFieldWeight;
            return result;
        }

        if (PathLayerRawGridUtility.IsRawWalkable(gridPos, rawGridCells, gridSize, hasRawGrid) && !PathLayerTargetUtility.IsPathWalkable(gridPos, pathLayer, gridSize))
        {
            if (PathLayerTargetUtility.TryGetSoftPathLayerEscape(position, gridPos, pathLayer, groupCells, gridSize, startCoordinat, cellSize, out float2 softDir, out _))
                result.FlowVelocity = softDir * flowFieldWeight;
            else
                result.FlowVelocity = math.normalizesafe(targetPos - position) * flowFieldWeight;
            return result;
        }

        if (gridPos.x == targetGridPos.x && gridPos.y == targetGridPos.y)
        {
            result.SetIdle = true;
            result.FlowVelocity = float2.zero;
            return result;
        }

        float finalApproachDistance = math.max(cellSize * 2f, math.distance(gridData.TargetPosition, targetPos) + cellSize);
        if (math.distancesq(position, gridData.TargetPosition) <= finalApproachDistance * finalApproachDistance)
        {
            float2 safeTarget = PathLayerTargetUtility.ClampTargetToPathLayer(targetPos, pathLayer, gridSize, startCoordinat, cellSize);

            int2 safeTargetGridPos = FlowFieldUtility.FlowFieldGridPos(safeTarget, startCoordinat, cellSize);
            if (!PathLayerTargetUtility.IsPathWalkable(safeTargetGridPos, pathLayer, gridSize))
            {
                result.SetIdle = true;
                result.FlowVelocity = float2.zero;
                return result;
            }

            if (math.distancesq(position, safeTarget) <= 1f)
            {
                result.SetIdle = true;
                result.FlowVelocity = float2.zero;
                return result;
            }

            result.FlowVelocity = math.normalizesafe(safeTarget - position) * flowFieldWeight;
            return result;
        }

        float2 flowDir = FlowFieldDirectionUtility.GetFlowFieldDirection(position, targetPos, gridPos, gridSize, startCoordinat, cellSize, groupCells, pathLayer, flowFieldData);
        result.FlowVelocity = flowDir * flowFieldWeight;
        return result;
    }

    public static PathLayerMoveResult Resolve(
        float2 position,
        float2 targetPos,
        int2 gridPos,
        int2 targetGridPos,
        in GroupGridCell gridData,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<GridCell> rawGridCells,
        DynamicBuffer<PathGridCellLarge> pathLayer,
        bool hasRawGrid,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        in FlowFieldData flowFieldData,
        float flowFieldWeight)
    {
        PathLayerMoveResult result = new PathLayerMoveResult
        {
            FlowVelocity = float2.zero,
            SetIdle = false,
        };

        if (PathLayerRawGridUtility.TryGetBlockedCellEscapeDirection(position, gridPos, rawGridCells, gridSize, startCoordinat, cellSize, hasRawGrid, out float2 escapeDir, out _))
        {
            result.FlowVelocity = escapeDir * flowFieldWeight;
            return result;
        }

        if (PathLayerRawGridUtility.IsRawWalkable(gridPos, rawGridCells, gridSize, hasRawGrid) && !PathLayerTargetUtility.IsPathWalkable(gridPos, pathLayer, gridSize))
        {
            if (PathLayerTargetUtility.TryGetSoftPathLayerEscape(position, gridPos, pathLayer, groupCells, gridSize, startCoordinat, cellSize, out float2 softDir, out _))
                result.FlowVelocity = softDir * flowFieldWeight;
            else
                result.FlowVelocity = math.normalizesafe(targetPos - position) * flowFieldWeight;
            return result;
        }

        if (gridPos.x == targetGridPos.x && gridPos.y == targetGridPos.y)
        {
            result.SetIdle = true;
            result.FlowVelocity = float2.zero;
            return result;
        }

        float finalApproachDistance = math.max(cellSize * 2f, math.distance(gridData.TargetPosition, targetPos) + cellSize);
        if (math.distancesq(position, gridData.TargetPosition) <= finalApproachDistance * finalApproachDistance)
        {
            float2 safeTarget = PathLayerTargetUtility.ClampTargetToPathLayer(targetPos, pathLayer, gridSize, startCoordinat, cellSize);

            int2 safeTargetGridPos = FlowFieldUtility.FlowFieldGridPos(safeTarget, startCoordinat, cellSize);
            if (!PathLayerTargetUtility.IsPathWalkable(safeTargetGridPos, pathLayer, gridSize))
            {
                result.SetIdle = true;
                result.FlowVelocity = float2.zero;
                return result;
            }

            if (math.distancesq(position, safeTarget) <= 1f)
            {
                result.SetIdle = true;
                result.FlowVelocity = float2.zero;
                return result;
            }

            result.FlowVelocity = math.normalizesafe(safeTarget - position) * flowFieldWeight;
            return result;
        }

        float2 flowDir = FlowFieldDirectionUtility.GetFlowFieldDirection(position, targetPos, gridPos, gridSize, startCoordinat, cellSize, groupCells, pathLayer, flowFieldData);
        result.FlowVelocity = flowDir * flowFieldWeight;
        return result;
    }
}
