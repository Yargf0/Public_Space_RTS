using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static class LevelSpawnApi
{
    public static readonly SquadRequestArgs DefaultEnemyArgs = new SquadRequestArgs
    {
        faction = Faction.Enemy,
        spawnPosition = float2.zero,
        targetPosition = float2.zero,
        initialState = ShipState.MovingToTarget,
        initialMoveMode = MoveMode.MoveAndEngage,
        initialFireMode = FireMode.FireAtWill,
        initialTactics = Tactics.Neutral,
        squadRole = SquadRole.Interceptor,
        createOrJoinStrikeGroup = false,
        groupId = 0,
        groupOwnership = StrikeGroupOwnership.Debug,
        ownerEntity = Entity.Null,
        targetStrikeGroupEntity = Entity.Null,
        requestTag = 0,
    };

    public struct SquadRequestArgs
    {
        public Faction faction;
        public float2 spawnPosition;
        public float2 targetPosition;

        public ShipState initialState;
        public MoveMode initialMoveMode;
        public FireMode initialFireMode;
        public Tactics initialTactics;

        public SquadRole squadRole;

        public bool createOrJoinStrikeGroup;
        public int groupId;
        public StrikeGroupOwnership groupOwnership;
        public Entity ownerEntity;
        public Entity targetStrikeGroupEntity;

        public int requestTag;
    }

    public struct CompositionEntry
    {
        public string prefabId;
        public int count;
        public int memberPrefabIndex;
    }

    public static int ReserveGroupId()
    {
        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
        EntityQuery query = em.CreateEntityQuery(ComponentType.ReadWrite<StrikeGroupIdAllocator>());

        Entity allocatorEntity;
        StrikeGroupIdAllocator allocator;

        if (query.IsEmpty)
        {
            allocatorEntity = em.CreateEntity();
            allocator = new StrikeGroupIdAllocator { nextGroupId = math.max(1, FindMaxGroupId(em) + 1) };
            em.AddComponentData(allocatorEntity, allocator);
        }
        else
        {
            allocatorEntity = query.GetSingletonEntity();
            allocator = em.GetComponentData<StrikeGroupIdAllocator>(allocatorEntity);
        }

        int id = allocator.nextGroupId;
        allocator.nextGroupId++;
        em.SetComponentData(allocatorEntity, allocator);
        query.Dispose();
        return id;
    }

    public static Entity RequestSquad(string prefabId, int count, SquadRequestArgs args)
    {
        return RequestSquad(prefabId, count, 0, args);
    }

    public static Entity RequestSquad(string prefabId, int count, int memberPrefabIndex, SquadRequestArgs args)
    {
        return RequestMixedSquad(new[] { new CompositionEntry { prefabId = prefabId, count = count, memberPrefabIndex = memberPrefabIndex } }, args);
    }

    public static Entity RequestMixedSquad(IReadOnlyList<CompositionEntry> composition, SquadRequestArgs args)
    {
        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity requestEntity = em.CreateEntity();
        em.AddComponentData(requestEntity, BuildRequest(args, Entity.Null, 0, 0));
        DynamicBuffer<SpawnSquadRequestMemberElement> buffer = em.AddBuffer<SpawnSquadRequestMemberElement>(requestEntity);

        for (int i = 0; i < composition.Count; i++)
        {
            CompositionEntry src = composition[i];
            if (string.IsNullOrEmpty(src.prefabId) || src.count <= 0)
                continue;

            Entity prefab = LevelPrefabRegistry.Resolve(em, src.prefabId);
            if (prefab == Entity.Null)
            {
                Debug.LogWarning($"[LevelSpawnApi] prefab '{src.prefabId}' not found.");
                continue;
            }

            buffer.Add(new SpawnSquadRequestMemberElement
            {
                prefab = prefab,
                count = math.max(1, src.count),
                memberPrefabIndex = src.memberPrefabIndex,
            });
        }

        return requestEntity;
    }

    public static Entity RequestStrikeGroup(
        ArmyPlan plan,
        float2 spawnCenter,
        float2 targetPosition,
        int groupId,
        Tactics tactics,
        StrikeGroupOwnership ownership,
        Entity ownerEntity = default,
        int baseRequestTag = 0)
    {
        if (plan == null || plan.squads == null)
            return Entity.Null;

        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity groupEntity = SquadConfigurator.GetOrCreateStrikeGroup(
            em,
            plan.faction,
            groupId,
            tactics,
            spawnCenter,
            ownership,
            ownerEntity);

        if (groupEntity == Entity.Null)
            return Entity.Null;

        for (int i = 0; i < plan.squads.Length; i++)
        {
            ArmyPlanSquadEntry entry = plan.squads[i];
            if (entry == null || entry.squadPlan == null)
                continue;

            SquadPlan squadPlan = entry.squadPlan;
            SquadRequestArgs args = new SquadRequestArgs
            {
                faction = plan.faction,
                spawnPosition = spawnCenter + new float2(entry.spawnOffset.x, entry.spawnOffset.y),
                targetPosition = targetPosition,
                initialState = plan.initialState,
                initialMoveMode = plan.initialMoveMode,
                initialFireMode = plan.initialFireMode,
                initialTactics = tactics,
                squadRole = squadPlan.role,
                createOrJoinStrikeGroup = true,
                groupId = groupId,
                groupOwnership = ownership,
                ownerEntity = ownerEntity,
                targetStrikeGroupEntity = groupEntity,
                requestTag = baseRequestTag == 0 ? 0 : baseRequestTag + i,
            };

            RequestSquadFromPlan(squadPlan, args);
        }

        return groupEntity;
    }

    private static Entity RequestSquadFromPlan(SquadPlan squadPlan, SquadRequestArgs args)
    {
        if (squadPlan.composition == null || squadPlan.composition.Length == 0)
            return Entity.Null;

        List<CompositionEntry> entries = new List<CompositionEntry>(squadPlan.composition.Length);
        for (int i = 0; i < squadPlan.composition.Length; i++)
        {
            SquadCompositionEntry src = squadPlan.composition[i];
            if (src == null || string.IsNullOrEmpty(src.prefabId) || src.count <= 0)
                continue;

            entries.Add(new CompositionEntry
            {
                prefabId = src.prefabId,
                count = src.count,
                memberPrefabIndex = src.memberPrefabIndex,
            });
        }

        return RequestMixedSquad(entries, args);
    }

    private static SpawnSquadRequest BuildRequest(SquadRequestArgs args, Entity singlePrefab, int singleCount, int memberPrefabIndex)
    {
        return new SpawnSquadRequest
        {
            mode = SpawnSquadRequestMode.SpawnNewSquad,
            targetSquadEntity = Entity.Null,
            targetStrikeGroupEntity = args.targetStrikeGroupEntity,
            singlePrefab = singlePrefab,
            singlePrefabCount = singleCount,
            singlePrefabMemberIndex = memberPrefabIndex,
            spawnPosition = args.spawnPosition,
            targetPosition = args.targetPosition,
            faction = args.faction,
            role = args.squadRole,
            initialState = args.initialState,
            initialMoveMode = args.initialMoveMode,
            initialFireMode = args.initialFireMode,
            initialTactics = args.initialTactics,
            createOrJoinStrikeGroup = args.createOrJoinStrikeGroup,
            groupId = args.groupId,
            groupOwnership = args.groupOwnership,
            ownerEntity = args.ownerEntity,
            requestTag = args.requestTag,
        };
    }

    private static int FindMaxGroupId(EntityManager em)
    {
        EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<StrikeGroupTag>(), ComponentType.ReadOnly<StrikeGroupData>());
        NativeArray<Entity> groups = query.ToEntityArray(Allocator.Temp);
        int maxId = 0;
        for (int i = 0; i < groups.Length; i++)
            maxId = math.max(maxId, em.GetComponentData<StrikeGroupData>(groups[i]).groupId);
        groups.Dispose();
        query.Dispose();
        return maxId;
    }
}
