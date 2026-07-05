using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class SquadCommandApplyUtility
{
    private const float PositionChangeEpsilonSq = 0.0001f;
    private const float MovingAnchorRepathSpacingMultiplier = 0.5f;
    private const float MovingAnchorRepathMinDistance = 1.5f;
    private const float MovingAnchorRepathMaxDistance = 4f;

    public static SquadCommandElement ResolveMoveMode(in SquadComponent squad, SquadCommandElement command)
    {
        switch (command.type)
        {
            case SquadCommandType.AttackMoveToPoint:
            case SquadCommandType.AttackTarget:
                command.moveMode = MoveMode.AttackMove;
                break;

            case SquadCommandType.MoveToPoint:
            case SquadCommandType.FollowEntity:
            case SquadCommandType.SetMoveMode:
                // moveMode is set by UI / BuildSquadCommand, don't change it here
                break;
        }

        return command;
    }

    public static bool CleanupInvalidReferences(EntityManager em, ref SquadComponent squad)
    {
        return CleanupInvalidReferencesCore(em, ref squad, out _);
    }

    public static bool CleanupInvalidReferences(EntityManager em, DynamicBuffer<SquadMember> members, ref SquadComponent squad)
    {
        Entity clearedPriorityTarget;
        bool changed = CleanupInvalidReferencesCore(em, ref squad, out clearedPriorityTarget);

        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null || !em.Exists(ship))
                continue;

            if (em.HasComponent<ShipStateComponent>(ship))
            {
                ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(ship);
                bool stateChanged = false;

                if (shipState.forcedTarget != Entity.Null && !IsValidTargetEntity(em, shipState.forcedTarget))
                {
                    shipState.forcedTarget = Entity.Null;
                    stateChanged = true;
                }

                if (clearedPriorityTarget != Entity.Null && shipState.forcedTarget == clearedPriorityTarget)
                {
                    shipState.forcedTarget = Entity.Null;
                    stateChanged = true;
                }

                if (stateChanged)
                {
                    em.SetComponentData(ship, shipState);
                    changed = true;
                }
            }

            if (em.HasComponent<ShipPriorityHint>(ship))
            {
                ShipPriorityHint hint = em.GetComponentData<ShipPriorityHint>(ship);
                bool hintChanged = false;

                if (hint.target != Entity.Null && !IsValidTargetEntity(em, hint.target))
                {
                    hint.target = Entity.Null;
                    hintChanged = true;
                }

                if (clearedPriorityTarget != Entity.Null && hint.target == clearedPriorityTarget)
                {
                    hint.target = Entity.Null;
                    hintChanged = true;
                }

                if (hintChanged)
                {
                    em.SetComponentData(ship, hint);
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool CleanupInvalidReferencesCore(
        EntityManager em,
        ref SquadComponent squad,
        out Entity clearedPriorityTarget)
    {
        bool changed = false;
        clearedPriorityTarget = Entity.Null;

        if (squad.priorityTarget != Entity.Null && !IsValidTargetEntity(em, squad.priorityTarget))
        {
            clearedPriorityTarget = squad.priorityTarget;

            if (squad.anchorEntity == squad.priorityTarget)
                squad.anchorEntity = Entity.Null;

            squad.priorityTarget = Entity.Null;
            changed = true;
        }

        if (squad.anchorEntity != Entity.Null && !IsValidTargetEntity(em, squad.anchorEntity))
        {
            squad.anchorEntity = Entity.Null;
            changed = true;
        }

        return changed;
    }

    // applies one command right now. doesn't clear queue, caller must do it
    // SetFireMode can call this directly, fire mode don't cancel movement
    public static void ApplyNow(EntityManager em, Entity squadEntity, SquadCommandElement command)
    {
        if (squadEntity == Entity.Null ||
            !em.Exists(squadEntity) ||
            !em.HasComponent<SquadComponent>(squadEntity))
        {
            return;
        }

        SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);

        if (squad.origin == SquadOrigin.Carrier)
            return;

        command = ResolveMoveMode(squad, command);

        if (!em.HasBuffer<SquadMember>(squadEntity))
        {
            CleanupInvalidReferences(em, ref squad);
            ApplyToSquadWithoutMembers(em, ref squad, command, out _, 0f);
            em.SetComponentData(squadEntity, squad);
            return;
        }

        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
        CleanupInvalidReferences(em, members, ref squad);
        ApplyToSquad(em, members, ref squad, command, out _, 0f);
        em.SetComponentData(squadEntity, squad);
        ApplyToMembers(em, squadEntity, members, squad, command);
    }

    public static bool ApplyToSquad(
        EntityManager em,
        DynamicBuffer<SquadMember> members,
        ref SquadComponent squad,
        SquadCommandElement command,
        out bool commandFinished,
        float reachedDistanceSq)
    {
        float2 averagePosition = CalculateAveragePosition(em, members, squad.anchorPosition);
        return ApplyToSquadCore(em, averagePosition, ref squad, command, out commandFinished, reachedDistanceSq);
    }

    private static bool ApplyToSquadWithoutMembers(
        EntityManager em,
        ref SquadComponent squad,
        SquadCommandElement command,
        out bool commandFinished,
        float reachedDistanceSq)
    {
        return ApplyToSquadCore(em, squad.anchorPosition, ref squad, command, out commandFinished, reachedDistanceSq);
    }

    private static bool ApplyToSquadCore(
        EntityManager em,
        float2 averagePosition,
        ref SquadComponent squad,
        SquadCommandElement command,
        out bool commandFinished,
        float reachedDistanceSq)
    {
        commandFinished = false;

        switch (command.type)
        {
            case SquadCommandType.MoveToPoint:
                squad.anchorEntity = Entity.Null;
                squad.anchorPosition = command.targetPosition;
                squad.priorityTarget = Entity.Null;
                squad.defaultMoveMode = command.moveMode;
                commandFinished = IsNear(averagePosition, command.targetPosition, reachedDistanceSq);
                break;

            case SquadCommandType.AttackMoveToPoint:
                squad.anchorEntity = Entity.Null;
                squad.anchorPosition = command.targetPosition;
                squad.priorityTarget = Entity.Null;
                squad.defaultMoveMode = MoveMode.AttackMove;
                commandFinished = IsNear(averagePosition, command.targetPosition, reachedDistanceSq);
                break;

            case SquadCommandType.AttackTarget:
                if (command.targetEntity == Entity.Null || !IsValidTargetEntity(em, command.targetEntity))
                {
                    squad.priorityTarget = Entity.Null;
                    squad.anchorEntity = Entity.Null;
                    commandFinished = true;
                    break;
                }

                squad.priorityTarget = command.targetEntity;
                squad.anchorEntity = command.targetEntity;
                squad.defaultMoveMode = MoveMode.AttackMove;
                squad.anchorPosition = GetEntityPositionOrFallback(em, command.targetEntity, command.targetPosition);

                // applied once, then SquadDefaultsSystem tracks target
                commandFinished = true;
                break;

            case SquadCommandType.FollowEntity:
                if (command.targetEntity == Entity.Null || !IsValidTargetEntity(em, command.targetEntity))
                {
                    squad.priorityTarget = Entity.Null;
                    squad.anchorEntity = Entity.Null;
                    commandFinished = true;
                    break;
                }

                squad.priorityTarget = Entity.Null;
                squad.anchorEntity = command.targetEntity;
                squad.defaultMoveMode = command.moveMode;
                squad.anchorPosition = GetEntityPositionOrFallback(em, command.targetEntity, command.targetPosition);

                // applied once, then SquadDefaultsSystem follows anchorEntity
                commandFinished = true;
                break;

            case SquadCommandType.Stop:
                squad.priorityTarget = Entity.Null;
                squad.anchorEntity = Entity.Null;
                squad.anchorPosition = averagePosition;
                squad.defaultMoveMode = MoveMode.HoldPosition;
                commandFinished = true;
                break;

            case SquadCommandType.SetFireMode:
                squad.defaultFireMode = command.fireMode;
                commandFinished = true;
                break;

            case SquadCommandType.SetMoveMode:
                squad.defaultMoveMode = command.moveMode;

                if (command.moveMode == MoveMode.HoldPosition)
                {
                    // not a Stop, squad keeps formation around current center
                    squad.priorityTarget = Entity.Null;
                    squad.anchorEntity = Entity.Null;
                    squad.anchorPosition = averagePosition;
                }
                else
                {
                    // player override, drops old AttackTarget/Follow, keeps destination
                    if (squad.anchorEntity != Entity.Null && IsValidTargetEntity(em, squad.anchorEntity))
                        squad.anchorPosition = GetEntityPositionOrFallback(em, squad.anchorEntity, squad.anchorPosition);

                    squad.priorityTarget = Entity.Null;
                    squad.anchorEntity = Entity.Null;
                }

                commandFinished = true;
                break;
        }

        return ShouldApplyToMembersForCommand(command.type);
    }

    public static bool ShouldApplyToMembersAfterApply(
        EntityManager em,
        in SquadComponent before,
        in SquadComponent after,
        in SquadCommandElement command)
    {
        float movingAnchorRepathDistanceSq = GetMovingAnchorRepathDistanceSq(after);

        switch (command.type)
        {
            case SquadCommandType.AttackTarget:
                return command.targetEntity != Entity.Null &&
                       IsValidTargetEntity(em, command.targetEntity) &&
                       (before.priorityTarget != after.priorityTarget ||
                        before.anchorEntity != after.anchorEntity ||
                        before.defaultMoveMode != after.defaultMoveMode ||
                        math.distancesq(before.anchorPosition, after.anchorPosition) > movingAnchorRepathDistanceSq);

            case SquadCommandType.FollowEntity:
                return command.targetEntity != Entity.Null &&
                       IsValidTargetEntity(em, command.targetEntity) &&
                       (before.priorityTarget != after.priorityTarget ||
                        before.anchorEntity != after.anchorEntity ||
                        before.defaultMoveMode != after.defaultMoveMode ||
                        math.distancesq(before.anchorPosition, after.anchorPosition) > movingAnchorRepathDistanceSq);

            case SquadCommandType.MoveToPoint:
            case SquadCommandType.AttackMoveToPoint:
                return before.anchorEntity != after.anchorEntity ||
                       before.priorityTarget != after.priorityTarget ||
                       before.defaultMoveMode != after.defaultMoveMode ||
                       math.distancesq(before.anchorPosition, after.anchorPosition) > PositionChangeEpsilonSq;

            case SquadCommandType.Stop:
            case SquadCommandType.SetFireMode:
            case SquadCommandType.SetMoveMode:
                return true;

            default:
                return false;
        }
    }

    public static void ApplyToMembers(
        EntityManager em,
        Entity squadEntity,
        DynamicBuffer<SquadMember> members,
        in SquadComponent squad,
        SquadCommandElement command)
    {
        if (command.type == SquadCommandType.SetFireMode)
        {
            ApplyFireModeToMembers(em, members, squad.defaultFireMode);
            return;
        }

        if (!IsMovementOverrideCommand(command.type))
            return;

        SquadCommandType commandType = command.type;
        bool isStop = commandType == SquadCommandType.Stop;
        bool isSetMoveMode = commandType == SquadCommandType.SetMoveMode;
        bool isAttackTarget = commandType == SquadCommandType.AttackTarget &&
                              command.targetEntity != Entity.Null &&
                              IsValidTargetEntity(em, command.targetEntity);
        bool forceHoldPosition = isSetMoveMode && squad.defaultMoveMode == MoveMode.HoldPosition;
        bool requestFlowField = ShouldRequestFlowField(commandType) || isSetMoveMode;
        float2 flowFieldTarget = GetFlowFieldTarget(em, squad);
        PathfindingSizeClass squadPathSize = UpdateSquadPathSizeClass(em, squadEntity, members);

        for (int i = 0; i < members.Length; i++)
        {
            SquadMember member = members[i];
            Entity ship = member.ship;

            if (ship == Entity.Null || !em.Exists(ship))
                continue;

            float2 desiredPos = GetMemberDesiredPosition(em, squad, member, members.Length);

            if (isStop && em.HasComponent<LocalTransform>(ship))
                desiredPos = em.GetComponentData<LocalTransform>(ship).Position.xy;

            if (em.HasComponent<UnitMover>(ship))
            {
                UnitMover mover = em.GetComponentData<UnitMover>(ship);
                mover.targetPos = desiredPos;
                mover.fightTarget = desiredPos;
                em.SetComponentData(ship, mover);
            }

            if (requestFlowField)
            {
                // squad shares one flow field to anchor, formation slot sits in UnitMover.targetPos
                if (em.HasComponent<GroupManagerComponent>(ship))
                {
                    em.SetComponentData(ship, new GroupManagerComponent
                    {
                        position = flowFieldTarget,
                        addOrCreateGroup = true,
                        setTargetWithoutGroup = false,
                        partOfSwarm = false,
                        overrideSizeClass = squadPathSize,
                        useOverrideSizeClass = true,
                    });
                }

                ResetUnitGroup(em, ship);
            }
            else if (isStop)
            {
                // Stop must drop flow field right away, no new request
                ClearGroupManagerRequest(em, ship, desiredPos);
                ResetUnitGroup(em, ship);
                StopVelocity(em, ship);
            }

            if (em.HasComponent<ShipStateComponent>(ship))
            {
                ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(ship);
                shipState.mode = squad.defaultFireMode;
                shipState.moveMode = squad.defaultMoveMode;
                shipState.forcedTarget = isAttackTarget ? command.targetEntity : Entity.Null;

                bool reached = isStop;
                if (!reached && em.HasComponent<LocalTransform>(ship))
                    reached = math.distancesq(em.GetComponentData<LocalTransform>(ship).Position.xy, desiredPos) <= 1.0f;

                ShipState nextState = reached ? ShipState.Idle : ShipState.MovingToTarget;
                if (isAttackTarget && shipState.currentState == ShipState.InCombat)
                    nextState = ShipState.InCombat;

                if (shipState.currentState != nextState)
                {
                    shipState.previousState = shipState.currentState;
                    shipState.currentState = nextState;
                }

                em.SetComponentData(ship, shipState);
            }

            if (em.HasComponent<ShipPriorityHint>(ship))
            {
                ShipPriorityHint hint = em.GetComponentData<ShipPriorityHint>(ship);
                hint.target = isStop ? Entity.Null : squad.priorityTarget;
                em.SetComponentData(ship, hint);
            }
        }
    }

    public static bool IsMovementOverrideCommand(SquadCommandType type)
    {
        return type == SquadCommandType.MoveToPoint ||
               type == SquadCommandType.AttackMoveToPoint ||
               type == SquadCommandType.AttackTarget ||
               type == SquadCommandType.FollowEntity ||
               type == SquadCommandType.Stop ||
               type == SquadCommandType.SetMoveMode;
    }

    public static void ClearGroupManagerRequest(EntityManager em, Entity ship, float2 position)
    {
        if (!em.HasComponent<GroupManagerComponent>(ship))
            return;

        em.SetComponentData(ship, new GroupManagerComponent
        {
            position = position,
            addOrCreateGroup = false,
            setTargetWithoutGroup = false,
            partOfSwarm = false,
            overrideSizeClass = default,
            useOverrideSizeClass = false,
        });
    }

    public static void ResetUnitGroup(EntityManager em, Entity ship)
    {
        if (!em.HasComponent<UnitGroup>(ship))
            return;

        UnitGroup unitGroup = em.GetComponentData<UnitGroup>(ship);
        unitGroup.GroupEntity = Entity.Null;
        em.SetComponentData(ship, unitGroup);
    }

    public static void StopVelocity(EntityManager em, Entity ship)
    {
        if (!em.HasComponent<Velocity>(ship))
            return;

        Velocity velocity = em.GetComponentData<Velocity>(ship);
        velocity.velocity = float2.zero;
        velocity.flowFieldVelocity = float2.zero;
        em.SetComponentData(ship, velocity);
    }



    public static PathfindingSizeClass UpdateSquadPathSizeClass(
        EntityManager em,
        Entity squadEntity,
        DynamicBuffer<SquadMember> members)
    {
        PathfindingSizeClass maxSize = PathfindingSizeClass.Small;
        bool hasAnyMember = false;

        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null || !em.Exists(ship))
                continue;

            PathfindingSizeClass shipSize = em.HasComponent<PathfindingSizeClassComponent>(ship)
                ? em.GetComponentData<PathfindingSizeClassComponent>(ship).Value
                : PathfindingSizeClass.Medium;

            if (!hasAnyMember || GetPathSizeRank(shipSize) > GetPathSizeRank(maxSize))
                maxSize = shipSize;

            hasAnyMember = true;
        }

        if (!hasAnyMember)
            maxSize = PathfindingSizeClass.Medium;

        if (squadEntity != Entity.Null && em.Exists(squadEntity))
        {
            SquadPathSizeClass data = new SquadPathSizeClass
            {
                Value = maxSize,
                Valid = hasAnyMember,
            };

            if (em.HasComponent<SquadPathSizeClass>(squadEntity))
                em.SetComponentData(squadEntity, data);
        }

        return maxSize;
    }

    private static int GetPathSizeRank(PathfindingSizeClass sizeClass)
    {
        return sizeClass switch
        {
            PathfindingSizeClass.Small => 0,
            PathfindingSizeClass.Medium => 1,
            PathfindingSizeClass.Large => 2,
            _ => 1,
        };
    }

    private static float GetMovingAnchorRepathDistanceSq(in SquadComponent squad)
    {
        float spacingBasedDistance = math.max(MovingAnchorRepathMinDistance, squad.spacing * MovingAnchorRepathSpacingMultiplier);
        float distance = math.clamp(spacingBasedDistance, MovingAnchorRepathMinDistance, MovingAnchorRepathMaxDistance);
        return distance * distance;
    }

    private static bool ShouldApplyToMembersForCommand(SquadCommandType type)
    {
        return type == SquadCommandType.MoveToPoint ||
               type == SquadCommandType.AttackMoveToPoint ||
               type == SquadCommandType.AttackTarget ||
               type == SquadCommandType.FollowEntity ||
               type == SquadCommandType.Stop ||
               type == SquadCommandType.SetFireMode ||
               type == SquadCommandType.SetMoveMode;
    }

    private static bool ShouldRequestFlowField(SquadCommandType type)
    {
        return type == SquadCommandType.MoveToPoint ||
               type == SquadCommandType.AttackMoveToPoint ||
               type == SquadCommandType.AttackTarget ||
               type == SquadCommandType.FollowEntity;
    }

    private static void ApplyFireModeToMembers(EntityManager em, DynamicBuffer<SquadMember> members, FireMode fireMode)
    {
        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null || !em.Exists(ship) || !em.HasComponent<ShipStateComponent>(ship))
                continue;

            ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(ship);
            shipState.mode = fireMode;
            em.SetComponentData(ship, shipState);
        }
    }

    private static float2 GetFlowFieldTarget(EntityManager em, in SquadComponent squad)
    {
        if (squad.anchorEntity != Entity.Null &&
            IsValidTargetEntity(em, squad.anchorEntity))
        {
            return em.GetComponentData<LocalTransform>(squad.anchorEntity).Position.xy;
        }

        return squad.anchorPosition;
    }

    private static float2 GetMemberDesiredPosition(EntityManager em, in SquadComponent squad, in SquadMember member, int formationMemberCount)
    {
        float2 anchor = squad.anchorPosition;

        if (squad.anchorEntity != Entity.Null &&
            IsValidTargetEntity(em, squad.anchorEntity))
        {
            anchor = em.GetComponentData<LocalTransform>(squad.anchorEntity).Position.xy;
        }

        float2 offset = FormationUtility.GetSlotOffset(
            squad.formation,
            member.formationSlotIndex,
            math.max(1, formationMemberCount),
            squad.spacing);

        return anchor + offset;
    }

    private static float2 CalculateAveragePosition(
        EntityManager em,
        DynamicBuffer<SquadMember> members,
        float2 fallback)
    {
        float2 sum = float2.zero;
        int count = 0;

        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null ||
                !em.Exists(ship) ||
                !em.HasComponent<LocalTransform>(ship))
            {
                continue;
            }

            sum += em.GetComponentData<LocalTransform>(ship).Position.xy;
            count++;
        }

        return count > 0 ? sum / count : fallback;
    }

    private static float2 GetEntityPositionOrFallback(EntityManager em, Entity entity, float2 fallback)
    {
        if (IsValidTargetEntity(em, entity))
            return em.GetComponentData<LocalTransform>(entity).Position.xy;

        return fallback;
    }

    private static bool IsValidTargetEntity(EntityManager em, Entity entity)
    {
        return entity != Entity.Null && em.Exists(entity) && em.HasComponent<LocalTransform>(entity);
    }

    private static bool IsNear(float2 current, float2 target, float reachedDistanceSq)
    {
        return reachedDistanceSq > 0f && math.distancesq(current, target) <= reachedDistanceSq;
    }
}
