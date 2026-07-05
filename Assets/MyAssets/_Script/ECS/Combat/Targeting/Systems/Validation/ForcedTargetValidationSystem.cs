using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

// checks player direct targets before aggro and state systems
// dead or unloaded targets get cleared from forcedTarget
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ShipStateChangeSystem))]
partial struct ForcedTargetValidationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (
            RefRW<ShipStateComponent> shipState
            in SystemAPI.Query<
                RefRW<ShipStateComponent>>())
        {
            // no direct target
            if (shipState.ValueRO.forcedTarget == Entity.Null)
            {
                continue;
            }

            // target without transform is not valid anymore
            bool targetAlive =
                SystemAPI.Exists(shipState.ValueRO.forcedTarget) &&
                SystemAPI.HasComponent<LocalTransform>(shipState.ValueRO.forcedTarget);

            if (!targetAlive)
            {
                // clear stale target
                shipState.ValueRW.forcedTarget = Entity.Null;

                // Following needs fallback right away when leader is gone
                if (shipState.ValueRO.currentState == ShipState.Following)
                {
                    shipState.ValueRW.previousState = ShipState.Following;
                    shipState.ValueRW.currentState = ShipState.Idle;
                }

                // InCombat fallback is done by ShipStateChangeSystem next frame
            }
        }
    }
}
