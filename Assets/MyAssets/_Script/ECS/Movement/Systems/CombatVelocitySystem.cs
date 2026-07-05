using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PathVelocitySystem))]
[UpdateAfter(typeof(FightSystem))]
[UpdateAfter(typeof(LastKnownTargetBehaviorSystem))]
[UpdateBefore(typeof(BoidSystem))]
public partial struct CombatVelocitySystem : ISystem
{
    private ComponentLookup<Velocity> velocityLookup;
    private const float VelocityWriteEpsilonSq = 0.0001f;

    public void OnCreate(ref SystemState state)
    {
        velocityLookup = state.GetComponentLookup<Velocity>(isReadOnly: false);
        state.RequireForUpdate<FlowFieldData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        velocityLookup.Update(ref state);

        foreach ((
            RefRO<ShipStateComponent> shipState,
            RefRO<UnitMover> unitMover,
            RefRO<LocalTransform> localTransform,
            RefRO<Boid> boid,
            RefRW<MovementVelocityIntent> intent,
            Entity entity)
            in SystemAPI.Query<
                    RefRO<ShipStateComponent>,
                    RefRO<UnitMover>,
                    RefRO<LocalTransform>,
                    RefRO<Boid>,
                    RefRW<MovementVelocityIntent>>()
                .WithAll<Velocity>()
                .WithEntityAccess())
        {
            float2 combatVelocity = float2.zero;

            if (shipState.ValueRO.currentState == ShipState.InCombat)
            {
                float2 position = localTransform.ValueRO.Position.xy;
                if (math.isfinite(position.x) && math.isfinite(position.y))
                    combatVelocity = ShipMovementIntentResolver.ResolveDirectCombatVelocity(position, unitMover.ValueRO.fightTarget, boid.ValueRO.flowFieldWeight, 0.15f);
            }

            if (!math.isfinite(combatVelocity.x) || !math.isfinite(combatVelocity.y))
                combatVelocity = float2.zero;

            if (math.distancesq(intent.ValueRO.CombatVelocity, combatVelocity) > VelocityWriteEpsilonSq)
                intent.ValueRW.CombatVelocity = combatVelocity;

            if (shipState.ValueRO.currentState == ShipState.InCombat)
                WriteFlowVelocityIfChanged(entity, combatVelocity);
        }
    }

    private void WriteFlowVelocityIfChanged(Entity entity, float2 flowVelocity)
    {
        if (!velocityLookup.HasComponent(entity))
            return;

        Velocity velocity = velocityLookup[entity];
        float2 oldFlowVelocity = velocity.flowFieldVelocity;
        if (!math.isfinite(oldFlowVelocity.x) || !math.isfinite(oldFlowVelocity.y) || math.distancesq(oldFlowVelocity, flowVelocity) > VelocityWriteEpsilonSq)
        {
            velocity.flowFieldVelocity = flowVelocity;
            velocityLookup[entity] = velocity;
        }
    }
}
