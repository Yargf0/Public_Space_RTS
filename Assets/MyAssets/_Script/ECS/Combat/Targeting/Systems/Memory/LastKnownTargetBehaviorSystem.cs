using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateAfter(typeof(ShipAgroSystem))]
[UpdateBefore(typeof(FightSystem))]
[UpdateBefore(typeof(SetTotalVelocitySystem))]
public partial struct LastKnownTargetBehaviorSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach ((RefRW<LastKnownTarget> lastKnownTarget,
                  EnabledRefRW<LastKnownTarget> lastKnownTargetEnabled,
                  RefRW<ShipStateComponent> shipState,
                  RefRW<ShipAgro> shipAgro,
                  RefRW<UnitMover> unitMover,
                  Entity entity)
            in SystemAPI.Query<RefRW<LastKnownTarget>, EnabledRefRW<LastKnownTarget>, RefRW<ShipStateComponent>, RefRW<ShipAgro>, RefRW<UnitMover>>()
                .WithEntityAccess())
        {
            if (shipState.ValueRO.currentState != ShipState.InCombat)
            {
                lastKnownTargetEnabled.ValueRW = false;
                continue;
            }

            if (shipAgro.ValueRO.targetEntity != Entity.Null)
            {
                lastKnownTargetEnabled.ValueRW = false;
                continue;
            }

            bool exitLkp = false;

            if (lastKnownTarget.ValueRO.target != Entity.Null && !SystemAPI.Exists(lastKnownTarget.ValueRO.target))
            {
                exitLkp = true;
            }

            if (!exitLkp)
            {
                lastKnownTarget.ValueRW.searchTimer -= dt;
                unitMover.ValueRW.fightTarget = lastKnownTarget.ValueRO.lastKnownPosition;

                if (lastKnownTarget.ValueRO.searchTimer > 0f)
                {
                    SystemAPI.SetComponentEnabled<ShipAgro>(entity, true);
                    continue;
                }

                exitLkp = true;
            }

            if (!exitLkp)
            {
                continue;
            }

            lastKnownTargetEnabled.ValueRW = false;
            shipAgro.ValueRW.targetEntity = Entity.Null;
            SystemAPI.SetComponentEnabled<ShipAgro>(entity, false);

            if (shipState.ValueRO.forcedTarget != Entity.Null)
            {
                shipState.ValueRW.forcedTarget = Entity.Null;
            }

            ShipState fallback = shipState.ValueRO.previousState;
            if (fallback == ShipState.InCombat)
            {
                fallback = ShipState.ReturnToGroup;
            }

            shipState.ValueRW.currentState = fallback;
        }
    }
}
