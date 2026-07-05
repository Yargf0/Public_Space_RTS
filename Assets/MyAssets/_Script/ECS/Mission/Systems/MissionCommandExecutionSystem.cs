using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SpawnSquadRequestSystem))]
[UpdateBefore(typeof(StrikeGroupSummarySystem))]
public partial class MissionCommandExecutionSystem : SystemBase
{
    public MissionScript ActiveScript;

    private enum PendingMissionCommandKind : byte
    {
        SpawnGroup = 1,
        OrderGroup = 2,
        SetTactics = 3,
        AttackFaction = 4,
        DestroyGroup = 5,
    }

    private struct PendingMissionCommand
    {
        public PendingMissionCommandKind kind;
        public MissionSpawnGroupCommand spawnGroup;
        public MissionOrderGroupCommand orderGroup;
        public MissionSetTacticsCommand setTactics;
        public MissionAttackFactionCommand attackFaction;
        public MissionDestroyGroupCommand destroyGroup;
    }

    private readonly List<PendingMissionCommand> pendingCommands = new List<PendingMissionCommand>(128);
    private EntityQuery spawnCommandQuery;
    private EntityQuery orderCommandQuery;
    private EntityQuery setTacticsCommandQuery;
    private EntityQuery attackFactionCommandQuery;
    private EntityQuery destroyCommandQuery;

    public void Enqueue(in MissionSpawnGroupCommand command)
    {
        pendingCommands.Add(new PendingMissionCommand
        {
            kind = PendingMissionCommandKind.SpawnGroup,
            spawnGroup = command,
        });
    }

    public void Enqueue(in MissionOrderGroupCommand command)
    {
        pendingCommands.Add(new PendingMissionCommand
        {
            kind = PendingMissionCommandKind.OrderGroup,
            orderGroup = command,
        });
    }

    public void Enqueue(in MissionSetTacticsCommand command)
    {
        pendingCommands.Add(new PendingMissionCommand
        {
            kind = PendingMissionCommandKind.SetTactics,
            setTactics = command,
        });
    }

    public void Enqueue(in MissionAttackFactionCommand command)
    {
        pendingCommands.Add(new PendingMissionCommand
        {
            kind = PendingMissionCommandKind.AttackFaction,
            attackFaction = command,
        });
    }

    public void Enqueue(in MissionDestroyGroupCommand command)
    {
        pendingCommands.Add(new PendingMissionCommand
        {
            kind = PendingMissionCommandKind.DestroyGroup,
            destroyGroup = command,
        });
    }

    protected override void OnCreate()
    {
        // no RequireForUpdate here, runner queues commands before entities exist
        spawnCommandQuery = GetEntityQuery(
            ComponentType.ReadOnly<MissionCommandTag>(),
            ComponentType.ReadOnly<MissionSpawnGroupCommand>());

        orderCommandQuery = GetEntityQuery(
            ComponentType.ReadOnly<MissionCommandTag>(),
            ComponentType.ReadOnly<MissionOrderGroupCommand>());

        setTacticsCommandQuery = GetEntityQuery(
            ComponentType.ReadOnly<MissionCommandTag>(),
            ComponentType.ReadOnly<MissionSetTacticsCommand>());

        attackFactionCommandQuery = GetEntityQuery(
            ComponentType.ReadOnly<MissionCommandTag>(),
            ComponentType.ReadOnly<MissionAttackFactionCommand>());

        destroyCommandQuery = GetEntityQuery(
            ComponentType.ReadOnly<MissionCommandTag>(),
            ComponentType.ReadOnly<MissionDestroyGroupCommand>());
    }

    protected override void OnUpdate()
    {
        EntityManager em = EntityManager;

        FlushPendingCommands(em);

        ExecuteSpawnCommands(em);
        ExecuteOrderCommands(em);
        ExecuteSetTacticsCommands(em);
        ExecuteAttackFactionCommands(em);
        ExecuteDestroyCommands(em);
    }

    private void ExecuteSpawnCommands(EntityManager em)
    {
        using NativeArray<Entity> entities = spawnCommandQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<MissionSpawnGroupCommand> commands = spawnCommandQuery.ToComponentDataArray<MissionSpawnGroupCommand>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
            ExecuteSpawn(em, entities[i], commands[i]);
    }

    private void ExecuteOrderCommands(EntityManager em)
    {
        using NativeArray<Entity> entities = orderCommandQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<MissionOrderGroupCommand> commands = orderCommandQuery.ToComponentDataArray<MissionOrderGroupCommand>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
            ExecuteOrder(em, entities[i], commands[i]);
    }

    private void ExecuteSetTacticsCommands(EntityManager em)
    {
        using NativeArray<Entity> entities = setTacticsCommandQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<MissionSetTacticsCommand> commands = setTacticsCommandQuery.ToComponentDataArray<MissionSetTacticsCommand>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
            ExecuteSetTactics(em, entities[i], commands[i]);
    }

    private void ExecuteAttackFactionCommands(EntityManager em)
    {
        using NativeArray<Entity> entities = attackFactionCommandQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<MissionAttackFactionCommand> commands = attackFactionCommandQuery.ToComponentDataArray<MissionAttackFactionCommand>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
            ExecuteAttackFaction(em, entities[i], commands[i]);
    }

    private void ExecuteDestroyCommands(EntityManager em)
    {
        using NativeArray<Entity> entities = destroyCommandQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<MissionDestroyGroupCommand> commands = destroyCommandQuery.ToComponentDataArray<MissionDestroyGroupCommand>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
            ExecuteDestroy(em, entities[i], commands[i]);
    }

    private void FlushPendingCommands(EntityManager em)
    {
        if (pendingCommands.Count == 0)
            return;

        for (int i = 0; i < pendingCommands.Count; i++)
        {
            PendingMissionCommand pending = pendingCommands[i];
            switch (pending.kind)
            {
                case PendingMissionCommandKind.SpawnGroup:
                    CreateCommandEntity(em, pending.spawnGroup);
                    break;

                case PendingMissionCommandKind.OrderGroup:
                    CreateCommandEntity(em, pending.orderGroup);
                    break;

                case PendingMissionCommandKind.SetTactics:
                    CreateCommandEntity(em, pending.setTactics);
                    break;

                case PendingMissionCommandKind.AttackFaction:
                    CreateCommandEntity(em, pending.attackFaction);
                    break;

                case PendingMissionCommandKind.DestroyGroup:
                    CreateCommandEntity(em, pending.destroyGroup);
                    break;
            }
        }

        pendingCommands.Clear();
    }

    private static Entity CreateCommandEntity(EntityManager em, MissionSpawnGroupCommand command)
    {
        Entity entity = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionSpawnGroupCommand>());
        em.SetComponentData(entity, command);
        return entity;
    }

    private static Entity CreateCommandEntity(EntityManager em, MissionOrderGroupCommand command)
    {
        Entity entity = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionOrderGroupCommand>());
        em.SetComponentData(entity, command);
        return entity;
    }

    private static Entity CreateCommandEntity(EntityManager em, MissionSetTacticsCommand command)
    {
        Entity entity = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionSetTacticsCommand>());
        em.SetComponentData(entity, command);
        return entity;
    }

    private static Entity CreateCommandEntity(EntityManager em, MissionAttackFactionCommand command)
    {
        Entity entity = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionAttackFactionCommand>());
        em.SetComponentData(entity, command);
        return entity;
    }

    private static Entity CreateCommandEntity(EntityManager em, MissionDestroyGroupCommand command)
    {
        Entity entity = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionDestroyGroupCommand>());
        em.SetComponentData(entity, command);
        return entity;
    }

    private void ExecuteSpawn(EntityManager em, Entity cmd, MissionSpawnGroupCommand c)
    {
        if (ActiveScript == null || ActiveScript.spawnPresets == null)
        {
            em.DestroyEntity(cmd);
            return;
        }

        if (c.spawnPresetIndex < 0 || c.spawnPresetIndex >= ActiveScript.spawnPresets.Length)
        {
            Debug.LogWarning($"[Mission] Spawn command invalid presetIndex={c.spawnPresetIndex}");
            em.DestroyEntity(cmd);
            return;
        }

        MissionSpawnPreset preset = ActiveScript.spawnPresets[c.spawnPresetIndex];
        if (preset == null || preset.plan == null)
        {
            em.DestroyEntity(cmd);
            return;
        }

        if (!WaypointLookup.TryGetData(em, preset.spawnWaypointId, out Waypoint spawnWp))
        {
            Debug.LogWarning($"[Mission] Spawn waypoint id={preset.spawnWaypointId} not found.");
            em.DestroyEntity(cmd);
            return;
        }

        float2 targetPos = spawnWp.position;
        if (preset.targetWaypointId != 0 && WaypointLookup.TryGetData(em, preset.targetWaypointId, out Waypoint targetWp))
            targetPos = targetWp.position;

        int groupId = preset.assignGroupId != 0 ? preset.assignGroupId : LevelSpawnApi.ReserveGroupId();

        Entity groupEntity = LevelSpawnApi.RequestStrikeGroup(
            preset.plan,
            spawnWp.position,
            targetPos,
            groupId,
            preset.tactics,
            StrikeGroupOwnership.Director,
            c.directorEntity);

        if (groupEntity != Entity.Null && em.Exists(groupEntity) && em.HasComponent<StrikeGroupOrder>(groupEntity))
        {
            StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(groupEntity);
            order.stance = preset.stance;
            order.targetPosition = targetPos;
            order.targetEntity = Entity.Null;
            order.radius = spawnWp.radius > 0f ? spawnWp.radius : 24f;
            order.version++;
            em.SetComponentData(groupEntity, order);
        }

        em.DestroyEntity(cmd);
    }

    private void ExecuteOrder(EntityManager em, Entity cmd, MissionOrderGroupCommand c)
    {
        Entity groupEntity = StrikeGroupLookup.FindDirectorGroup(em, c.groupId, c.faction, c.directorEntity);
        if (groupEntity == Entity.Null || !em.HasComponent<StrikeGroupOrder>(groupEntity))
        {
            em.DestroyEntity(cmd);
            return;
        }

        StrikeGroupData data = em.HasComponent<StrikeGroupData>(groupEntity)
            ? em.GetComponentData<StrikeGroupData>(groupEntity)
            : default;

        float2 targetPos = data.center;
        if (c.targetWaypointId != 0 && WaypointLookup.TryGetData(em, c.targetWaypointId, out Waypoint waypoint))
        {
            targetPos = waypoint.position;
        }
        else if (c.targetEntity != Entity.Null && em.Exists(c.targetEntity) && em.HasComponent<LocalTransform>(c.targetEntity))
        {
            targetPos = em.GetComponentData<LocalTransform>(c.targetEntity).Position.xy;
        }

        StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(groupEntity);
        order.stance = c.stance;
        order.targetPosition = targetPos;
        order.targetEntity = c.targetEntity;
        order.radius = c.radius > 0f ? c.radius : 24f;
        order.version++;
        em.SetComponentData(groupEntity, order);

        em.DestroyEntity(cmd);
    }

    private void ExecuteSetTactics(EntityManager em, Entity cmd, MissionSetTacticsCommand c)
    {
        Entity groupEntity = StrikeGroupLookup.FindDirectorGroup(em, c.groupId, c.faction, c.directorEntity);
        if (groupEntity == Entity.Null || !em.HasComponent<StrikeGroupData>(groupEntity))
        {
            em.DestroyEntity(cmd);
            return;
        }

        StrikeGroupData data = em.GetComponentData<StrikeGroupData>(groupEntity);
        data.tactics = c.tactics;
        em.SetComponentData(groupEntity, data);

        em.DestroyEntity(cmd);
    }


    private void ExecuteAttackFaction(EntityManager em, Entity cmd, MissionAttackFactionCommand c)
    {
        Entity groupEntity = StrikeGroupLookup.FindDirectorGroup(em, c.groupId, c.faction, c.directorEntity);
        if (groupEntity == Entity.Null || !em.HasComponent<StrikeGroupOrder>(groupEntity))
        {
            em.DestroyEntity(cmd);
            return;
        }

        if (!TryGetFactionCenter(em, c.targetFaction, out float2 targetCenter, out int targetCount))
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Mission] AttackFaction found no alive targets. attackerGroup={c.groupId} attackerFaction={(int)c.faction} targetFaction={(int)c.targetFaction}");
#endif
            em.DestroyEntity(cmd);
            return;
        }

        StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(groupEntity);
        order.stance = c.stance == Stance.Idle ? Stance.AttackMove : c.stance;
        order.targetPosition = targetCenter;
        order.targetEntity = Entity.Null;
        order.radius = c.radius > 0f ? c.radius : 24f;
        order.version++;
        em.SetComponentData(groupEntity, order);

#if STRIKEGROUP_VERBOSE
        Debug.Log($"[Mission] AttackFaction group={c.groupId} targetFaction={(int)c.targetFaction} targets={targetCount} pos={targetCenter} version={order.version}");
#endif

        em.DestroyEntity(cmd);
    }

    private static bool TryGetFactionCenter(EntityManager em, Faction targetFaction, out float2 center, out int count)
    {
        EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Unit>(),
                ComponentType.ReadOnly<LocalTransform>(),
            },
            None = new[]
            {
                ComponentType.ReadOnly<Prefab>(),
            },
        });

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

        float2 sum = float2.zero;
        count = 0;

        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            if (e == Entity.Null || !em.Exists(e))
                continue;

            Unit unit = em.GetComponentData<Unit>(e);
            if (unit.faction != targetFaction)
                continue;

            if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).healthAmount <= 0f)
                continue;

            LocalTransform transform = em.GetComponentData<LocalTransform>(e);
            sum += transform.Position.xy;
            count++;
        }

        entities.Dispose();
        query.Dispose();

        if (count <= 0)
        {
            center = float2.zero;
            return false;
        }

        center = sum / count;
        return true;
    }

    private void ExecuteDestroy(EntityManager em, Entity cmd, MissionDestroyGroupCommand c)
    {
        Entity groupEntity = StrikeGroupLookup.FindDirectorGroup(em, c.groupId, c.faction, c.directorEntity);
        if (groupEntity == Entity.Null)
        {
            em.DestroyEntity(cmd);
            return;
        }

        if (em.HasBuffer<StrikeGroupSquadElement>(groupEntity))
        {
            DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(groupEntity);
            for (int i = squads.Length - 1; i >= 0; i--)
            {
                Entity squadEntity = squads[i].squadEntity;
                if (squadEntity == Entity.Null || !em.Exists(squadEntity)) continue;

                if (c.mode == DestroyGroupMode.DetachSquadsOnly)
                {
                    SquadConfigurator.DetachSquadFromStrikeGroup(em, squadEntity);
                    continue;
                }

                if (em.HasBuffer<SquadMember>(squadEntity))
                {
                    DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
                    for (int m = 0; m < members.Length; m++)
                    {
                        Entity ship = members[m].ship;
                        if (ship == Entity.Null || !em.Exists(ship))
                            continue;

                        if (c.mode == DestroyGroupMode.DestroySquadsAndShips)
                        {
                            em.DestroyEntity(ship);
                        }
                        else if (c.mode == DestroyGroupMode.DestroySquads)
                        {
                            if (em.HasComponent<ShipSquadRef>(ship))
                                em.RemoveComponent<ShipSquadRef>(ship);

                            if (em.HasComponent<ShipPriorityHint>(ship))
                            {
                                em.SetComponentData(ship, new ShipPriorityHint
                                {
                                    target = Entity.Null,
                                    weight = 0f,
                                });
                            }
                        }
                    }
                }

                em.DestroyEntity(squadEntity);
            }
        }

        em.DestroyEntity(groupEntity);
        em.DestroyEntity(cmd);
    }
}
