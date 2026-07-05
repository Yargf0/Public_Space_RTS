using Unity.Entities;
using Unity.Mathematics;

public static class PathLayerTargetUtility
{

    public static bool IsPathWalkable(int2 gridPos, DynamicBuffer<PathGridCellSmall> pathLayer, int2 gridSize)
    {
        if (!FlowFieldUtility.InBounds(gridPos, gridSize))
            return false;

        int index = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        return index >= 0 && index < pathLayer.Length && pathLayer[index].Walkable;
    }

    public static bool TryGetSoftPathLayerEscape(
        float2 position,
        int2 gridPos,
        DynamicBuffer<PathGridCellSmall> pathLayer,
        DynamicBuffer<GroupCell> groupCells,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        out float2 escapeDir,
        out int2 escapeGridPos)
    {
        escapeDir = float2.zero;
        escapeGridPos = gridPos;
        const int MaxSoftEscapeSearchRadius = 3;

        bool found = false;
        int2 bestGridPos = gridPos;
        float bestDistanceSq = float.MaxValue;

        for (int radius = 1; radius <= MaxSoftEscapeSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.abs(x) != radius && math.abs(y) != radius)
                        continue;

                    int2 candidateGridPos = gridPos + new int2(x, y);
                    if (!FlowFieldUtility.InBounds(candidateGridPos, gridSize))
                        continue;

                    int candidateIndex = FlowFieldUtility.PosToIndex(candidateGridPos, gridSize.x);
                    if (candidateIndex < 0 || candidateIndex >= pathLayer.Length || candidateIndex >= groupCells.Length)
                        continue;

                    if (!pathLayer[candidateIndex].Walkable || groupCells[candidateIndex].IntegrationValue == int.MaxValue)
                        continue;

                    float2 candidateWorldPos = FlowFieldUtility.FlowFieldGridToWorld(candidateGridPos, startCoordinat, cellSize);
                    float distanceSq = math.distancesq(position, candidateWorldPos);
                    if (!found || distanceSq < bestDistanceSq)
                    {
                        found = true;
                        bestGridPos = candidateGridPos;
                        bestDistanceSq = distanceSq;
                    }
                }
            }

            if (found)
                break;
        }

        if (!found)
            return false;

        escapeGridPos = bestGridPos;
        int bestIndex = FlowFieldUtility.PosToIndex(bestGridPos, gridSize.x);
        float2 bestDirection = groupCells[bestIndex].Direction;
        if (math.lengthsq(bestDirection) > 0.0001f)
            escapeDir = math.normalizesafe(bestDirection);
        else
        {
            float2 escapeWorldPos = FlowFieldUtility.FlowFieldGridToWorld(bestGridPos, startCoordinat, cellSize);
            escapeDir = math.normalizesafe(escapeWorldPos - position);
        }

        if (math.lengthsq(escapeDir) < 0.0001f)
            escapeDir = new float2(1f, 0f);

        return true;
    }

    public static float2 ClampTargetToPathLayer(
        float2 targetPos,
        DynamicBuffer<PathGridCellSmall> pathLayer,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize)
    {
        int2 targetGridPos = FlowFieldUtility.FlowFieldGridPos(targetPos, startCoordinat, cellSize);
        if (IsPathWalkable(targetGridPos, pathLayer, gridSize))
            return targetPos;

        const int MaxClampSearchRadius = 8;
        bool found = false;
        int2 bestGridPos = targetGridPos;
        int bestCost = int.MaxValue;
        int bestDistanceSq = int.MaxValue;

        for (int radius = 1; radius <= MaxClampSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.abs(x) != radius && math.abs(y) != radius)
                        continue;

                    int2 cell = targetGridPos + new int2(x, y);
                    if (!FlowFieldUtility.InBounds(cell, gridSize))
                        continue;

                    int index = FlowFieldUtility.PosToIndex(cell, gridSize.x);
                    if (index < 0 || index >= pathLayer.Length || !pathLayer[index].Walkable)
                        continue;

                    int cost = math.max(1, pathLayer[index].Cost);
                    int distanceSq = x * x + y * y;
                    if (!found || cost < bestCost || (cost == bestCost && distanceSq < bestDistanceSq))
                    {
                        found = true;
                        bestGridPos = cell;
                        bestCost = cost;
                        bestDistanceSq = distanceSq;
                    }
                }
            }

            if (found)
                break;
        }

        return found
            ? FlowFieldUtility.FlowFieldGridToWorld(bestGridPos, startCoordinat, cellSize)
            : FlowFieldUtility.FlowFieldGridToWorld(
                new int2(math.clamp(targetGridPos.x, 0, gridSize.x - 1), math.clamp(targetGridPos.y, 0, gridSize.y - 1)),
                startCoordinat,
                cellSize);
    }

    public static bool IsPathWalkable(int2 gridPos, DynamicBuffer<PathGridCellMedium> pathLayer, int2 gridSize)
    {
        if (!FlowFieldUtility.InBounds(gridPos, gridSize))
            return false;

        int index = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        return index >= 0 && index < pathLayer.Length && pathLayer[index].Walkable;
    }

    public static bool TryGetSoftPathLayerEscape(
        float2 position,
        int2 gridPos,
        DynamicBuffer<PathGridCellMedium> pathLayer,
        DynamicBuffer<GroupCell> groupCells,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        out float2 escapeDir,
        out int2 escapeGridPos)
    {
        escapeDir = float2.zero;
        escapeGridPos = gridPos;
        const int MaxSoftEscapeSearchRadius = 3;

        bool found = false;
        int2 bestGridPos = gridPos;
        float bestDistanceSq = float.MaxValue;

        for (int radius = 1; radius <= MaxSoftEscapeSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.abs(x) != radius && math.abs(y) != radius)
                        continue;

                    int2 candidateGridPos = gridPos + new int2(x, y);
                    if (!FlowFieldUtility.InBounds(candidateGridPos, gridSize))
                        continue;

                    int candidateIndex = FlowFieldUtility.PosToIndex(candidateGridPos, gridSize.x);
                    if (candidateIndex < 0 || candidateIndex >= pathLayer.Length || candidateIndex >= groupCells.Length)
                        continue;

                    if (!pathLayer[candidateIndex].Walkable || groupCells[candidateIndex].IntegrationValue == int.MaxValue)
                        continue;

                    float2 candidateWorldPos = FlowFieldUtility.FlowFieldGridToWorld(candidateGridPos, startCoordinat, cellSize);
                    float distanceSq = math.distancesq(position, candidateWorldPos);
                    if (!found || distanceSq < bestDistanceSq)
                    {
                        found = true;
                        bestGridPos = candidateGridPos;
                        bestDistanceSq = distanceSq;
                    }
                }
            }

            if (found)
                break;
        }

        if (!found)
            return false;

        escapeGridPos = bestGridPos;
        int bestIndex = FlowFieldUtility.PosToIndex(bestGridPos, gridSize.x);
        float2 bestDirection = groupCells[bestIndex].Direction;
        if (math.lengthsq(bestDirection) > 0.0001f)
            escapeDir = math.normalizesafe(bestDirection);
        else
        {
            float2 escapeWorldPos = FlowFieldUtility.FlowFieldGridToWorld(bestGridPos, startCoordinat, cellSize);
            escapeDir = math.normalizesafe(escapeWorldPos - position);
        }

        if (math.lengthsq(escapeDir) < 0.0001f)
            escapeDir = new float2(1f, 0f);

        return true;
    }

    public static float2 ClampTargetToPathLayer(
        float2 targetPos,
        DynamicBuffer<PathGridCellMedium> pathLayer,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize)
    {
        int2 targetGridPos = FlowFieldUtility.FlowFieldGridPos(targetPos, startCoordinat, cellSize);
        if (IsPathWalkable(targetGridPos, pathLayer, gridSize))
            return targetPos;

        const int MaxClampSearchRadius = 8;
        bool found = false;
        int2 bestGridPos = targetGridPos;
        int bestCost = int.MaxValue;
        int bestDistanceSq = int.MaxValue;

        for (int radius = 1; radius <= MaxClampSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.abs(x) != radius && math.abs(y) != radius)
                        continue;

                    int2 cell = targetGridPos + new int2(x, y);
                    if (!FlowFieldUtility.InBounds(cell, gridSize))
                        continue;

                    int index = FlowFieldUtility.PosToIndex(cell, gridSize.x);
                    if (index < 0 || index >= pathLayer.Length || !pathLayer[index].Walkable)
                        continue;

                    int cost = math.max(1, pathLayer[index].Cost);
                    int distanceSq = x * x + y * y;
                    if (!found || cost < bestCost || (cost == bestCost && distanceSq < bestDistanceSq))
                    {
                        found = true;
                        bestGridPos = cell;
                        bestCost = cost;
                        bestDistanceSq = distanceSq;
                    }
                }
            }

            if (found)
                break;
        }

        return found
            ? FlowFieldUtility.FlowFieldGridToWorld(bestGridPos, startCoordinat, cellSize)
            : FlowFieldUtility.FlowFieldGridToWorld(
                new int2(math.clamp(targetGridPos.x, 0, gridSize.x - 1), math.clamp(targetGridPos.y, 0, gridSize.y - 1)),
                startCoordinat,
                cellSize);
    }

    public static bool IsPathWalkable(int2 gridPos, DynamicBuffer<PathGridCellLarge> pathLayer, int2 gridSize)
    {
        if (!FlowFieldUtility.InBounds(gridPos, gridSize))
            return false;

        int index = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        return index >= 0 && index < pathLayer.Length && pathLayer[index].Walkable;
    }

    public static bool TryGetSoftPathLayerEscape(
        float2 position,
        int2 gridPos,
        DynamicBuffer<PathGridCellLarge> pathLayer,
        DynamicBuffer<GroupCell> groupCells,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        out float2 escapeDir,
        out int2 escapeGridPos)
    {
        escapeDir = float2.zero;
        escapeGridPos = gridPos;
        const int MaxSoftEscapeSearchRadius = 3;

        bool found = false;
        int2 bestGridPos = gridPos;
        float bestDistanceSq = float.MaxValue;

        for (int radius = 1; radius <= MaxSoftEscapeSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.abs(x) != radius && math.abs(y) != radius)
                        continue;

                    int2 candidateGridPos = gridPos + new int2(x, y);
                    if (!FlowFieldUtility.InBounds(candidateGridPos, gridSize))
                        continue;

                    int candidateIndex = FlowFieldUtility.PosToIndex(candidateGridPos, gridSize.x);
                    if (candidateIndex < 0 || candidateIndex >= pathLayer.Length || candidateIndex >= groupCells.Length)
                        continue;

                    if (!pathLayer[candidateIndex].Walkable || groupCells[candidateIndex].IntegrationValue == int.MaxValue)
                        continue;

                    float2 candidateWorldPos = FlowFieldUtility.FlowFieldGridToWorld(candidateGridPos, startCoordinat, cellSize);
                    float distanceSq = math.distancesq(position, candidateWorldPos);
                    if (!found || distanceSq < bestDistanceSq)
                    {
                        found = true;
                        bestGridPos = candidateGridPos;
                        bestDistanceSq = distanceSq;
                    }
                }
            }

            if (found)
                break;
        }

        if (!found)
            return false;

        escapeGridPos = bestGridPos;
        int bestIndex = FlowFieldUtility.PosToIndex(bestGridPos, gridSize.x);
        float2 bestDirection = groupCells[bestIndex].Direction;
        if (math.lengthsq(bestDirection) > 0.0001f)
            escapeDir = math.normalizesafe(bestDirection);
        else
        {
            float2 escapeWorldPos = FlowFieldUtility.FlowFieldGridToWorld(bestGridPos, startCoordinat, cellSize);
            escapeDir = math.normalizesafe(escapeWorldPos - position);
        }

        if (math.lengthsq(escapeDir) < 0.0001f)
            escapeDir = new float2(1f, 0f);

        return true;
    }

    public static float2 ClampTargetToPathLayer(
        float2 targetPos,
        DynamicBuffer<PathGridCellLarge> pathLayer,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize)
    {
        int2 targetGridPos = FlowFieldUtility.FlowFieldGridPos(targetPos, startCoordinat, cellSize);
        if (IsPathWalkable(targetGridPos, pathLayer, gridSize))
            return targetPos;

        const int MaxClampSearchRadius = 8;
        bool found = false;
        int2 bestGridPos = targetGridPos;
        int bestCost = int.MaxValue;
        int bestDistanceSq = int.MaxValue;

        for (int radius = 1; radius <= MaxClampSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.abs(x) != radius && math.abs(y) != radius)
                        continue;

                    int2 cell = targetGridPos + new int2(x, y);
                    if (!FlowFieldUtility.InBounds(cell, gridSize))
                        continue;

                    int index = FlowFieldUtility.PosToIndex(cell, gridSize.x);
                    if (index < 0 || index >= pathLayer.Length || !pathLayer[index].Walkable)
                        continue;

                    int cost = math.max(1, pathLayer[index].Cost);
                    int distanceSq = x * x + y * y;
                    if (!found || cost < bestCost || (cost == bestCost && distanceSq < bestDistanceSq))
                    {
                        found = true;
                        bestGridPos = cell;
                        bestCost = cost;
                        bestDistanceSq = distanceSq;
                    }
                }
            }

            if (found)
                break;
        }

        return found
            ? FlowFieldUtility.FlowFieldGridToWorld(bestGridPos, startCoordinat, cellSize)
            : FlowFieldUtility.FlowFieldGridToWorld(
                new int2(math.clamp(targetGridPos.x, 0, gridSize.x - 1), math.clamp(targetGridPos.y, 0, gridSize.y - 1)),
                startCoordinat,
                cellSize);
    }
}
