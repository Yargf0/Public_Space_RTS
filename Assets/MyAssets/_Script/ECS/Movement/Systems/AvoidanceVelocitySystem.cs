using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(BoidSystem))]
[UpdateAfter(typeof(CombatVelocitySystem))]
[UpdateBefore(typeof(SetTotalVelocitySystem))]
public partial struct AvoidanceVelocitySystem : ISystem
{
    private BufferLookup<GridCell> gridCellLookup;

    // soft avoidance can skip frames, hard danger is every frame
    private const uint PreventiveStaggerBuckets = 3u;
    private const float VelocityWriteEpsilonSq = 0.0001f;

    private const float HardDangerAvoidanceWeight = 1.5f;

    private uint frameIndex;

    public void OnCreate(ref SystemState state)
    {
        gridCellLookup = state.GetBufferLookup<GridCell>(isReadOnly: true);
        frameIndex = 0u;
        state.RequireForUpdate<FlowFieldData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        gridCellLookup.Update(ref state);

        uint currentPreventiveBucket = frameIndex % PreventiveStaggerBuckets;
        frameIndex++;

        FlowFieldData config = SystemAPI.GetSingleton<FlowFieldData>();
        Entity flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();

        bool hasGrid = false;
        DynamicBuffer<GridCell> gridCells = default;
        if (gridCellLookup.HasBuffer(flowFieldEntity))
        {
            gridCells = gridCellLookup[flowFieldEntity];
            hasGrid = gridCells.Length > 0;
        }

        ObstacleAvoidanceSettings obstacleSettings = SystemAPI.HasSingleton<ObstacleAvoidanceSettings>()
            ? SystemAPI.GetSingleton<ObstacleAvoidanceSettings>()
            : ObstacleAvoidanceSettings.CreateDefault();

        foreach ((
            RefRO<ShipStateComponent> shipState,
            RefRO<LocalTransform> localTransform,
            RefRO<Velocity> velocity,
            RefRO<UnitCollisionRadius> collisionRadius,
            RefRW<MovementVelocityIntent> intent,
            Entity entity)
            in SystemAPI.Query<
                    RefRO<ShipStateComponent>,
                    RefRO<LocalTransform>,
                    RefRO<Velocity>,
                    RefRO<UnitCollisionRadius>,
                    RefRW<MovementVelocityIntent>>()
                .WithEntityAccess())
        {
            float2 position = localTransform.ValueRO.Position.xy;
            if (!math.isfinite(position.x) || !math.isfinite(position.y) || !hasGrid)
            {
                WriteAvoidance(intent, float2.zero, false);
                continue;
            }

            float2 primaryVelocity = intent.ValueRO.PathVelocity + intent.ValueRO.CombatVelocity;
            if (!math.isfinite(primaryVelocity.x) || !math.isfinite(primaryVelocity.y))
                primaryVelocity = float2.zero;

            if (CanSkipCalmIdleShip(shipState.ValueRO.currentState, velocity.ValueRO.velocity, primaryVelocity, velocity.ValueRO))
            {
                WriteAvoidance(intent, float2.zero, false);
                continue;
            }

            ObstacleAvoidanceUtility.AvoidanceMode avoidanceMode = ObstacleAvoidanceUtility.SelectMode(
                shipState.ValueRO.currentState,
                primaryVelocity);

            if (avoidanceMode == ObstacleAvoidanceUtility.AvoidanceMode.Disabled)
            {
                WriteAvoidance(intent, float2.zero, false);
                continue;
            }

            float2 shipHalfExtents = collisionRadius.ValueRO.collisionRadius;

            // hard danger every frame, ship in bad cell must escape now
            bool inHardDanger = ObstacleAvoidanceUtility.IsInDangerZone(
                position,
                shipHalfExtents,
                gridCells,
                config,
                obstacleSettings);

            if (!inHardDanger)
            {
                if (avoidanceMode == ObstacleAvoidanceUtility.AvoidanceMode.DangerOnly)
                {
                    WriteAvoidance(intent, float2.zero, false);
                    continue;
                }

                // soft sampling in buckets, old avoidance can stay 1-2 frames
                if (!ShouldRunPreventiveThisFrame(entity, currentPreventiveBucket))
                {
                    if (intent.ValueRO.InHardDanger != 0)
                        WriteAvoidance(intent, intent.ValueRO.AvoidanceVelocity, false);

                    continue;
                }
            }

            if (!TryComputeAvoidanceSinglePass(
                    position,
                    shipHalfExtents,
                    gridCells,
                    config,
                    obstacleSettings,
                    out float2 avoidanceVelocity))
            {
                WriteAvoidance(intent, float2.zero, inHardDanger);
                continue;
            }

            avoidanceVelocity /= math.max(0.01f, obstacleSettings.MaxRepulsion);

            if (inHardDanger)
            {
                avoidanceVelocity *= HardDangerAvoidanceWeight;
            }
            else if (avoidanceMode == ObstacleAvoidanceUtility.AvoidanceMode.FlowFieldSafety)
            {
                avoidanceVelocity *= obstacleSettings.FlowFieldSafetyMultiplier;
                avoidanceVelocity = FlowFieldSamplingUtility.ClampSoftAvoidanceAgainstPrimaryMove(
                    primaryVelocity,
                    avoidanceVelocity);
            }

            WriteAvoidance(intent, avoidanceVelocity, inHardDanger);
        }
    }

    private static bool CanSkipCalmIdleShip(ShipState state, float2 currentVelocity, float2 primaryVelocity, in Velocity velocity)
    {
        if (state != ShipState.Idle && state != ShipState.GuardPosition)
            return false;

        return math.lengthsq(currentVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(primaryVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(velocity.separationVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(velocity.alignmentVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(velocity.cohesionVelocity) <= VelocityWriteEpsilonSq;
    }

    private static bool TryComputeAvoidanceSinglePass(
        float2 shipPos,
        float2 shipHalfExtents,
        in DynamicBuffer<GridCell> gridCells,
        in FlowFieldData fieldData,
        in ObstacleAvoidanceSettings settings,
        out float2 repulsion)
    {
        repulsion = float2.zero;

        if (gridCells.Length == 0 || fieldData.GridSize.x <= 0 || fieldData.GridSize.y <= 0)
            return false;

        float cellSize = math.max(0.01f, fieldData.CellSize);
        float2 extents = math.abs(shipHalfExtents);
        int extraCellsX = (int)math.ceil(extents.x / cellSize);
        int extraCellsY = (int)math.ceil(extents.y / cellSize);
        int effectiveRadius = math.max(1, settings.SampleRadius + math.max(extraCellsX, extraCellsY));

        int2 centerCell = FlowFieldUtility.FlowFieldGridPos(shipPos, fieldData.StartCoordinate, fieldData.CellSize);
        float falloffRadius = math.max(0.01f, effectiveRadius * fieldData.CellSize);
        bool foundObstacle = false;

        for (int dy = -effectiveRadius; dy <= effectiveRadius; dy++)
        {
            for (int dx = -effectiveRadius; dx <= effectiveRadius; dx++)
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

                foundObstacle = true;

                float2 cellCenter = FlowFieldUtility.FlowFieldGridToWorld(cell, fieldData.StartCoordinate, fieldData.CellSize);
                float2 toShip = shipPos - cellCenter;
                float dist = math.length(toShip);

                if (dist < 0.01f)
                {
                    float2 windowCenter = FlowFieldUtility.FlowFieldGridToWorld(centerCell, fieldData.StartCoordinate, fieldData.CellSize);
                    toShip = math.normalizesafe(shipPos - windowCenter);
                    if (math.lengthsq(toShip) < VelocityWriteEpsilonSq)
                        toShip = new float2(1f, 0f);

                    dist = 0.01f;
                }

                float falloff = math.saturate(1f - dist / falloffRadius);
                float weight = gridCell.Walkable ? gridCell.Cost / 256f : 1f;
                repulsion += math.normalizesafe(toShip) * (weight * falloff * settings.Strength);
            }
        }

        if (!foundObstacle)
            return false;

        float repulsionLength = math.length(repulsion);
        if (repulsionLength > settings.MaxRepulsion)
            repulsion *= settings.MaxRepulsion / repulsionLength;

        return true;
    }

    private static bool ShouldRunPreventiveThisFrame(Entity entity, uint currentBucket)
    {
        unchecked
        {
            uint hash = ((uint)entity.Index * 747796405u) ^ ((uint)entity.Version * 2891336453u);
            return hash % PreventiveStaggerBuckets == currentBucket;
        }
    }

    private static void WriteAvoidance(RefRW<MovementVelocityIntent> intent, float2 avoidanceVelocity, bool inHardDanger)
    {
        if (!math.isfinite(avoidanceVelocity.x) || !math.isfinite(avoidanceVelocity.y))
            avoidanceVelocity = float2.zero;

        if (math.distancesq(intent.ValueRO.AvoidanceVelocity, avoidanceVelocity) > VelocityWriteEpsilonSq)
            intent.ValueRW.AvoidanceVelocity = avoidanceVelocity;

        byte dangerByte = inHardDanger ? (byte)1 : (byte)0;
        if (intent.ValueRO.InHardDanger != dangerByte)
            intent.ValueRW.InHardDanger = dangerByte;
    }
}
