using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class CommandInputHandler : MonoBehaviour
{
    private bool subscribed;

    // InputProvider can be null in first OnEnable, Start tries again.
    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (!subscribed) return;
        subscribed = false;

        var input = InputProvider.Instance;
        if (input == null) return;

        input.OnCommandPressed -= HandleCommand;
        input.OnFireModePressed -= HandleFireMode;
        input.OnStopPressed -= HandleStop;
    }

    private void TrySubscribe()
    {
        if (subscribed) return;

        var input = InputProvider.Instance;
        if (input == null) return;

        input.OnCommandPressed += HandleCommand;
        input.OnFireModePressed += HandleFireMode;
        input.OnStopPressed += HandleStop;
        subscribed = true;
    }

    private void HandleCommand()
    {
        if (InputProvider.Instance == null || InputProvider.Instance.IsPointerOverUI()) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;

        MovementPathDebugLogSettings debugSettings = GetDebugSettings(em);

        bool hitSomething = TryRaycastUnit(em, out Entity hitEntity);
        bool hitEnemy = hitSomething && em.HasComponent<Enemy>(hitEntity);
        bool hitFriendly = hitSomething && em.HasComponent<Friendly>(hitEntity);

        bool appendToQueue = InputProvider.Instance.IsQueueModifierHeld;
        float2 worldPos = (float2)(Vector2)InputProvider.Instance.GetWorldPointerPosition();

        SquadCommandElement command = BuildSquadCommand(em, hitEnemy, hitFriendly, hitEntity, worldPos);

        NativeHashSet<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        NativeArray<Entity> squads = selectedSquads.ToNativeArray(Allocator.Temp);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugSettings.Enabled && debugSettings.LogClick)
        {
            Debug.Log($"[MovePath 01 CLICK] pos=({worldPos.x},{worldPos.y}) hit={(hitSomething ? 1 : 0)} enemy={(hitEnemy ? 1 : 0)} friendly={(hitFriendly ? 1 : 0)} hitEntity={hitEntity.Index}:{hitEntity.Version} append={(appendToQueue ? 1 : 0)} squads={squads.Length}");
        }
#endif

        // whole group selected = group order, partial = per squad
        NativeList<Entity> selectedPlayerGroups;
        NativeHashSet<Entity> groupClaimedSquads;
        PlayerStrikeGroupOrderUtility.CollectFullySelectedPlayerGroups(
            em,
            squads,
            Allocator.Temp,
            out selectedPlayerGroups,
            out groupClaimedSquads);

        if (!appendToQueue)
        {
            for (int i = 0; i < selectedPlayerGroups.Length; i++)
                PlayerStrikeGroupOrderUtility.ApplySquadCommandToGroupOrder(em, selectedPlayerGroups[i], command);
        }

        for (int i = 0; i < squads.Length; i++)
        {
            Entity squad = squads[i];

            if (!appendToQueue && groupClaimedSquads.Contains(squad))
                continue;

            QueueSquadCommand(em, squad, command, appendToQueue, debugSettings);
        }

        // ships without squad use own command queue
        QueueLooseShipCommands(em, hitEnemy, hitFriendly, hitEntity, worldPos, appendToQueue, debugSettings);

        groupClaimedSquads.Dispose();
        selectedPlayerGroups.Dispose();
        squads.Dispose();
        selectedSquads.Dispose();
    }

    private void HandleFireMode()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;

        NativeHashSet<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        NativeArray<Entity> squads = selectedSquads.ToNativeArray(Allocator.Temp);

        for (int i = 0; i < squads.Length; i++)
        {
            Entity squadEntity = squads[i];
            if (!em.Exists(squadEntity) || !em.HasComponent<SquadComponent>(squadEntity))
                continue;

            SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);

            if (squad.origin == SquadOrigin.Carrier)
                continue;

            SquadCommandApplyUtility.ApplyNow(
                em,
                squadEntity,
                new SquadCommandElement
                {
                    type = SquadCommandType.SetFireMode,
                    fireMode = GetNextFireMode(squad.defaultFireMode),
                });
        }

        ApplyFireModeToLooseShips(em);

        squads.Dispose();
        selectedSquads.Dispose();
    }

    private void HandleStop()
    {
        StopSelected();
    }

    private static NativeHashSet<Entity> CollectSelectedSquads(EntityManager em, Allocator allocator)
    {
        NativeHashSet<Entity> squads = new NativeHashSet<Entity>(32, allocator);

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, ShipSquadRef>()
            .Build(em);

        NativeArray<Entity> selectedShips = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < selectedShips.Length; i++)
        {
            Entity ship = selectedShips[i];

            if (!em.IsComponentEnabled<Selected>(ship))
                continue;

            ShipSquadRef shipRef = em.GetComponentData<ShipSquadRef>(ship);
            Entity squadEntity = shipRef.squad;

            if (squadEntity == Entity.Null ||
                !em.Exists(squadEntity) ||
                !em.HasComponent<SquadComponent>(squadEntity))
            {
                continue;
            }

            SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);

            // Carrier squadrons belong to the carrier, player can't command them.
            if (squad.origin == SquadOrigin.Carrier)
                continue;

            squads.Add(squadEntity);
        }

        selectedShips.Dispose();
        query.Dispose();

        return squads;
    }

    private static SquadCommandElement BuildSquadCommand(
        EntityManager em,
        bool hitEnemy,
        bool hitFriendly,
        Entity hitEntity,
        float2 worldPos)
    {
        bool attackModifierHeld = InputProvider.Instance != null && InputProvider.Instance.IsAttackModifierHeld;

        if (hitEnemy)
        {
            return new SquadCommandElement
            {
                type = SquadCommandType.AttackTarget,
                targetEntity = hitEntity,
                targetPosition = em.HasComponent<LocalTransform>(hitEntity)
                    ? em.GetComponentData<LocalTransform>(hitEntity).Position.xy
                    : worldPos,
                moveMode = MoveMode.AttackMove,
            };
        }

        if (hitFriendly)
        {
            return new SquadCommandElement
            {
                type = SquadCommandType.FollowEntity,
                targetEntity = hitEntity,
                targetPosition = em.HasComponent<LocalTransform>(hitEntity)
                    ? em.GetComponentData<LocalTransform>(hitEntity).Position.xy
                    : worldPos,
                moveMode = MoveMode.HoldPosition,
            };
        }

        return new SquadCommandElement
        {
            type = attackModifierHeld ? SquadCommandType.AttackMoveToPoint : SquadCommandType.MoveToPoint,
            targetEntity = Entity.Null,
            targetPosition = worldPos,
            // plain right click = MoveAndEngage: reach point, then fight
            moveMode = attackModifierHeld ? MoveMode.AttackMove : MoveMode.MoveAndEngage,
        };
    }

    private static void QueueSquadCommand(
        EntityManager em,
        Entity squadEntity,
        SquadCommandElement command,
        bool appendToQueue,
        in MovementPathDebugLogSettings debugSettings)
    {
        if (squadEntity == Entity.Null ||
            !em.Exists(squadEntity) ||
            !em.HasComponent<SquadComponent>(squadEntity))
        {
            return;
        }

        SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);

        // Same, carrier squadrons are not for the player.
        if (squad.origin == SquadOrigin.Carrier)
            return;

        command = SquadCommandApplyUtility.ResolveMoveMode(squad, command);

        DynamicBuffer<SquadCommandElement> queue;
        if (em.HasBuffer<SquadCommandElement>(squadEntity))
        {
            queue = em.GetBuffer<SquadCommandElement>(squadEntity);
        }
        else
        {
            queue = em.AddBuffer<SquadCommandElement>(squadEntity);
        }

        if (appendToQueue)
        {
            // Shift = queued order.
            queue.Add(command);
        }
        else
        {
            // No shift = override, clear queue and run now.
            queue.Clear();

            // Player command is hard override for normal squads.
            SquadCommandApplyUtility.ApplyNow(em, squadEntity, command);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugSettings.Enabled && debugSettings.LogCommandQueue)
        {
            Debug.Log($"[MovePath 02 SQUAD_{(appendToQueue ? "QUEUE" : "OVERRIDE")}] squad={squadEntity.Index}:{squadEntity.Version} type={(int)command.type} target=({command.targetPosition.x},{command.targetPosition.y}) queueLen={queue.Length} append={(appendToQueue ? 1 : 0)} immediate={(appendToQueue ? 0 : 1)}");
        }
#endif
    }

    private void QueueLooseShipCommands(
        EntityManager em,
        bool hitEnemy,
        bool hitFriendly,
        Entity hitEntity,
        float2 worldPos,
        bool appendToQueue,
        in MovementPathDebugLogSettings debugSettings)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, CommandQueueElement>()
            .WithPresent<UnitGroup, CanControl, ShipStateComponent>()
            .WithAbsent<ShipSquadRef>()
            .Build(em);

        NativeArray<Entity> ships = query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < ships.Length; i++)
        {
            Entity ship = ships[i];

            if (!em.IsComponentEnabled<Selected>(ship))
                continue;


            ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(ship);
            CommandQueueElement command = BuildLegacyCommand(hitEnemy, hitFriendly, hitEntity, worldPos, shipState.moveMode);

            DynamicBuffer<CommandQueueElement> queue = em.GetBuffer<CommandQueueElement>(ship);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugSettings.Enabled && debugSettings.LogCommandQueue && (debugSettings.DebugShipIndex < 0 || debugSettings.DebugShipIndex == ship.Index))
                {
                    Debug.Log($"[MovePath 02 SHIP_{(appendToQueue ? "QUEUE" : "OVERRIDE")}] ship={ship.Index}:{ship.Version} type={(int)command.type} target=({command.targetPosition.x},{command.targetPosition.y}) oldState={(int)shipState.currentState} append={(appendToQueue ? 1 : 0)} immediate={(appendToQueue ? 0 : 1)} queueBefore={queue.Length}");
                }
            #endif

            if (appendToQueue)
            {
                // Shift = queued order.
                queue.Add(command);
            }
            else
            {
                // No shift = override, clear queue and run now.
                queue.Clear();

                ApplyLooseShipCommandNow(em, ship, command);
            }
        }

        ships.Dispose();
        query.Dispose();
    }

    private static CommandQueueElement BuildLegacyCommand(
        bool hitEnemy,
        bool hitFriendly,
        Entity hitEntity,
        float2 worldPos,
        MoveMode moveMode)
    {
        bool attackModifierHeld = InputProvider.Instance != null && InputProvider.Instance.IsAttackModifierHeld;

        if (hitEnemy)
        {
            return new CommandQueueElement
            {
                type = CommandType.AttackTarget,
                targetEntity = hitEntity,
                moveMode = MoveMode.AttackMove,
            };
        }

        if (hitFriendly)
        {
            return new CommandQueueElement
            {
                type = CommandType.Follow,
                targetEntity = hitEntity,
                moveMode = moveMode,
            };
        }

        if (attackModifierHeld)
        {
            return new CommandQueueElement
            {
                type = CommandType.AttackMove,
                targetPosition = worldPos,
                moveMode = MoveMode.AttackMove,
            };
        }

        return new CommandQueueElement
        {
            type = CommandType.MoveTo,
            targetPosition = worldPos,
            moveMode = moveMode,
        };
    }

    private static void ApplyLooseShipCommandNow(EntityManager em, Entity ship, CommandQueueElement command)
    {
        if (ship == Entity.Null ||
            !em.Exists(ship) ||
            !em.HasComponent<ShipStateComponent>(ship))
        {
            return;
        }

        ShipStateComponent state = em.GetComponentData<ShipStateComponent>(ship);
        state.previousState = state.currentState;
        state.moveMode = command.moveMode;
        state.forcedTarget = Entity.Null;

        switch (command.type)
        {
            case CommandType.MoveTo:
            case CommandType.AttackMove:
                state.currentState = ShipState.MovingToTarget;
                break;

            case CommandType.AttackTarget:
                state.currentState = ShipState.InCombat;
                state.forcedTarget = command.targetEntity;
                break;

            case CommandType.Follow:
                state.currentState = ShipState.Following;
                state.forcedTarget = command.targetEntity;
                break;
        }

        em.SetComponentData(ship, state);

        bool isMovementCommand = command.type == CommandType.MoveTo || command.type == CommandType.AttackMove;

        if (em.HasComponent<UnitMover>(ship))
        {
            UnitMover mover = em.GetComponentData<UnitMover>(ship);

            if (isMovementCommand)
            {
                mover.targetPos = command.targetPosition;
                mover.fightTarget = command.targetPosition;
            }
            else if (command.targetEntity != Entity.Null &&
                     em.Exists(command.targetEntity) &&
                     em.HasComponent<LocalTransform>(command.targetEntity))
            {
                float2 targetPos = em.GetComponentData<LocalTransform>(command.targetEntity).Position.xy;
                mover.targetPos = targetPos;
                mover.fightTarget = targetPos;
            }

            em.SetComponentData(ship, mover);
        }

        if (!isMovementCommand)
            return;

        // ask new flow field group now or ship keeps old one until Idle
        if (em.HasComponent<GroupManagerComponent>(ship))
        {
            em.SetComponentData(ship, new GroupManagerComponent
            {
                position = command.targetPosition,
                addOrCreateGroup = true,
                setTargetWithoutGroup = false,
                partOfSwarm = false,
                overrideSizeClass = default,
                useOverrideSizeClass = false,
            });
        }

        // drop old group link, GroupManagerSystem sets new one
        if (em.HasComponent<UnitGroup>(ship))
        {
            UnitGroup unitGroup = em.GetComponentData<UnitGroup>(ship);
            unitGroup.GroupEntity = Entity.Null;
            em.SetComponentData(ship, unitGroup);
        }
    }


    private static MovementPathDebugLogSettings GetDebugSettings(EntityManager em)
    {
        EntityQuery query = em.CreateEntityQuery(typeof(MovementPathDebugLogSettings));
        if (query.IsEmpty)
        {
            query.Dispose();
            return MovementPathDebugLogSettings.Disabled();
        }

        MovementPathDebugLogSettings settings = query.GetSingleton<MovementPathDebugLogSettings>();
        query.Dispose();
        return settings;
    }

    private bool TryRaycastUnit(EntityManager em, out Entity hitEntity)
    {
        hitEntity = Entity.Null;

        if (Camera.main == null || InputProvider.Instance == null)
        {
            return false;
        }

        Vector3 worldPointer = InputProvider.Instance.GetWorldPointerPosition();
        float2 worldPoint = new float2(worldPointer.x, worldPointer.y);

        if (!GridPickUtility.TryPickShipAtWorldPoint(em, worldPoint, out Entity hit))
        {
            return false;
        }

        if (!em.HasComponent<Unit>(hit))
        {
            return false;
        }

        hitEntity = hit;
        return true;
    }

    public void StopSelected()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;

        NativeHashSet<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        NativeArray<Entity> squads = selectedSquads.ToNativeArray(Allocator.Temp);

        NativeList<Entity> selectedPlayerGroups;
        NativeHashSet<Entity> groupClaimedSquads;
        PlayerStrikeGroupOrderUtility.CollectFullySelectedPlayerGroups(
            em,
            squads,
            Allocator.Temp,
            out selectedPlayerGroups,
            out groupClaimedSquads);

        SquadCommandElement stopCommand = new SquadCommandElement { type = SquadCommandType.Stop };

        for (int i = 0; i < selectedPlayerGroups.Length; i++)
            PlayerStrikeGroupOrderUtility.ApplySquadCommandToGroupOrder(em, selectedPlayerGroups[i], stopCommand);

        for (int i = 0; i < squads.Length; i++)
        {
            Entity squad = squads[i];
            if (groupClaimedSquads.Contains(squad))
                continue;

            QueueSquadCommand(
                em,
                squad,
                stopCommand,
                appendToQueue: false,
                debugSettings: GetDebugSettings(em));
        }

        StopLooseShips(em);

        groupClaimedSquads.Dispose();
        selectedPlayerGroups.Dispose();
        squads.Dispose();
        selectedSquads.Dispose();
    }

    public void SetFireModeOnSelected(FireMode newMode)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;

        NativeHashSet<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        NativeArray<Entity> squads = selectedSquads.ToNativeArray(Allocator.Temp);

        for (int i = 0; i < squads.Length; i++)
        {
            SquadCommandApplyUtility.ApplyNow(
                em,
                squads[i],
                new SquadCommandElement
                {
                    type = SquadCommandType.SetFireMode,
                    fireMode = newMode,
                });
        }

        SetFireModeLooseShips(em, newMode);

        squads.Dispose();
        selectedSquads.Dispose();
    }

    public void SetMoveModeOnSelected(MoveMode newMode)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;
        MovementPathDebugLogSettings debugSettings = GetDebugSettings(em);

        NativeHashSet<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        NativeArray<Entity> squads = selectedSquads.ToNativeArray(Allocator.Temp);

        NativeList<Entity> selectedPlayerGroups;
        NativeHashSet<Entity> groupClaimedSquads;
        PlayerStrikeGroupOrderUtility.CollectFullySelectedPlayerGroups(
            em,
            squads,
            Allocator.Temp,
            out selectedPlayerGroups,
            out groupClaimedSquads);

        SquadCommandElement command = new SquadCommandElement
        {
            type = SquadCommandType.SetMoveMode,
            moveMode = newMode,
        };

        for (int i = 0; i < selectedPlayerGroups.Length; i++)
            PlayerStrikeGroupOrderUtility.ApplySquadCommandToGroupOrder(em, selectedPlayerGroups[i], command);

        for (int i = 0; i < squads.Length; i++)
        {
            Entity squad = squads[i];
            if (groupClaimedSquads.Contains(squad))
                continue;

            QueueSquadCommand(
                em,
                squad,
                command,
                appendToQueue: false,
                debugSettings: debugSettings);
        }

        SetMoveModeLooseShips(em, newMode);

        groupClaimedSquads.Dispose();
        selectedPlayerGroups.Dispose();
        squads.Dispose();
        selectedSquads.Dispose();
    }

    public void CycleMoveModeOnSelected()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager em = world.EntityManager;
        MovementPathDebugLogSettings debugSettings = GetDebugSettings(em);

        NativeHashSet<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        NativeArray<Entity> squads = selectedSquads.ToNativeArray(Allocator.Temp);

        for (int i = 0; i < squads.Length; i++)
        {
            Entity squadEntity = squads[i];
            if (!em.Exists(squadEntity) || !em.HasComponent<SquadComponent>(squadEntity))
                continue;

            SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);

            if (squad.origin == SquadOrigin.Carrier)
                continue;

            MoveMode newMode = GetNextMoveMode(squad.defaultMoveMode);

            QueueSquadCommand(
                em,
                squadEntity,
                new SquadCommandElement
                {
                    type = SquadCommandType.SetMoveMode,
                    moveMode = newMode,
                },
                appendToQueue: false,
                debugSettings: debugSettings);
        }

        CycleMoveModeLooseShips(em);

        squads.Dispose();
        selectedSquads.Dispose();
    }

    private static void ApplyFireModeToLooseShips(EntityManager em)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .WithPresent<ShipStateComponent>()
            .WithAbsent<ShipSquadRef>()
            .Build(em);

        NativeArray<Entity> ships = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < ships.Length; i++)
        {
            if (!em.IsComponentEnabled<Selected>(ships[i]))
                continue;

            ShipStateComponent s = em.GetComponentData<ShipStateComponent>(ships[i]);
            s.mode = GetNextFireMode(s.mode);
            em.SetComponentData(ships[i], s);
        }

        ships.Dispose();
        query.Dispose();
    }

    private static void SetFireModeLooseShips(EntityManager em, FireMode mode)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .WithPresent<ShipStateComponent>()
            .WithAbsent<ShipSquadRef>()
            .Build(em);

        NativeArray<Entity> ships = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < ships.Length; i++)
        {
            if (!em.IsComponentEnabled<Selected>(ships[i]))
                continue;

            ShipStateComponent s = em.GetComponentData<ShipStateComponent>(ships[i]);
            s.mode = mode;
            em.SetComponentData(ships[i], s);
        }

        ships.Dispose();
        query.Dispose();
    }

    private static void SetMoveModeLooseShips(EntityManager em, MoveMode mode)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .WithPresent<ShipStateComponent>()
            .WithAbsent<ShipSquadRef>()
            .Build(em);

        NativeArray<Entity> ships = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < ships.Length; i++)
        {
            if (!em.IsComponentEnabled<Selected>(ships[i]))
                continue;

            ApplyMoveModeToLooseShip(em, ships[i], mode);
        }

        ships.Dispose();
        query.Dispose();
    }

    private static void CycleMoveModeLooseShips(EntityManager em)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .WithPresent<ShipStateComponent>()
            .WithAbsent<ShipSquadRef>()
            .Build(em);

        NativeArray<Entity> ships = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < ships.Length; i++)
        {
            if (!em.IsComponentEnabled<Selected>(ships[i]))
                continue;

            ShipStateComponent s = em.GetComponentData<ShipStateComponent>(ships[i]);
            ApplyMoveModeToLooseShip(em, ships[i], MoveModeCombatRules.GetNextMoveMode(s.moveMode));
        }

        ships.Dispose();
        query.Dispose();
    }

    private static void ApplyMoveModeToLooseShip(EntityManager em, Entity ship, MoveMode mode)
    {
        if (ship == Entity.Null ||
            !em.Exists(ship) ||
            !em.HasComponent<ShipStateComponent>(ship))
        {
            return;
        }

        ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(ship);
        ShipState oldState = shipState.currentState;
        shipState.moveMode = mode;
        shipState.forcedTarget = Entity.Null;

        if (mode == MoveMode.HoldPosition && shipState.currentState == ShipState.InCombat)
        {
            shipState.previousState = oldState;
            shipState.currentState = ShipState.Idle;

            float2 pos = float2.zero;
            bool hasPosition = em.HasComponent<LocalTransform>(ship);
            if (hasPosition)
                pos = em.GetComponentData<LocalTransform>(ship).Position.xy;

            if (hasPosition && em.HasComponent<UnitMover>(ship))
            {
                UnitMover mover = em.GetComponentData<UnitMover>(ship);
                mover.targetPos = pos;
                mover.fightTarget = pos;
                em.SetComponentData(ship, mover);
            }

            if (hasPosition)
                SquadCommandApplyUtility.ClearGroupManagerRequest(em, ship, pos);

            SquadCommandApplyUtility.ResetUnitGroup(em, ship);
            SquadCommandApplyUtility.StopVelocity(em, ship);
        }

        em.SetComponentData(ship, shipState);
    }

    private static void StopLooseShips(EntityManager em)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, CommandQueueElement>()
            .WithPresent<ShipStateComponent, UnitMover, GroupManagerComponent>()
            .WithAbsent<ShipSquadRef>()
            .Build(em);

        NativeArray<Entity> ships = query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < ships.Length; i++)
        {
            Entity ship = ships[i];

            if (!em.IsComponentEnabled<Selected>(ship))
                continue;

            em.GetBuffer<CommandQueueElement>(ship).Clear();

            ShipStateComponent s = em.GetComponentData<ShipStateComponent>(ship);
            s.previousState = s.currentState;
            s.currentState = ShipState.Idle;
            s.forcedTarget = Entity.Null;
            em.SetComponentData(ship, s);

            float2 pos = float2.zero;
            bool hasPosition = em.HasComponent<LocalTransform>(ship);
            if (hasPosition)
                pos = em.GetComponentData<LocalTransform>(ship).Position.xy;

            if (hasPosition && em.HasComponent<UnitMover>(ship))
            {
                UnitMover mover = em.GetComponentData<UnitMover>(ship);
                mover.targetPos = pos;
                mover.fightTarget = pos;
                em.SetComponentData(ship, mover);
            }

            if (hasPosition)
                SquadCommandApplyUtility.ClearGroupManagerRequest(em, ship, pos);

            SquadCommandApplyUtility.ResetUnitGroup(em, ship);
            SquadCommandApplyUtility.StopVelocity(em, ship);
        }

        ships.Dispose();
        query.Dispose();
    }

    public static FireMode GetNextFireMode(FireMode currentMode)
    {
        return currentMode switch
        {
            FireMode.FireAtWill => FireMode.ReturnFire,
            FireMode.ReturnFire => FireMode.HoldFire,
            FireMode.HoldFire => FireMode.FireAtWill,
            _ => FireMode.FireAtWill,
        };
    }

    public static MoveMode GetNextMoveMode(MoveMode currentMode)
    {
        return MoveModeCombatRules.GetNextMoveMode(currentMode);
    }
}