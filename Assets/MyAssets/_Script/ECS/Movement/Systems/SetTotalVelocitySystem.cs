using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AvoidanceVelocitySystem))]
[UpdateAfter(typeof(BoidSystem))]
[UpdateBefore(typeof(MoveVelocitySystem))]
public partial struct SetTotalVelocitySystem : ISystem
{
    private ComponentLookup<PathfindingSizeClassComponent> pathSizeClassLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private EntityQuery empStatusQuery;
    private EntityQuery buffStatusQuery;
    private EntityQuery debuffStatusQuery;
    private const float VelocityWriteEpsilonSq = 0.0001f;

    public void OnCreate(ref SystemState state)
    {
        pathSizeClassLookup = state.GetComponentLookup<PathfindingSizeClassComponent>(isReadOnly: true);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(isReadOnly: true);
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(isReadOnly: true);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(isReadOnly: true);
        empStatusQuery = state.GetEntityQuery(ComponentType.ReadOnly<EmpStatus>());
        buffStatusQuery = state.GetEntityQuery(ComponentType.ReadOnly<EmbeddedActionBuffStatus>());
        debuffStatusQuery = state.GetEntityQuery(ComponentType.ReadOnly<EmbeddedActionDebuffStatus>());
        state.RequireForUpdate<FlowFieldData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        pathSizeClassLookup.Update(ref state);
        bool hasAnyMoveStatus =
            empStatusQuery.CalculateEntityCount() > 0 ||
            buffStatusQuery.CalculateEntityCount() > 0 ||
            debuffStatusQuery.CalculateEntityCount() > 0;

        if (hasAnyMoveStatus)
        {
            empStatusLookup.Update(ref state);
            buffStatusLookup.Update(ref state);
            debuffStatusLookup.Update(ref state);
        }

        float dt = SystemAPI.Time.DeltaTime;

        foreach ((
            RefRO<ShipStateComponent> shipState,
            RefRO<UnitMover> unitMover,
            RefRW<Velocity> velocity,
            RefRO<MovementVelocityIntent> intent,
            Entity entity)
            in SystemAPI.Query<
                    RefRO<ShipStateComponent>,
                    RefRO<UnitMover>,
                    RefRW<Velocity>,
                    RefRO<MovementVelocityIntent>>()
                .WithAll<PathfindingSizeClassComponent>()
                .WithEntityAccess())
        {
            float2 currentVelocity = velocity.ValueRO.velocity;
            if (!math.isfinite(currentVelocity.x) || !math.isfinite(currentVelocity.y) || intent.ValueRO.ForceZeroVelocity != 0)
            {
                currentVelocity = float2.zero;
                WriteVelocityIfChanged(velocity, float2.zero);
            }

            float2 primaryVelocity = intent.ValueRO.PathVelocity + intent.ValueRO.CombatVelocity;
            if (!math.isfinite(primaryVelocity.x) || !math.isfinite(primaryVelocity.y))
                primaryVelocity = float2.zero;

            if (intent.ValueRO.InHardDanger != 0)
                primaryVelocity = FlowFieldSamplingUtility.ClampPrimaryMoveAgainstHardDanger(
                    primaryVelocity,
                    intent.ValueRO.AvoidanceVelocity);

            if (CanSkipCalmIdleShip(shipState.ValueRO.currentState, currentVelocity, primaryVelocity, velocity.ValueRO, intent.ValueRO))
            {
                WriteVelocityIfChanged(velocity, float2.zero);
                continue;
            }

            float2 potentialVelocity =
                velocity.ValueRO.separationVelocity +
                velocity.ValueRO.alignmentVelocity +
                velocity.ValueRO.cohesionVelocity +
                primaryVelocity +
                intent.ValueRO.AvoidanceVelocity;

            potentialVelocity = FlowFieldSamplingUtility.ProtectPrimaryVelocityFromCancellation(
                primaryVelocity,
                potentialVelocity,
                intent.ValueRO.AvoidanceVelocity,
                intent.ValueRO.InHardDanger != 0);

            PathfindingSizeClass movementSizeClass = pathSizeClassLookup[entity].Value;
            float moveSpeedMultiplier = 1f;
            float accelerationMultiplier = 1f;
            if (hasAnyMoveStatus)
            {
                moveSpeedMultiplier = EmbeddedActionStatusUtility.GetMoveSpeedMultiplier(entity, ref empStatusLookup, ref buffStatusLookup, ref debuffStatusLookup);
                accelerationMultiplier = EmbeddedActionStatusUtility.GetAccelerationMultiplier(entity, ref empStatusLookup, ref buffStatusLookup, ref debuffStatusLookup);
            }

            float2 finalVelocity = VelocitySteeringUtility.CalculateFinalVelocity(
                currentVelocity,
                potentialVelocity,
                unitMover.ValueRO.maxSpeed * moveSpeedMultiplier,
                unitMover.ValueRO.acceleration * accelerationMultiplier,
                dt,
                movementSizeClass,
                shipState.ValueRO.currentState);

            if (!math.isfinite(finalVelocity.x) || !math.isfinite(finalVelocity.y))
            {
                WriteVelocityIfChanged(velocity, float2.zero);
                continue;
            }

            WriteVelocityIfChanged(velocity, finalVelocity);
        }
    }

    private static bool CanSkipCalmIdleShip(ShipState state, float2 currentVelocity, float2 primaryVelocity, in Velocity velocity, in MovementVelocityIntent intent)
    {
        if (state != ShipState.Idle && state != ShipState.GuardPosition)
            return false;

        return math.lengthsq(currentVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(primaryVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(intent.AvoidanceVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(velocity.separationVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(velocity.alignmentVelocity) <= VelocityWriteEpsilonSq &&
               math.lengthsq(velocity.cohesionVelocity) <= VelocityWriteEpsilonSq;
    }

    private static void WriteVelocityIfChanged(RefRW<Velocity> velocity, float2 newVelocity)
    {
        float2 oldVelocity = velocity.ValueRO.velocity;
        if (!math.isfinite(oldVelocity.x) || !math.isfinite(oldVelocity.y) || math.any(oldVelocity != newVelocity))
            velocity.ValueRW.velocity = newVelocity;
    }
}
