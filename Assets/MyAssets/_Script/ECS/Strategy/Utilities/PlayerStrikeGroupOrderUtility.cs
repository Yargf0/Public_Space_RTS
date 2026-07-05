using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class PlayerStrikeGroupOrderUtility
{
    public static void CollectFullySelectedPlayerGroups(
        EntityManager em,
        NativeArray<Entity> selectedSquads,
        Allocator allocator,
        out NativeList<Entity> playerGroups,
        out NativeHashSet<Entity> claimedSquads)
    {
        playerGroups = new NativeList<Entity>(allocator);
        claimedSquads = new NativeHashSet<Entity>(selectedSquads.Length, allocator);

        NativeList<Entity> candidates = new NativeList<Entity>(allocator);
        NativeList<int> selectedCounts = new NativeList<int>(allocator);

        for (int i = 0; i < selectedSquads.Length; i++)
        {
            Entity squad = selectedSquads[i];
            if (!TryGetPlayerGroup(em, squad, out Entity group))
                continue;

            int index = IndexOf(candidates, group);
            if (index < 0)
            {
                candidates.Add(group);
                selectedCounts.Add(1);
            }
            else
            {
                selectedCounts[index] = selectedCounts[index] + 1;
            }
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            Entity group = candidates[i];
            int totalGroupSquads = CleanupAndCountGroupSquads(em, group);
            if (totalGroupSquads <= 0)
                continue;

            // group order only when whole group selected, partial goes per squad
            if (selectedCounts[i] < totalGroupSquads)
                continue;

            playerGroups.Add(group);

            DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(group);
            for (int s = 0; s < squads.Length; s++)
            {
                Entity squad = squads[s].squadEntity;
                if (squad != Entity.Null && em.Exists(squad))
                    claimedSquads.Add(squad);
            }
        }

        selectedCounts.Dispose();
        candidates.Dispose();
    }

    public static bool ApplySquadCommandToGroupOrder(EntityManager em, Entity group, SquadCommandElement command, float fallbackRadius = 24f)
    {
        if (!IsValidPlayerGroup(em, group) || !em.HasComponent<StrikeGroupOrder>(group))
            return false;

        StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(group);

        switch (command.type)
        {
            case SquadCommandType.MoveToPoint:
                order.stance = Stance.MoveTo;
                order.targetEntity = Entity.Null;
                order.targetPosition = command.targetPosition;
                break;

            case SquadCommandType.AttackMoveToPoint:
                order.stance = Stance.AttackMove;
                order.targetEntity = Entity.Null;
                order.targetPosition = command.targetPosition;
                break;

            case SquadCommandType.AttackTarget:
                order.stance = Stance.AttackMove;
                order.targetEntity = command.targetEntity;
                order.targetPosition = ResolveTargetPosition(em, command.targetEntity, command.targetPosition);
                break;

            case SquadCommandType.FollowEntity:
                order.stance = Stance.Guard;
                order.targetEntity = command.targetEntity;
                order.targetPosition = ResolveTargetPosition(em, command.targetEntity, command.targetPosition);
                break;

            case SquadCommandType.Stop:
                order.stance = Stance.HoldPosition;
                order.targetEntity = Entity.Null;
                order.targetPosition = ResolveGroupCenter(em, group, order.targetPosition);
                break;

            case SquadCommandType.SetMoveMode:
                order.stance = ResolveStance(command.moveMode);
                order.targetEntity = Entity.Null;
                order.targetPosition = ResolveGroupCenter(em, group, order.targetPosition);
                break;

            case SquadCommandType.SetFireMode:
            default:
                return false;
        }

        if (order.radius <= 0f)
            order.radius = fallbackRadius;

        order.version = order.version == uint.MaxValue ? 1u : order.version + 1u;
        em.SetComponentData(group, order);

        if (em.HasComponent<StrikeGroupOrderRuntime>(group))
        {
            StrikeGroupOrderRuntime runtime = em.GetComponentData<StrikeGroupOrderRuntime>(group);
            runtime.appliedVersion = 0;
            em.SetComponentData(group, runtime);
        }

        ClearSquadCommandQueues(em, group);
        return true;
    }

    public static bool TrySetFireModeOnGroupSquads(EntityManager em, Entity group, FireMode mode)
    {
        if (!IsValidPlayerGroup(em, group) || !em.HasBuffer<StrikeGroupSquadElement>(group))
            return false;

        CleanupAndCountGroupSquads(em, group);
        DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(group);

        bool applied = false;
        for (int i = 0; i < squads.Length; i++)
        {
            Entity squad = squads[i].squadEntity;
            if (squad == Entity.Null || !em.Exists(squad) || !em.HasComponent<SquadComponent>(squad))
                continue;

            SquadCommandApplyUtility.ApplyNow(
                em,
                squad,
                new SquadCommandElement
                {
                    type = SquadCommandType.SetFireMode,
                    fireMode = mode,
                });

            applied = true;
        }

        return applied;
    }

    private static bool TryGetPlayerGroup(EntityManager em, Entity squad, out Entity group)
    {
        group = Entity.Null;

        if (squad == Entity.Null || !em.Exists(squad) || !em.HasComponent<StrikeGroupMember>(squad))
            return false;

        StrikeGroupMember member = em.GetComponentData<StrikeGroupMember>(squad);
        if (!IsValidPlayerGroup(em, member.groupEntity))
            return false;

        group = member.groupEntity;
        return true;
    }

    private static bool IsValidPlayerGroup(EntityManager em, Entity group)
    {
        if (group == Entity.Null || !em.Exists(group) || !em.HasComponent<StrikeGroupData>(group))
            return false;

        StrikeGroupData data = em.GetComponentData<StrikeGroupData>(group);
        return data.ownership == StrikeGroupOwnership.Player;
    }

    private static int CleanupAndCountGroupSquads(EntityManager em, Entity group)
    {
        if (!IsValidPlayerGroup(em, group) || !em.HasBuffer<StrikeGroupSquadElement>(group))
            return 0;

        DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(group);
        for (int i = squads.Length - 1; i >= 0; i--)
        {
            Entity squad = squads[i].squadEntity;
            if (squad == Entity.Null || !em.Exists(squad) || !em.HasComponent<SquadComponent>(squad))
                squads.RemoveAt(i);
        }

        return squads.Length;
    }

    private static void ClearSquadCommandQueues(EntityManager em, Entity group)
    {
        if (!IsValidPlayerGroup(em, group) || !em.HasBuffer<StrikeGroupSquadElement>(group))
            return;

        DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(group);
        for (int i = 0; i < squads.Length; i++)
        {
            Entity squad = squads[i].squadEntity;
            if (squad == Entity.Null || !em.Exists(squad) || !em.HasBuffer<SquadCommandElement>(squad))
                continue;

            em.GetBuffer<SquadCommandElement>(squad).Clear();
        }
    }

    private static float2 ResolveTargetPosition(EntityManager em, Entity targetEntity, float2 fallback)
    {
        if (targetEntity != Entity.Null && em.Exists(targetEntity) && em.HasComponent<LocalTransform>(targetEntity))
            return em.GetComponentData<LocalTransform>(targetEntity).Position.xy;

        return fallback;
    }

    private static float2 ResolveGroupCenter(EntityManager em, Entity group, float2 fallback)
    {
        if (group != Entity.Null && em.Exists(group) && em.HasComponent<StrikeGroupData>(group))
            return em.GetComponentData<StrikeGroupData>(group).center;

        if (group != Entity.Null && em.Exists(group) && em.HasComponent<LocalTransform>(group))
            return em.GetComponentData<LocalTransform>(group).Position.xy;

        return fallback;
    }

    private static Stance ResolveStance(MoveMode mode)
    {
        switch (mode)
        {
            case MoveMode.AttackMove:
                return Stance.AttackMove;
            case MoveMode.MoveAndEngage:
                return Stance.MoveTo;
            case MoveMode.HoldPosition:
            default:
                return Stance.HoldPosition;
        }
    }

    private static int IndexOf(NativeList<Entity> list, Entity value)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == value)
                return i;
        }

        return -1;
    }
}
