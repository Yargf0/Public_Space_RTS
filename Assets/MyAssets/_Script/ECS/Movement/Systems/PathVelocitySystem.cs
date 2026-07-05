using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FlowFieldUpdateSystem))]
[UpdateBefore(typeof(CombatVelocitySystem))]
public partial struct PathVelocitySystem : ISystem
{
    private BufferLookup<GridCell> gridCellLookup;
    private BufferLookup<PathGridCellSmall> smallLookup;
    private BufferLookup<PathGridCellMedium> mediumLookup;
    private BufferLookup<PathGridCellLarge> largeLookup;
    private BufferLookup<GroupCell> groupCellLookup;
    private ComponentLookup<GroupGridCell> groupGridCellLookup;
    private ComponentLookup<UnitCollisionRadius> collisionRadiusLookup;
    private ComponentLookup<Velocity> velocityLookup;

    private const float VelocityWriteEpsilonSq = 0.0001f;

    public void OnCreate(ref SystemState state)
    {
        gridCellLookup = state.GetBufferLookup<GridCell>(isReadOnly: true);
        smallLookup = state.GetBufferLookup<PathGridCellSmall>(isReadOnly: true);
        mediumLookup = state.GetBufferLookup<PathGridCellMedium>(isReadOnly: true);
        largeLookup = state.GetBufferLookup<PathGridCellLarge>(isReadOnly: true);
        groupCellLookup = state.GetBufferLookup<GroupCell>(isReadOnly: true);
        groupGridCellLookup = state.GetComponentLookup<GroupGridCell>(isReadOnly: true);
        collisionRadiusLookup = state.GetComponentLookup<UnitCollisionRadius>(isReadOnly: true);
        velocityLookup = state.GetComponentLookup<Velocity>(isReadOnly: false);
        state.RequireForUpdate<FlowFieldData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        gridCellLookup.Update(ref state);
        smallLookup.Update(ref state);
        mediumLookup.Update(ref state);
        largeLookup.Update(ref state);
        groupCellLookup.Update(ref state);
        groupGridCellLookup.Update(ref state);
        collisionRadiusLookup.Update(ref state);
        velocityLookup.Update(ref state);

        FlowFieldData config = SystemAPI.GetSingleton<FlowFieldData>();
        float cellSize = config.CellSize;
        int2 gridSize = config.GridSize;
        float2 startCoordinat = config.StartCoordinate;

        Entity flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();
        bool hasGrid = false;
        DynamicBuffer<GridCell> gridCells = default;

        if (gridCellLookup.HasBuffer(flowFieldEntity))
        {
            gridCells = gridCellLookup[flowFieldEntity];
            hasGrid = gridCells.Length > 0;
        }

        bool hasSmall = smallLookup.HasBuffer(flowFieldEntity) && smallLookup[flowFieldEntity].Length > 0;
        bool hasMedium = mediumLookup.HasBuffer(flowFieldEntity) && mediumLookup[flowFieldEntity].Length > 0;
        bool hasLarge = largeLookup.HasBuffer(flowFieldEntity) && largeLookup[flowFieldEntity].Length > 0;

        MovementPathDebugLogSettings debugSettings = SystemAPI.HasSingleton<MovementPathDebugLogSettings>()
            ? SystemAPI.GetSingleton<MovementPathDebugLogSettings>()
            : MovementPathDebugLogSettings.Disabled();

        foreach ((
            RefRW<ShipStateComponent> shipState,
            RefRO<UnitMover> unitMover,
            RefRO<UnitGroup> unitGroup,
            RefRO<LocalTransform> localTransform,
            RefRO<Boid> boid,
            RefRW<MovementVelocityIntent> intent,
            Entity entity)
            in SystemAPI.Query<
                    RefRW<ShipStateComponent>,
                    RefRO<UnitMover>,
                    RefRO<UnitGroup>,
                    RefRO<LocalTransform>,
                    RefRO<Boid>,
                    RefRW<MovementVelocityIntent>>()
                .WithAll<PathfindingSizeClassComponent, Velocity>()
                .WithEntityAccess())
        {
            float2 position = localTransform.ValueRO.Position.xy;
            if (!math.isfinite(position.x) || !math.isfinite(position.y))
            {
                WritePathIntent(intent, entity, float2.zero, false);
                continue;
            }

            float2 pathVelocity = float2.zero;
            bool forceZeroVelocity = false;

            switch (shipState.ValueRO.currentState)
            {
                case ShipState.MovingToTarget:
                case ShipState.ReturnToGroup:
                    pathVelocity = ResolvePathVelocity(
                        entity,
                        shipState,
                        unitGroup.ValueRO.GroupEntity,
                        unitMover.ValueRO.targetPos,
                        boid.ValueRO.flowFieldWeight,
                        position,
                        flowFieldEntity,
                        config,
                        gridCells,
                        hasGrid,
                        hasSmall,
                        hasMedium,
                        hasLarge,
                        gridSize,
                        startCoordinat,
                        cellSize,
                        debugSettings);
                    break;

                case ShipState.Following:
                    float2 shipHalfExtents = collisionRadiusLookup.HasComponent(entity)
                        ? collisionRadiusLookup[entity].collisionRadius
                        : float2.zero;

                    pathVelocity = ShipMovementIntentResolver.ResolveFollowingVelocity(
                        position,
                        unitMover.ValueRO.targetPos,
                        shipHalfExtents,
                        unitMover.ValueRO.maxSpeed,
                        boid.ValueRO.flowFieldWeight,
                        out forceZeroVelocity);
                    break;

                case ShipState.InCombat:
                case ShipState.Idle:
                case ShipState.GuardPosition:
                default:
                    pathVelocity = float2.zero;
                    break;
            }

            WritePathIntent(intent, entity, pathVelocity, forceZeroVelocity);
        }
    }

    private void WritePathIntent(RefRW<MovementVelocityIntent> intent, Entity entity, float2 pathVelocity, bool forceZeroVelocity)
    {
        if (!math.isfinite(pathVelocity.x) || !math.isfinite(pathVelocity.y))
            pathVelocity = float2.zero;

        if (math.distancesq(intent.ValueRO.PathVelocity, pathVelocity) > VelocityWriteEpsilonSq)
            intent.ValueRW.PathVelocity = pathVelocity;

        byte forceZeroByte = forceZeroVelocity ? (byte)1 : (byte)0;
        if (intent.ValueRO.ForceZeroVelocity != forceZeroByte)
            intent.ValueRW.ForceZeroVelocity = forceZeroByte;

        if (velocityLookup.HasComponent(entity))
        {
            Velocity velocity = velocityLookup[entity];
            float2 oldFlowVelocity = velocity.flowFieldVelocity;
            if (!math.isfinite(oldFlowVelocity.x) || !math.isfinite(oldFlowVelocity.y) || math.distancesq(oldFlowVelocity, pathVelocity) > VelocityWriteEpsilonSq)
            {
                velocity.flowFieldVelocity = pathVelocity;
                velocityLookup[entity] = velocity;
            }
        }
    }

    private float2 ResolvePathVelocity(
        Entity entity,
        RefRW<ShipStateComponent> shipState,
        Entity groupEntity,
        float2 targetPos,
        float flowFieldWeight,
        float2 position,
        Entity flowFieldEntity,
        in FlowFieldData config,
        DynamicBuffer<GridCell> gridCells,
        bool hasGrid,
        bool hasSmall,
        bool hasMedium,
        bool hasLarge,
        int2 gridSize,
        float2 startCoordinat,
        float cellSize,
        in MovementPathDebugLogSettings debugSettings)
    {
        if (groupEntity == Entity.Null || !groupGridCellLookup.HasComponent(groupEntity) || !groupCellLookup.HasBuffer(groupEntity))
        {
            if (math.distancesq(position, targetPos) < 1f)
                SetIdle(shipState);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugSettings.Enabled && debugSettings.LogSetTotalVelocity && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
                Debug.Log($"[MovePath 10 PATH_WAIT_NO_GROUP] ship={entity.Index}:{entity.Version} group={groupEntity.Index}:{groupEntity.Version} pos=({position.x},{position.y}) target=({targetPos.x},{targetPos.y})");
#endif
            return float2.zero;
        }

        GroupGridCell gridData = groupGridCellLookup[groupEntity];
        if (!gridData.ReadyToMove)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugSettings.Enabled && debugSettings.LogSetTotalVelocity && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
            {
                int needsUpdateInt = gridData.NeedsUpdate ? 1 : 0;
                Debug.Log($"[MovePath 10 PATH_WAIT_FF] ship={entity.Index}:{entity.Version} group={groupEntity.Index}:{groupEntity.Version} ready=0 needsUpdate={needsUpdateInt}");
            }
#endif
            return float2.zero;
        }

        int2 gridPos = FlowFieldUtility.FlowFieldGridPos(position, startCoordinat, cellSize);
        int2 targetGridPos = FlowFieldUtility.FlowFieldGridPos(targetPos, startCoordinat, cellSize);
        DynamicBuffer<GroupCell> groupCells = groupCellLookup[groupEntity];

        PathLayerMoveResult moveResult;
        switch (gridData.SizeClass)
        {
            case PathfindingSizeClass.Small when hasSmall:
                moveResult = PathLayerMovementResolver.Resolve(position, targetPos, gridPos, targetGridPos, gridData, groupCells, gridCells, smallLookup[flowFieldEntity], hasGrid, gridSize, startCoordinat, cellSize, config, flowFieldWeight);
                break;
            case PathfindingSizeClass.Large when hasLarge:
                moveResult = PathLayerMovementResolver.Resolve(position, targetPos, gridPos, targetGridPos, gridData, groupCells, gridCells, largeLookup[flowFieldEntity], hasGrid, gridSize, startCoordinat, cellSize, config, flowFieldWeight);
                break;
            case PathfindingSizeClass.Medium:
            default:
                if (hasMedium)
                    moveResult = PathLayerMovementResolver.Resolve(position, targetPos, gridPos, targetGridPos, gridData, groupCells, gridCells, mediumLookup[flowFieldEntity], hasGrid, gridSize, startCoordinat, cellSize, config, flowFieldWeight);
                else
                    moveResult = new PathLayerMoveResult { FlowVelocity = float2.zero, SetIdle = false };
                break;
        }

        if (moveResult.SetIdle)
            SetIdle(shipState);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugSettings.Enabled && debugSettings.LogSetTotalVelocity && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
            Debug.Log($"[MovePath 11 PATH_FLOWFIELD] ship={entity.Index}:{entity.Version} group={groupEntity.Index}:{groupEntity.Version} size={(int)gridData.SizeClass} cell=({gridPos.x},{gridPos.y}) targetCell=({targetGridPos.x},{targetGridPos.y}) pathVel=({moveResult.FlowVelocity.x},{moveResult.FlowVelocity.y})");
#endif
        return moveResult.FlowVelocity;
    }

    private static void SetIdle(RefRW<ShipStateComponent> shipState)
    {
        shipState.ValueRW.previousState = shipState.ValueRO.currentState;
        shipState.ValueRW.currentState = ShipState.Idle;
    }
}
