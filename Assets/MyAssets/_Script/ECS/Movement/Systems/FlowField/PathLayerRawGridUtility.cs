using Unity.Entities;
using Unity.Mathematics;

public static class PathLayerRawGridUtility
{
    public static bool IsRawWalkable(int2 gridPos, DynamicBuffer<GridCell> gridCells, int2 gridSize, bool hasGrid)
    {
        if (!hasGrid || !FlowFieldUtility.InBounds(gridPos, gridSize))
            return false;

        int index = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        return index >= 0 && index < gridCells.Length && gridCells[index].Walkable;
    }

    public static bool TryGetBlockedCellEscapeDirection(
        float2 position,
        int2 gridPos,
        DynamicBuffer<GridCell> gridCells,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        bool hasGrid,
        out float2 escapeDir,
        out int2 escapeGridPos)
    {
        escapeDir = float2.zero;
        escapeGridPos = gridPos;

        if (!hasGrid || !FlowFieldUtility.InBounds(gridPos, gridSize))
            return false;

        int currentCellIndex = FlowFieldUtility.PosToIndex(gridPos, gridSize.x);
        if (currentCellIndex < 0 || currentCellIndex >= gridCells.Length || gridCells[currentCellIndex].Walkable)
            return false;

        const int MaxEscapeSearchRadius = 8;
        bool found = false;
        int2 bestGridPos = gridPos;
        float bestDistanceSq = float.MaxValue;

        for (int radius = 1; radius <= MaxEscapeSearchRadius; radius++)
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

                    int candidateCellIndex = FlowFieldUtility.PosToIndex(candidateGridPos, gridSize.x);
                    if (candidateCellIndex < 0 || candidateCellIndex >= gridCells.Length || !gridCells[candidateCellIndex].Walkable)
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
        float2 escapeWorldPos = FlowFieldUtility.FlowFieldGridToWorld(bestGridPos, startCoordinat, cellSize);
        escapeDir = math.normalizesafe(escapeWorldPos - position);
        if (math.lengthsq(escapeDir) < 0.0001f)
            escapeDir = new float2(1f, 0f);

        return true;
    }
}
