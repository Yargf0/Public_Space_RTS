using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// config helper for spawners, level requests and production
// squad is anchor / formation / priority coordinator
public static class SquadConfigurator
{
    public struct Params
    {
        public Faction faction;
        public float2 spawnPos;
        public float2 targetPos;

        public ShipState initialState;
        public MoveMode initialMoveMode;
        public FireMode initialFireMode;
        public Tactics initialTactics;

        public SquadRole squadRole;

        public bool createOrJoinStrikeGroup;
        public int groupId;
        public Entity targetStrikeGroupEntity;
        public StrikeGroupOwnership groupOwnership;
        public Entity ownerEntity;
    }

    public static FormationType GetFormationByRole(SquadRole role)
    {
        return role switch
        {
            SquadRole.Interceptor => FormationType.Wedge,
            SquadRole.Skirmisher => FormationType.Line,
            SquadRole.Assault => FormationType.Column,
            SquadRole.Escort => FormationType.Ring,
            _ => FormationType.Wedge,
        };
    }

    public static float GetSpacingByRole(SquadRole role)
    {
        return role switch
        {
            SquadRole.Interceptor => 2.4f,
            SquadRole.Skirmisher => 3.2f,
            SquadRole.Assault => 2.2f,
            SquadRole.Escort => 2.8f,
            _ => 2.5f,
        };
    }

    public static void Configure(EntityManager em, Entity squadEntity, in Params p)
    {
        if (squadEntity == Entity.Null || !em.Exists(squadEntity) || !em.HasComponent<SquadComponent>(squadEntity))
            return;

        SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);
        squad.faction = p.faction;
        squad.role = p.squadRole;
        squad.anchorEntity = Entity.Null;
        squad.anchorPosition = p.targetPos;
        squad.priorityTarget = Entity.Null;
        squad.defaultMoveMode = p.initialMoveMode;
        squad.defaultFireMode = p.initialFireMode;
        squad.tactics = p.initialTactics;
        squad.currentStance = Stance.HoldPosition;
        squad.lastGroupOrderVersion = 0;
        em.SetComponentData(squadEntity, squad);

        if (em.HasComponent<LocalTransform>(squadEntity))
        {
            LocalTransform transform = em.GetComponentData<LocalTransform>(squadEntity);
            transform.Position = new float3(p.targetPos.x, p.targetPos.y, transform.Position.z);
            em.SetComponentData(squadEntity, transform);
        }

        ApplyInitialState(em, squadEntity, p.targetPos, p.initialState, p.initialMoveMode, p.initialFireMode);

        if (em.HasBuffer<SquadMember>(squadEntity))
        {
            DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
            for (int i = 0; i < members.Length; i++)
            {
                Entity ship = members[i].ship;
                if (ship != Entity.Null && em.Exists(ship))
                    ApplyInitialState(em, ship, p.targetPos, p.initialState, p.initialMoveMode, p.initialFireMode);
            }
        }

        if (p.targetStrikeGroupEntity != Entity.Null && em.Exists(p.targetStrikeGroupEntity) && em.HasComponent<StrikeGroupTag>(p.targetStrikeGroupEntity))
        {
            AttachSquadToStrikeGroup(em, p.targetStrikeGroupEntity, squadEntity);
        }
        else if (p.createOrJoinStrikeGroup)
        {
            Entity groupEntity = GetOrCreateStrikeGroup(
                em,
                p.faction,
                p.groupId,
                p.initialTactics,
                p.spawnPos,
                p.groupOwnership,
                p.ownerEntity);

            AttachSquadToStrikeGroup(em, groupEntity, squadEntity);
        }
    }

    public static void ApplyInitialState(EntityManager em, Entity entity, float2 targetPos, ShipState initialState, MoveMode initialMoveMode, FireMode initialFireMode)
    {
        if (em.HasComponent<UnitMover>(entity))
        {
            UnitMover mover = em.GetComponentData<UnitMover>(entity);
            mover.targetPos = targetPos;
            mover.fightTarget = targetPos;
            em.SetComponentData(entity, mover);
        }

        if (em.HasComponent<ShipStateComponent>(entity))
        {
            ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(entity);
            shipState.previousState = shipState.currentState;
            shipState.currentState = initialState;
            shipState.moveMode = initialMoveMode;
            shipState.mode = initialFireMode;
            if (initialState != ShipState.InCombat && initialState != ShipState.Following)
                shipState.forcedTarget = Entity.Null;
            em.SetComponentData(entity, shipState);
        }
    }

    public static Entity GetOrCreateStrikeGroup(
        EntityManager em,
        Faction faction,
        int groupId,
        Tactics tactics,
        float2 spawnPosition,
        StrikeGroupOwnership ownership,
        Entity ownerEntity)
    {
        Entity found = StrikeGroupLookup.FindByKey(em, groupId, faction, ownership, ownerEntity);
        if (found != Entity.Null)
            return found;

        return CreateStrikeGroup(em, faction, groupId, tactics, spawnPosition, ownership, ownerEntity);
    }

    public static Entity CreateStrikeGroup(
        EntityManager em,
        Faction faction,
        int groupId,
        Tactics tactics,
        float2 spawnPosition,
        StrikeGroupOwnership ownership,
        Entity ownerEntity)
    {
        Entity groupEntity = em.CreateEntity();
        em.AddComponentData(groupEntity, new StrikeGroupTag());
        em.AddComponentData(groupEntity, LocalTransform.FromPosition(new float3(spawnPosition.x, spawnPosition.y, GameConstants.SquadAnchorZ)));
        em.AddComponentData(groupEntity, new StrikeGroupData
        {
            groupId = groupId,
            faction = faction,
            ownership = ownership,
            ownerEntity = ownerEntity,
            tactics = tactics,
            center = spawnPosition,
            readiness = 1f,
            activeSquadCount = 0,
            aliveMemberCount = 0,
            totalMemberCount = 0,
            summaryTimer = 0f,
        });
        em.AddComponentData(groupEntity, new StrikeGroupOrder
        {
            stance = Stance.HoldPosition,
            targetEntity = Entity.Null,
            targetPosition = spawnPosition,
            radius = 20f,
            version = 1,
        });
        em.AddComponentData(groupEntity, new StrikeGroupOrderRuntime
        {
            appliedVersion = 0,
        });
        em.AddBuffer<StrikeGroupSquadElement>(groupEntity);
        return groupEntity;
    }

    public static void AttachSquadToStrikeGroup(EntityManager em, Entity groupEntity, Entity squadEntity)
    {
        if (groupEntity == Entity.Null || squadEntity == Entity.Null) return;
        if (!em.Exists(groupEntity) || !em.Exists(squadEntity)) return;
        if (!em.HasBuffer<StrikeGroupSquadElement>(groupEntity) || !em.HasComponent<StrikeGroupData>(groupEntity)) return;

        if (em.HasComponent<StrikeGroupMember>(squadEntity))
        {
            StrikeGroupMember prev = em.GetComponentData<StrikeGroupMember>(squadEntity);
            if (prev.groupEntity != Entity.Null && prev.groupEntity != groupEntity && em.Exists(prev.groupEntity) && em.HasBuffer<StrikeGroupSquadElement>(prev.groupEntity))
            {
                DynamicBuffer<StrikeGroupSquadElement> oldSquads = em.GetBuffer<StrikeGroupSquadElement>(prev.groupEntity);
                for (int i = oldSquads.Length - 1; i >= 0; i--)
                {
                    if (oldSquads[i].squadEntity == squadEntity)
                        oldSquads.RemoveAt(i);
                }
            }
        }

        DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(groupEntity);
        bool alreadyMember = false;
        for (int i = 0; i < squads.Length; i++)
        {
            if (squads[i].squadEntity == squadEntity)
            {
                alreadyMember = true;
                break;
            }
        }

        if (!alreadyMember)
            squads.Add(new StrikeGroupSquadElement { squadEntity = squadEntity });

        StrikeGroupData data = em.GetComponentData<StrikeGroupData>(groupEntity);
        AddOrSetComponent(em, squadEntity, new StrikeGroupMember
        {
            groupEntity = groupEntity,
            groupId = data.groupId,
        });

        if (em.HasComponent<SquadComponent>(squadEntity))
        {
            SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);
            squad.tactics = data.tactics;
            em.SetComponentData(squadEntity, squad);
        }

        if (em.HasComponent<StrikeGroupOrderRuntime>(groupEntity))
        {
            StrikeGroupOrderRuntime runtime = em.GetComponentData<StrikeGroupOrderRuntime>(groupEntity);
            runtime.appliedVersion = 0;
            em.SetComponentData(groupEntity, runtime);
        }
    }

    public static void DetachSquadFromStrikeGroup(EntityManager em, Entity squadEntity)
    {
        if (squadEntity == Entity.Null || !em.Exists(squadEntity) || !em.HasComponent<StrikeGroupMember>(squadEntity))
            return;

        StrikeGroupMember member = em.GetComponentData<StrikeGroupMember>(squadEntity);
        if (member.groupEntity != Entity.Null && em.Exists(member.groupEntity) && em.HasBuffer<StrikeGroupSquadElement>(member.groupEntity))
        {
            DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(member.groupEntity);
            for (int i = squads.Length - 1; i >= 0; i--)
            {
                if (squads[i].squadEntity == squadEntity)
                    squads.RemoveAt(i);
            }
        }

        em.RemoveComponent<StrikeGroupMember>(squadEntity);
    }

    public static void AddOrSetComponent<T>(EntityManager em, Entity entity, in T data)
        where T : unmanaged, IComponentData
    {
        if (em.HasComponent<T>(entity))
            em.SetComponentData(entity, data);
        else
            em.AddComponentData(entity, data);
    }
}
