using Unity.Burst;
using Unity.Entities;

// ship state machine: movement, targeting, auto combat
// player target overrides fire mode, MoveMode says when auto combat can take the ship
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ShipAgroSystem))]
[UpdateBefore(typeof(FightSystem))]
partial struct ShipStateChangeSystem : ISystem
{
    private ComponentLookup<LastKnownTarget> lastKnownTargetLookup;
    private ComponentLookup<CanControl> canControlLookup;
    private ComponentLookup<ShipSquadRef> shipSquadRefLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        lastKnownTargetLookup = state.GetComponentLookup<LastKnownTarget>(true);
        canControlLookup = state.GetComponentLookup<CanControl>(true);
        shipSquadRefLookup = state.GetComponentLookup<ShipSquadRef>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        lastKnownTargetLookup.Update(ref state);
        canControlLookup.Update(ref state);
        shipSquadRefLookup.Update(ref state);

        foreach ((RefRW<ShipStateComponent> shipState, RefRO<ShipAgro> shipAgro, Entity entity)
            in SystemAPI.Query<RefRW<ShipStateComponent>, RefRO<ShipAgro>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            // ShipAgro enabled = enemy detected
            bool hasEnemy = SystemAPI.IsComponentEnabled<ShipAgro>(entity);

            // ReturnFire uses this short damage memory.
            bool wasHit = shipAgro.ValueRO.wasHit;

            // Direct player target.
            bool hasForcedTarget = shipState.ValueRO.forcedTarget != Entity.Null;

            // LKP keeps InCombat while ship flies to lost target
            bool hasLastKnownTarget = lastKnownTargetLookup.HasComponent(entity) && lastKnownTargetLookup.IsComponentEnabled(entity);

            FireMode mode = shipState.ValueRO.mode;
            ShipState current = shipState.ValueRO.currentState;
            ShipState next = current;

            // can auto combat start?
            bool autoEngageAllowed = false;
            switch (mode)
            {
                case FireMode.FireAtWill:
                    // engage when enemy detected
                    autoEngageAllowed = hasEnemy;
                    break;

                case FireMode.ReturnFire:
                    // engage only after taking damage
                    autoEngageAllowed = hasEnemy && wasHit;
                    break;

                case FireMode.HoldFire:
                    // Never auto-engage.
                    autoEngageAllowed = false;
                    break;
            }

            bool canAutoEnterCombat = MoveModeCombatRules.CanAutoEnterCombat(
                shipState.ValueRO.moveMode,
                current,
                autoEngageAllowed,
                shipState.ValueRO.forcedTarget,
                !IsPlayerControlled(entity, ref canControlLookup, ref shipSquadRefLookup));

            // State transitions.
            switch (current)
            {
                case ShipState.Idle:
                    {
                        // Direct attack order enters combat.
                        if (hasForcedTarget)
                        {
                            next = ShipState.InCombat;
                            break;
                        }

                        // no movement order, auto combat allowed
                        if (canAutoEnterCombat)
                        {
                            next = ShipState.InCombat;
                        }

                        break;
                    }

                case ShipState.MovingToTarget:
                    {
                        // direct attack order overrides movement
                        if (hasForcedTarget)
                        {
                            next = ShipState.InCombat;
                            break;
                        }

                        // AttackMove can fight on the way, MoveAndEngage usually waits
                        if (canAutoEnterCombat)
                        {
                            next = ShipState.InCombat;
                        }

                        break;
                    }

                case ShipState.InCombat:
                    {
                        // HoldPosition = hard hold. weapons still work, forcedTarget orders still work,
                        // but combat movement don't own the ship
                        if (shipState.ValueRO.moveMode == MoveMode.HoldPosition && !hasForcedTarget)
                        {
                            next = ShipState.Idle;
                            break;
                        }

                        if (!hasEnemy && !hasForcedTarget && !hasLastKnownTarget)
                        {
                            bool hasQueuedCommands = false;

                            if (SystemAPI.HasBuffer<CommandQueueElement>(entity))
                            {
                                DynamicBuffer<CommandQueueElement> queue = SystemAPI.GetBuffer<CommandQueueElement>(entity);
                                hasQueuedCommands = queue.Length > 0;
                            }

                            // Idle lets CommandDequeueSystem take next command
                            if (hasQueuedCommands)
                            {
                                next = ShipState.Idle;
                            }
                            else if (shipState.ValueRO.previousState == ShipState.Following && shipState.ValueRO.forcedTarget != Entity.Null)
                            {
                                next = ShipState.Following;
                            }
                            else if (shipState.ValueRO.previousState == ShipState.MovingToTarget)
                            {
                                next = ShipState.ReturnToGroup;
                            }
                            else
                            {
                                next = ShipState.ReturnToGroup;
                            }
                        }

                        break;
                    }

                case ShipState.GuardPosition:
                    {
                        // Direct attack order enters combat.
                        if (hasForcedTarget)
                        {
                            next = ShipState.InCombat;
                            break;
                        }

                        // no movement order, auto combat allowed
                        if (canAutoEnterCombat)
                        {
                            next = ShipState.InCombat;
                        }

                        break;
                    }

                case ShipState.ReturnToGroup:
                    {
                        // Direct attack order enters combat.
                        if (hasForcedTarget)
                        {
                            next = ShipState.InCombat;
                            break;
                        }

                        // ReturnToGroup is not player order, auto combat can resume
                        if (canAutoEnterCombat)
                        {
                            next = ShipState.InCombat;
                        }

                        break;
                    }

                case ShipState.Following:
                    {
                        // dead leader is handled by ForcedTargetValidationSystem
                        if (!hasForcedTarget)
                        {
                            next = ShipState.Idle;
                            break;
                        }

                        // Following is active order, no auto combat
                        break;
                    }
            }

            // Apply the transition.
            if (next != current)
            {
                shipState.ValueRW.previousState = current;
                shipState.ValueRW.currentState = next;
            }
        }
    }

    private static bool IsPlayerControlled(
        Entity entity,
        ref ComponentLookup<CanControl> canControlLookup,
        ref ComponentLookup<ShipSquadRef> shipSquadRefLookup)
    {
        if (canControlLookup.HasComponent(entity))
            return true;

        if (!shipSquadRefLookup.TryGetComponent(entity, out ShipSquadRef squadRef))
            return false;

        return squadRef.squad != Entity.Null && canControlLookup.HasComponent(squadRef.squad);
    }
}
