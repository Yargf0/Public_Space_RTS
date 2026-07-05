using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateAfter(typeof(SearchlightSpotSystem))]
[UpdateBefore(typeof(RocketMoverSystem))]
public partial struct MissileVisibilityWatcherSystem : ISystem
{
    private Entity gridEntity;
    private bool hasGridEntity;

    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<Visibility> visibilityLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<FindTarget> findTargetLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        hasGridEntity = false;

        gridDataLookup = state.GetComponentLookup<GridData>(true);
        visibilityLookup = state.GetComponentLookup<Visibility>(true);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        findTargetLookup = state.GetComponentLookup<FindTarget>(true);

        state.RequireForUpdate<GridData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!hasGridEntity)
        {
            gridEntity = SystemAPI.GetSingletonEntity<GridData>();
            hasGridEntity = true;
        }

        gridDataLookup.Update(ref state);
        visibilityLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        findTargetLookup.Update(ref state);

        GridData gridData = gridDataLookup.GetRefRO(gridEntity).ValueRO;
        float dt = SystemAPI.Time.DeltaTime;

        foreach ((RefRW<Rocket> rocket,
                  RefRW<Target> target,
                  RefRW<LastKnownTarget> lastKnownTarget,
                  EnabledRefRW<LastKnownTarget> lastKnownTargetEnabled,
                  RefRO<LocalTransform> localTransform,
                  Entity entity)
            in SystemAPI.Query<RefRW<Rocket>, RefRW<Target>, RefRW<LastKnownTarget>, EnabledRefRW<LastKnownTarget>, RefRO<LocalTransform>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            if (!rocket.ValueRO.useFogOfWar)
            {
                continue;
            }

            RocketFlightPhase phase = (RocketFlightPhase)rocket.ValueRO.phase;

            if (phase == RocketFlightPhase.Locked)
            {
                if (target.ValueRO.targetEntity == Entity.Null)
                {
                    continue;
                }

                if (VisibilityUtility.IsVisibleToFaction(target.ValueRO.targetEntity, rocket.ValueRO.ownerFaction, ref visibilityLookup))
                {
                    continue;
                }

                lastKnownTarget.ValueRW.target = target.ValueRO.targetEntity;
                lastKnownTarget.ValueRW.lastKnownPosition = target.ValueRO.targetPosition;
                lastKnownTarget.ValueRW.searchTimer = 0f;
                lastKnownTargetEnabled.ValueRW = true;

                target.ValueRW.targetEntity = Entity.Null;
                rocket.ValueRW.phase = (byte)RocketFlightPhase.Lkp;
                rocket.ValueRW.lkpFreeFlightTimer = 0f;
                continue;
            }

            if (phase == RocketFlightPhase.Lkp)
            {
                Entity rememberedTarget = lastKnownTarget.ValueRO.target;

                if (rememberedTarget != Entity.Null && localTransformLookup.HasComponent(rememberedTarget) && VisibilityUtility.IsVisibleToFaction(rememberedTarget, rocket.ValueRO.ownerFaction, ref visibilityLookup))
                {
                    LocalTransform rememberedTransform = localTransformLookup[rememberedTarget];

                    target.ValueRW.targetEntity = rememberedTarget;
                    target.ValueRW.targetPosition = rememberedTransform.Position.xy;

                    lastKnownTargetEnabled.ValueRW = false;
                    rocket.ValueRW.phase = (byte)RocketFlightPhase.Locked;
                    rocket.ValueRW.lkpFreeFlightTimer = 0f;
                    continue;
                }

                float2 rocketPos = localTransform.ValueRO.Position.xy;
                float distToLkpSq = math.lengthsq(rocketPos - lastKnownTarget.ValueRO.lastKnownPosition);
                float arrivalSq = GameConstants.RocketLkpArrivalDistance * GameConstants.RocketLkpArrivalDistance;

                if (distToLkpSq > arrivalSq)
                {
                    continue;
                }

                Entity reacquired = Entity.Null;
                float2 reacquiredPosition = float2.zero;

                if (findTargetLookup.HasComponent(entity))
                {
                    FindTarget findTarget = findTargetLookup[entity];

                    float2 searchPos = rocketPos;

                    TryFindVisibleRocketTarget(
                        ref gridData,
                        target.ValueRO.targetFaction,
                        rocket.ValueRO.ownerFaction,
                        ref searchPos,
                        findTarget.range,
                        findTarget.allowedTargets,
                        findTarget.priorityTargets,
                        ref visibilityLookup,
                        ref reacquired,
                        ref reacquiredPosition);
                }

                if (reacquired != Entity.Null)
                {
                    target.ValueRW.targetEntity = reacquired;
                    target.ValueRW.targetPosition = reacquiredPosition;

                    lastKnownTargetEnabled.ValueRW = false;
                    rocket.ValueRW.phase = (byte)RocketFlightPhase.Locked;
                    rocket.ValueRW.lkpFreeFlightTimer = 0f;
                }
                else
                {
                    target.ValueRW.targetEntity = Entity.Null;
                    lastKnownTargetEnabled.ValueRW = false;
                    rocket.ValueRW.phase = (byte)RocketFlightPhase.FreeDelay;
                    rocket.ValueRW.lkpFreeFlightTimer = GameConstants.RocketLkpFreeFlightDuration;
                }

                continue;
            }

            if (phase == RocketFlightPhase.FreeDelay)
            {
                target.ValueRW.targetEntity = Entity.Null;

                float newFreeFlightTimer = math.max(0f, rocket.ValueRO.lkpFreeFlightTimer - dt);
                rocket.ValueRW.lkpFreeFlightTimer = newFreeFlightTimer;

                if (newFreeFlightTimer <= 0f)
                {
                    rocket.ValueRW.phase = (byte)RocketFlightPhase.Free;
                }

                continue;
            }

            if (phase == RocketFlightPhase.Free)
            {
                target.ValueRW.targetEntity = Entity.Null;
            }
        }
    }

    private static void TryFindVisibleRocketTarget(
        ref GridData gridData,
        Faction targetFaction,
        Faction observerFaction,
        ref float2 pos,
        float range,
        byte allowedTargets,
        byte priorityTargets,
        ref ComponentLookup<Visibility> visibilityLookup,
        ref Entity result,
        ref float2 resultPosition)
    {
        NativeParallelMultiHashMap<int2, Grid> map = CombatUtility.GetEntityMap(in gridData, targetFaction);

        int2 min = GridUtility.WorldToSmallCell(pos - range);
        int2 max = GridUtility.WorldToSmallCell(pos + range);

        Entity closestPriority = Entity.Null;
        float2 closestPriorityPosition = float2.zero;
        float closestPriorityDist = float.MaxValue;

        Entity closestAllowed = Entity.Null;
        float2 closestAllowedPosition = float2.zero;
        float closestAllowedDist = float.MaxValue;

        for (int y = min.y; y <= max.y; y++)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                if (!map.TryGetFirstValue(new int2(x, y), out Grid grid, out NativeParallelMultiHashMapIterator<int2> it))
                {
                    continue;
                }

                do
                {
                    if ((allowedTargets & grid.ShipSize) == 0)
                    {
                        continue;
                    }

                    if (!VisibilityUtility.IsVisibleToFaction(grid.Entity, observerFaction, ref visibilityLookup))
                    {
                        continue;
                    }

                    float edgeDist = math.distance(grid.Position, pos) - math.max(grid.CollisionRadius.x, grid.CollisionRadius.y);
                    if (edgeDist > range)
                    {
                        continue;
                    }

                    if ((priorityTargets & grid.ShipSize) != 0)
                    {
                        if (edgeDist < closestPriorityDist)
                        {
                            closestPriorityDist = edgeDist;
                            closestPriority = grid.Entity;
                            closestPriorityPosition = grid.Position;
                        }
                    }
                    else
                    {
                        if (edgeDist < closestAllowedDist)
                        {
                            closestAllowedDist = edgeDist;
                            closestAllowed = grid.Entity;
                            closestAllowedPosition = grid.Position;
                        }
                    }
                }
                while (map.TryGetNextValue(out grid, ref it));
            }
        }

        if (closestPriority != Entity.Null)
        {
            result = closestPriority;
            resultPosition = closestPriorityPosition;
        }
        else
        {
            result = closestAllowed;
            resultPosition = closestAllowedPosition;
        }
    }
}