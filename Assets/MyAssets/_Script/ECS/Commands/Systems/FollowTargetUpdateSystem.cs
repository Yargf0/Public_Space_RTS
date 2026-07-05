using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ShipStateChangeSystem))]
partial struct FollowTargetUpdateSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<UnitCollisionRadius> radiusLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        radiusLookup = state.GetComponentLookup<UnitCollisionRadius>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        radiusLookup.Update(ref state);

        foreach ((RefRO<ShipStateComponent> shipState, RefRO<UnitCollisionRadius> myRadius, RefRW<UnitMover> unitMover, Entity entity)
            in SystemAPI.Query<RefRO<ShipStateComponent>, RefRO<UnitCollisionRadius>, RefRW<UnitMover>>().WithEntityAccess())
        {
            if (shipState.ValueRO.currentState != ShipState.Following)
            {
                continue;
            }

            Entity leader = shipState.ValueRO.forcedTarget;
            if (leader == Entity.Null || !transformLookup.HasComponent(leader))
            {
                continue;
            }

            LocalTransform leaderTransform = transformLookup[leader];
            float2 leaderPos = leaderTransform.Position.xy;

            float2 leaderForward = math.normalizesafe(
                math.rotate(leaderTransform.Rotation, new float3(0f, 1f, 0f)).xy,
                new float2(0f, 1f));

            float myRadiusValue = math.max(myRadius.ValueRO.collisionRadius.x, myRadius.ValueRO.collisionRadius.y);

            float leaderRadiusValue = 0f;
            if (radiusLookup.HasComponent(leader))
            {
                float2 leaderRadius = radiusLookup[leader].collisionRadius;
                leaderRadiusValue = math.max(leaderRadius.x, leaderRadius.y);
            }

            float baseFollowDistance = unitMover.ValueRO.followDistance > 0f
                ? unitMover.ValueRO.followDistance
                : GameConstants.DefaultFollowDistance;

            float finalFollowDistance = baseFollowDistance + myRadiusValue + leaderRadiusValue + 0.75f;

            unitMover.ValueRW.targetPos = leaderPos - leaderForward * finalFollowDistance;
        }
    }
}