using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// takes next command when ship is ready: Idle, GuardPosition
// or close to its ReturnToGroup point
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(GroupManagerSystem))]
[UpdateBefore(typeof(ShipStateChangeSystem))]
partial struct CommandDequeueSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        MovementPathDebugLogSettings debugSettings = SystemAPI.HasSingleton<MovementPathDebugLogSettings>()
            ? SystemAPI.GetSingleton<MovementPathDebugLogSettings>()
            : MovementPathDebugLogSettings.Disabled();

        // only asks movement here, flow field group is made in GroupManagerSystem
        foreach ((RefRW<ShipStateComponent> shipState, RefRW<UnitMover> unitMover, RefRW<GroupManagerComponent> groupManager, RefRW<UnitGroup> unitGroup, DynamicBuffer<CommandQueueElement> queue, Entity entity)
            in SystemAPI.Query<RefRW<ShipStateComponent>, RefRW<UnitMover>, RefRW<GroupManagerComponent>, RefRW<UnitGroup>, DynamicBuffer<CommandQueueElement>>().WithEntityAccess())
        {
            // Nothing to dequeue.
            if (queue.Length == 0)
            {
                continue;
            }

            bool shouldDequeue = false;

            // Idle ship takes next command.
            if (shipState.ValueRO.currentState == ShipState.Idle)
            {
                shouldDequeue = true;
            }

            // Guarding ship too.
            if (shipState.ValueRO.currentState == ShipState.GuardPosition)
            {
                shouldDequeue = true;
            }

            // ReturnToGroup can continue when close enough.
            if (shipState.ValueRO.currentState == ShipState.ReturnToGroup)
            {
                if (SystemAPI.HasComponent<LocalTransform>(entity))
                {
                    float2 pos = SystemAPI.GetComponent<LocalTransform>(entity).Position.xy;
                    float distSq = math.distancesq(pos, unitMover.ValueRO.targetPos);

                    if (distSq < GameConstants.ReachDistanceSq * 100f)
                    {
                        shouldDequeue = true;
                    }
                }
            }

            if (!shouldDequeue)
            {
                continue;
            }

            // Take first command from queue.
            CommandQueueElement command = queue[0];
            queue.RemoveAt(0);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugSettings.Enabled &&
                debugSettings.LogCommandDequeue &&
                (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
            {
                Debug.Log($"[MovePath 03 DEQUEUE] ship={entity.Index}:{entity.Version} type={(int)command.type} target=({command.targetPosition.x},{command.targetPosition.y}) queueLeft={queue.Length} oldState={(int)shipState.ValueRO.currentState}");
            }
#endif

            // old state is needed to return from combat
            shipState.ValueRW.previousState = shipState.ValueRO.currentState;

            switch (command.type)
            {
                case CommandType.MoveTo:
                case CommandType.AttackMove:
                    {
                        shipState.ValueRW.currentState = ShipState.MovingToTarget;
                        shipState.ValueRW.forcedTarget = Entity.Null;
                        shipState.ValueRW.moveMode = command.type == CommandType.AttackMove ? MoveMode.AttackMove : command.moveMode;
                        unitMover.ValueRW.targetPos = command.targetPosition;

                        // Clear old group, new one comes from GroupManagerSystem.
                        groupManager.ValueRW = new GroupManagerComponent
                        {
                            position = command.targetPosition,
                            addOrCreateGroup = true,
                            setTargetWithoutGroup = false,
                            partOfSwarm = false,
                            overrideSizeClass = default,
                            useOverrideSizeClass = false,
                        };

                        unitGroup.ValueRW.GroupEntity = Entity.Null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        if (debugSettings.Enabled &&
                            debugSettings.LogCommandDequeue &&
                            (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == entity.Index))
                        {
                            Debug.Log($"[MovePath 04 GROUP_REQUEST] ship={entity.Index}:{entity.Version} target=({command.targetPosition.x},{command.targetPosition.y}) addGroup=1 partOfSwarm=0 newState={(int)shipState.ValueRO.currentState}");
                        }
#endif
                        break;
                    }

                case CommandType.AttackTarget:
                    {
                        // Skip commands with dead target.
                        if (SystemAPI.Exists(command.targetEntity) && SystemAPI.HasComponent<LocalTransform>(command.targetEntity))
                        {
                            shipState.ValueRW.currentState = ShipState.InCombat;
                            shipState.ValueRW.forcedTarget = command.targetEntity;
                        }
                        // target is gone, next command on next frame
                        break;
                    }

                case CommandType.Follow:
                    {
                        if (SystemAPI.Exists(command.targetEntity) && SystemAPI.HasComponent<LocalTransform>(command.targetEntity))
                        {
                            shipState.ValueRW.currentState = ShipState.Following;
                            shipState.ValueRW.forcedTarget = command.targetEntity;
                        }
                        break;
                    }
            }
        }
    }
}
