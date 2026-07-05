using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// push away from obstacles in GridCell buffer. ship size clearance is in path layers
[BurstCompile]
public static class ObstacleAvoidanceUtility
{
    public enum AvoidanceMode : byte
    {
        Disabled,
        DangerOnly,
        FlowFieldSafety,
        Preventive,
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AvoidanceMode SelectMode(ShipState state, float2 behaviorVelocity)
    {
        bool hasBehaviorVelocity = math.lengthsq(behaviorVelocity) > 0.01f;
        if (!hasBehaviorVelocity)
            return AvoidanceMode.DangerOnly;

        if (state == ShipState.MovingToTarget || state == ShipState.ReturnToGroup)
            return AvoidanceMode.FlowFieldSafety;

        return AvoidanceMode.Preventive;
    }

    public static float2 ComputeAvoidance(
        float2 shipPos,
        float2 shipHalfExtents,
        AvoidanceMode mode,
        in DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        in ObstacleAvoidanceSettings settings)
    {
        if (mode == AvoidanceMode.Disabled)
            return float2.zero;

        bool inHardDanger = IsInDangerZone(shipPos, shipHalfExtents, gridCells, fieldData, settings);
        return ComputeAvoidanceKnownDanger(shipPos, shipHalfExtents, mode, inHardDanger, gridCells, fieldData, settings);
    }

    public static float2 ComputeAvoidanceKnownDanger(
        float2 shipPos,
        float2 shipHalfExtents,
        AvoidanceMode mode,
        bool inHardDanger,
        in DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        in ObstacleAvoidanceSettings settings)
    {
        if (mode == AvoidanceMode.Disabled)
            return float2.zero;

        if (mode == AvoidanceMode.DangerOnly && !inHardDanger)
            return float2.zero;

        float2 extents = math.abs(shipHalfExtents);
        int extraCellsX = (int)math.ceil(extents.x / math.max(0.01f, fieldData.CellSize));
        int extraCellsY = (int)math.ceil(extents.y / math.max(0.01f, fieldData.CellSize));
        int effectiveRadius = math.max(1, settings.SampleRadius + math.max(extraCellsX, extraCellsY));

        float2 repulsion = SampleRepulsion(shipPos, gridCells, fieldData, effectiveRadius, settings);

        if (mode == AvoidanceMode.FlowFieldSafety && !inHardDanger)
            repulsion *= settings.FlowFieldSafetyMultiplier;

        return repulsion;
    }

    public static float2 ComputeRawRepulsion(
        float2 shipPos,
        float2 shipHalfExtents,
        in DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        in ObstacleAvoidanceSettings settings)
    {
        float2 extents = math.abs(shipHalfExtents);
        int extraCellsX = (int)math.ceil(extents.x / math.max(0.01f, fieldData.CellSize));
        int extraCellsY = (int)math.ceil(extents.y / math.max(0.01f, fieldData.CellSize));
        int effectiveRadius = math.max(1, settings.SampleRadius + math.max(extraCellsX, extraCellsY));

        return SampleRepulsion(shipPos, gridCells, fieldData, effectiveRadius, settings);
    }

    public static bool IsInDangerZone(
        float2 shipPos,
        float2 shipHalfExtents,
        in DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        in ObstacleAvoidanceSettings settings)
    {
        if (CheckHardBlockedPoint(shipPos, gridCells, fieldData))
            return true;

        float2 ext = math.abs(shipHalfExtents);
        if (ext.x <= 0.01f && ext.y <= 0.01f)
            return false;

        if (CheckHardBlockedPoint(shipPos + new float2( ext.x, 0f), gridCells, fieldData)) return true;
        if (CheckHardBlockedPoint(shipPos + new float2(-ext.x, 0f), gridCells, fieldData)) return true;
        if (CheckHardBlockedPoint(shipPos + new float2(0f,  ext.y), gridCells, fieldData)) return true;
        if (CheckHardBlockedPoint(shipPos + new float2(0f, -ext.y), gridCells, fieldData)) return true;

        if (CheckHardBlockedPoint(shipPos + new float2( ext.x,  ext.y), gridCells, fieldData)) return true;
        if (CheckHardBlockedPoint(shipPos + new float2( ext.x, -ext.y), gridCells, fieldData)) return true;
        if (CheckHardBlockedPoint(shipPos + new float2(-ext.x,  ext.y), gridCells, fieldData)) return true;
        if (CheckHardBlockedPoint(shipPos + new float2(-ext.x, -ext.y), gridCells, fieldData)) return true;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CheckHardBlockedPoint(
        float2 pos,
        in DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData)
    {
        int2 cell = FlowFieldUtility.FlowFieldGridPos(pos, fieldData.StartCoordinate, fieldData.CellSize);

        if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize))
            return false;

        int idx = FlowFieldUtility.PosToIndex(cell, fieldData.GridSize.x);
        if (idx < 0 || idx >= gridCells.Length)
            return false;

        return !gridCells[idx].Walkable;
    }

    private static float2 SampleRepulsion(
        float2 shipPos,
        in DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        int radiusCells,
        in ObstacleAvoidanceSettings settings)
    {
        float2 repulsion = float2.zero;
        int2 centerCell = FlowFieldUtility.FlowFieldGridPos(shipPos, fieldData.StartCoordinate, fieldData.CellSize);
        float falloffRadius = math.max(0.01f, radiusCells * fieldData.CellSize);

        for (int dy = -radiusCells; dy <= radiusCells; dy++)
        {
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                int2 cell = centerCell + new int2(dx, dy);
                if (!FlowFieldUtility.InBounds(cell, fieldData.GridSize))
                    continue;

                int idx = FlowFieldUtility.PosToIndex(cell, fieldData.GridSize.x);
                if (idx < 0 || idx >= gridCells.Length)
                    continue;

                GridCell gridCell = gridCells[idx];
                if (gridCell.Walkable && gridCell.Cost <= 1)
                    continue;

                float2 cellCenter = FlowFieldUtility.FlowFieldGridToWorld(cell, fieldData.StartCoordinate, fieldData.CellSize);
                float2 toShip = shipPos - cellCenter;
                float dist = math.length(toShip);

                if (dist < 0.01f)
                {
                    float2 windowCenter = FlowFieldUtility.FlowFieldGridToWorld(centerCell, fieldData.StartCoordinate, fieldData.CellSize);
                    toShip = math.normalizesafe(shipPos - windowCenter);
                    if (math.lengthsq(toShip) < 0.0001f)
                        toShip = new float2(1f, 0f);

                    dist = 0.01f;
                }

                float falloff = math.saturate(1f - dist / falloffRadius);
                float weight = gridCell.Walkable ? gridCell.Cost / 256f : 1f;
                repulsion += math.normalizesafe(toShip) * (weight * falloff * settings.Strength);
            }
        }

        float repulsionLength = math.length(repulsion);
        if (repulsionLength > settings.MaxRepulsion)
            repulsion *= settings.MaxRepulsion / repulsionLength;

        return repulsion;
    }
}
