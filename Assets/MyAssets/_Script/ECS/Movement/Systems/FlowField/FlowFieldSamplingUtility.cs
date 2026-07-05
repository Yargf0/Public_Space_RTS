using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

// sampling for ships on cell border so direction don't flip every frame
public static class FlowFieldSamplingUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 SampleStableFlowDirection(
        float2 position,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData)
    {
        if (fieldData.CellSize <= 0f ||
            fieldData.GridSize.x <= 0 ||
            fieldData.GridSize.y <= 0 ||
            groupCells.Length == 0 ||
            gridCells.Length == 0)
        {
            return float2.zero;
        }

        int2 centerCell = FlowFieldUtility.FlowFieldGridPos(
            position,
            fieldData.StartCoordinate,
            fieldData.CellSize);

        if (!FlowFieldUtility.InBounds(centerCell, fieldData.GridSize))
            return float2.zero;

        int gridWidth = fieldData.GridSize.x;
        int centerIndex = FlowFieldUtility.PosToIndex(centerCell, gridWidth);

        if (centerIndex < 0 || centerIndex >= groupCells.Length || centerIndex >= gridCells.Length)
            return float2.zero;

        if (!gridCells[centerIndex].Walkable)
            return float2.zero;

        int centerIntegration = groupCells[centerIndex].IntegrationValue;
        float2 weightedDir = float2.zero;
        float weightSum = 0f;

        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                int2 cell = centerCell + new int2(x, y);

                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize))
                    continue;

                int index = FlowFieldUtility.PosToIndex(cell, gridWidth);

                if (index < 0 || index >= groupCells.Length || index >= gridCells.Length)
                    continue;

                if (!gridCells[index].Walkable)
                    continue;

                GroupCell groupCell = groupCells[index];

                if (groupCell.IntegrationValue == int.MaxValue)
                    continue;

                if (centerIntegration != int.MaxValue && groupCell.IntegrationValue > centerIntegration)
                    continue;

                float2 dir = groupCell.Direction;

                if (math.lengthsq(dir) < 0.0001f)
                    continue;

                float2 cellCenter = FlowFieldUtility.FlowFieldGridToWorld(
                    cell,
                    fieldData.StartCoordinate,
                    fieldData.CellSize);

                float distanceSq = math.max(0.01f, math.distancesq(position, cellCenter));
                float weight = 1f / distanceSq;
                int cost = math.max(1, gridCells[index].Cost);
                weight /= cost;

                weightedDir += math.normalizesafe(dir) * weight;
                weightSum += weight;
            }
        }

        if (weightSum <= 0.0001f || math.lengthsq(weightedDir) < 0.0001f)
            return groupCells[centerIndex].Direction;

        return math.normalizesafe(weightedDir / weightSum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 SampleStableFlowDirection(
        float2 position,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<PathGridCellSmall> gridCells,
        in FlowFieldData fieldData)
    {
        if (fieldData.CellSize <= 0f ||
            fieldData.GridSize.x <= 0 ||
            fieldData.GridSize.y <= 0 ||
            groupCells.Length == 0 ||
            gridCells.Length == 0)
        {
            return float2.zero;
        }

        int2 centerCell = FlowFieldUtility.FlowFieldGridPos(
            position,
            fieldData.StartCoordinate,
            fieldData.CellSize);

        if (!FlowFieldUtility.InBounds(centerCell, fieldData.GridSize))
            return float2.zero;

        int gridWidth = fieldData.GridSize.x;
        int centerIndex = FlowFieldUtility.PosToIndex(centerCell, gridWidth);

        if (centerIndex < 0 || centerIndex >= groupCells.Length || centerIndex >= gridCells.Length)
            return float2.zero;

        if (!gridCells[centerIndex].Walkable)
            return float2.zero;

        int centerIntegration = groupCells[centerIndex].IntegrationValue;
        float2 weightedDir = float2.zero;
        float weightSum = 0f;

        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                int2 cell = centerCell + new int2(x, y);

                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize))
                    continue;

                int index = FlowFieldUtility.PosToIndex(cell, gridWidth);

                if (index < 0 || index >= groupCells.Length || index >= gridCells.Length)
                    continue;

                if (!gridCells[index].Walkable)
                    continue;

                GroupCell groupCell = groupCells[index];

                if (groupCell.IntegrationValue == int.MaxValue)
                    continue;

                if (centerIntegration != int.MaxValue && groupCell.IntegrationValue > centerIntegration)
                    continue;

                float2 dir = groupCell.Direction;

                if (math.lengthsq(dir) < 0.0001f)
                    continue;

                float2 cellCenter = FlowFieldUtility.FlowFieldGridToWorld(
                    cell,
                    fieldData.StartCoordinate,
                    fieldData.CellSize);

                float distanceSq = math.max(0.01f, math.distancesq(position, cellCenter));
                float weight = 1f / distanceSq;
                int cost = math.max(1, gridCells[index].Cost);
                weight /= cost;

                weightedDir += math.normalizesafe(dir) * weight;
                weightSum += weight;
            }
        }

        if (weightSum <= 0.0001f || math.lengthsq(weightedDir) < 0.0001f)
            return groupCells[centerIndex].Direction;

        return math.normalizesafe(weightedDir / weightSum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 SampleStableFlowDirection(
        float2 position,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<PathGridCellMedium> gridCells,
        in FlowFieldData fieldData)
    {
        if (fieldData.CellSize <= 0f ||
            fieldData.GridSize.x <= 0 ||
            fieldData.GridSize.y <= 0 ||
            groupCells.Length == 0 ||
            gridCells.Length == 0)
        {
            return float2.zero;
        }

        int2 centerCell = FlowFieldUtility.FlowFieldGridPos(
            position,
            fieldData.StartCoordinate,
            fieldData.CellSize);

        if (!FlowFieldUtility.InBounds(centerCell, fieldData.GridSize))
            return float2.zero;

        int gridWidth = fieldData.GridSize.x;
        int centerIndex = FlowFieldUtility.PosToIndex(centerCell, gridWidth);

        if (centerIndex < 0 || centerIndex >= groupCells.Length || centerIndex >= gridCells.Length)
            return float2.zero;

        if (!gridCells[centerIndex].Walkable)
            return float2.zero;

        int centerIntegration = groupCells[centerIndex].IntegrationValue;
        float2 weightedDir = float2.zero;
        float weightSum = 0f;

        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                int2 cell = centerCell + new int2(x, y);

                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize))
                    continue;

                int index = FlowFieldUtility.PosToIndex(cell, gridWidth);

                if (index < 0 || index >= groupCells.Length || index >= gridCells.Length)
                    continue;

                if (!gridCells[index].Walkable)
                    continue;

                GroupCell groupCell = groupCells[index];

                if (groupCell.IntegrationValue == int.MaxValue)
                    continue;

                if (centerIntegration != int.MaxValue && groupCell.IntegrationValue > centerIntegration)
                    continue;

                float2 dir = groupCell.Direction;

                if (math.lengthsq(dir) < 0.0001f)
                    continue;

                float2 cellCenter = FlowFieldUtility.FlowFieldGridToWorld(
                    cell,
                    fieldData.StartCoordinate,
                    fieldData.CellSize);

                float distanceSq = math.max(0.01f, math.distancesq(position, cellCenter));
                float weight = 1f / distanceSq;
                int cost = math.max(1, gridCells[index].Cost);
                weight /= cost;

                weightedDir += math.normalizesafe(dir) * weight;
                weightSum += weight;
            }
        }

        if (weightSum <= 0.0001f || math.lengthsq(weightedDir) < 0.0001f)
            return groupCells[centerIndex].Direction;

        return math.normalizesafe(weightedDir / weightSum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 SampleStableFlowDirection(
        float2 position,
        DynamicBuffer<GroupCell> groupCells,
        DynamicBuffer<PathGridCellLarge> gridCells,
        in FlowFieldData fieldData)
    {
        if (fieldData.CellSize <= 0f ||
            fieldData.GridSize.x <= 0 ||
            fieldData.GridSize.y <= 0 ||
            groupCells.Length == 0 ||
            gridCells.Length == 0)
        {
            return float2.zero;
        }

        int2 centerCell = FlowFieldUtility.FlowFieldGridPos(
            position,
            fieldData.StartCoordinate,
            fieldData.CellSize);

        if (!FlowFieldUtility.InBounds(centerCell, fieldData.GridSize))
            return float2.zero;

        int gridWidth = fieldData.GridSize.x;
        int centerIndex = FlowFieldUtility.PosToIndex(centerCell, gridWidth);

        if (centerIndex < 0 || centerIndex >= groupCells.Length || centerIndex >= gridCells.Length)
            return float2.zero;

        if (!gridCells[centerIndex].Walkable)
            return float2.zero;

        int centerIntegration = groupCells[centerIndex].IntegrationValue;
        float2 weightedDir = float2.zero;
        float weightSum = 0f;

        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                int2 cell = centerCell + new int2(x, y);

                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize))
                    continue;

                int index = FlowFieldUtility.PosToIndex(cell, gridWidth);

                if (index < 0 || index >= groupCells.Length || index >= gridCells.Length)
                    continue;

                if (!gridCells[index].Walkable)
                    continue;

                GroupCell groupCell = groupCells[index];

                if (groupCell.IntegrationValue == int.MaxValue)
                    continue;

                if (centerIntegration != int.MaxValue && groupCell.IntegrationValue > centerIntegration)
                    continue;

                float2 dir = groupCell.Direction;

                if (math.lengthsq(dir) < 0.0001f)
                    continue;

                float2 cellCenter = FlowFieldUtility.FlowFieldGridToWorld(
                    cell,
                    fieldData.StartCoordinate,
                    fieldData.CellSize);

                float distanceSq = math.max(0.01f, math.distancesq(position, cellCenter));
                float weight = 1f / distanceSq;
                int cost = math.max(1, gridCells[index].Cost);
                weight /= cost;

                weightedDir += math.normalizesafe(dir) * weight;
                weightSum += weight;
            }
        }

        if (weightSum <= 0.0001f || math.lengthsq(weightedDir) < 0.0001f)
            return groupCells[centerIndex].Direction;

        return math.normalizesafe(weightedDir / weightSum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 ClampSoftAvoidanceAgainstPrimaryMove(float2 primaryVelocity, float2 avoidanceVelocity)
    {
        if (math.lengthsq(primaryVelocity) < 0.0001f || math.lengthsq(avoidanceVelocity) < 0.0001f)
            return avoidanceVelocity;

        float2 primaryDir = math.normalizesafe(primaryVelocity);
        float backward = math.dot(avoidanceVelocity, primaryDir);

        if (backward < 0f)
            avoidanceVelocity -= primaryDir * backward;

        return avoidanceVelocity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 ClampPrimaryMoveAgainstHardDanger(float2 primaryVelocity, float2 avoidanceVelocity)
    {
        if (math.lengthsq(primaryVelocity) < 0.0001f || math.lengthsq(avoidanceVelocity) < 0.0001f)
            return primaryVelocity;

        float2 awayDir = math.normalizesafe(avoidanceVelocity);
        float intoObstacle = math.dot(primaryVelocity, awayDir);

        if (intoObstacle < 0f)
            primaryVelocity -= awayDir * intoObstacle;

        return primaryVelocity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 ProtectPrimaryVelocityFromCancellation(
        float2 primaryVelocity,
        float2 potentialVelocity,
        float2 avoidanceVelocity,
        bool inHardDanger)
    {
        if (inHardDanger)
        {
            if (math.lengthsq(potentialVelocity) < 0.01f && math.lengthsq(avoidanceVelocity) > 0.0001f)
                return math.normalizesafe(avoidanceVelocity);

            return potentialVelocity;
        }

        if (math.lengthsq(primaryVelocity) > 0.01f && math.lengthsq(potentialVelocity) < 0.01f)
            return primaryVelocity;

        return potentialVelocity;
    }
}
