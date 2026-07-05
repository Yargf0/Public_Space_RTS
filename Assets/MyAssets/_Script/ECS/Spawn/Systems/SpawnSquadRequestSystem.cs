using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MissionCommandExecutionSystem))]
[UpdateBefore(typeof(SquadSpawnSystem))]
public partial struct SpawnSquadRequestSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        NativeList<Entity> requests = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<SpawnSquadRequest> _, Entity requestEntity)
            in SystemAPI.Query<RefRO<SpawnSquadRequest>>().WithEntityAccess())
        {
            requests.Add(requestEntity);
        }

        for (int i = 0; i < requests.Length; i++)
        {
            Entity requestEntity = requests[i];
            if (!em.Exists(requestEntity))
                continue;

            SpawnSquadRequest req = em.GetComponentData<SpawnSquadRequest>(requestEntity);
            if (req.mode == SpawnSquadRequestMode.ReinforceExistingSquad)
                ProcessReinforce(em, requestEntity, req);
            else
                CreateSpawnCommand(em, requestEntity, req);

            if (em.Exists(requestEntity))
                em.DestroyEntity(requestEntity);
        }

        requests.Dispose();
    }

    private static void ProcessReinforce(EntityManager em, Entity requestEntity, in SpawnSquadRequest req)
    {
        if (req.targetSquadEntity == Entity.Null || !em.Exists(req.targetSquadEntity))
            return;

        Entity groupEntity = ResolveReinforceGroup(em, req);
        if (groupEntity != Entity.Null && em.Exists(groupEntity) && em.HasComponent<StrikeGroupTag>(groupEntity))
            SquadConfigurator.AttachSquadToStrikeGroup(em, groupEntity, req.targetSquadEntity);

        if (em.HasBuffer<SpawnSquadRequestMemberElement>(requestEntity))
        {
            DynamicBuffer<SpawnSquadRequestMemberElement> members = em.GetBuffer<SpawnSquadRequestMemberElement>(requestEntity);
            for (int i = 0; i < members.Length; i++)
            {
                SpawnSquadRequestMemberElement src = members[i];
                if (src.prefab != Entity.Null && src.count > 0)
                    SquadronSpawner.Reinforce(em, req.targetSquadEntity, src.prefab, src.count);
            }
        }
        else if (req.singlePrefab != Entity.Null && req.singlePrefabCount > 0)
        {
            SquadronSpawner.Reinforce(em, req.targetSquadEntity, req.singlePrefab, req.singlePrefabCount);
        }

        ForceRefreshGroupOrder(em, groupEntity);
    }

    private static Entity ResolveReinforceGroup(EntityManager em, in SpawnSquadRequest req)
    {
        if (req.targetStrikeGroupEntity != Entity.Null && em.Exists(req.targetStrikeGroupEntity))
            return req.targetStrikeGroupEntity;

        if (req.targetSquadEntity != Entity.Null &&
            em.Exists(req.targetSquadEntity) &&
            em.HasComponent<StrikeGroupMember>(req.targetSquadEntity))
        {
            StrikeGroupMember member = em.GetComponentData<StrikeGroupMember>(req.targetSquadEntity);
            if (member.groupEntity != Entity.Null && em.Exists(member.groupEntity))
                return member.groupEntity;
        }

        return Entity.Null;
    }

    private static void ForceRefreshGroupOrder(EntityManager em, Entity groupEntity)
    {
        if (groupEntity == Entity.Null || !em.Exists(groupEntity))
            return;

        if (em.HasComponent<StrikeGroupOrderRuntime>(groupEntity))
        {
            StrikeGroupOrderRuntime runtime = em.GetComponentData<StrikeGroupOrderRuntime>(groupEntity);
            runtime.appliedVersion = 0;
            em.SetComponentData(groupEntity, runtime);
        }

        if (em.HasComponent<StrikeGroupData>(groupEntity))
        {
            StrikeGroupData data = em.GetComponentData<StrikeGroupData>(groupEntity);
            data.summaryTimer = 0f;
            em.SetComponentData(groupEntity, data);
        }
    }

    private static void CreateSpawnCommand(EntityManager em, Entity requestEntity, in SpawnSquadRequest req)
    {
        Entity targetGroup = req.targetStrikeGroupEntity;
        if (targetGroup == Entity.Null && req.createOrJoinStrikeGroup)
        {
            int groupId = req.groupId != 0 ? req.groupId : LevelSpawnApi.ReserveGroupId();
            targetGroup = SquadConfigurator.GetOrCreateStrikeGroup(
                em,
                req.faction,
                groupId,
                req.initialTactics,
                req.spawnPosition,
                req.groupOwnership,
                req.ownerEntity);
        }

        Entity commandEntity = em.CreateEntity();
        int totalCount = CountMembers(em, requestEntity, req);
        FormationType formation = SquadConfigurator.GetFormationByRole(req.role);
        float spacing = SquadConfigurator.GetSpacingByRole(req.role);

        em.AddComponentData(commandEntity, new CreateSquadCommand
        {
            faction = req.faction,
            role = req.role,
            initialState = req.initialState,
            initialTargetPosition = req.targetPosition,
            memberCount = totalCount,
            formation = formation,
            spacing = spacing,
            spawnAnchor = req.spawnPosition,
            anchorEntity = Entity.Null,
            defaultFireMode = req.initialFireMode,
            defaultMoveMode = req.initialMoveMode,
            origin = SquadOrigin.ArmyPlan,
            originEntity = Entity.Null,
            carrierSlotIndex = -1,
            initialEndurance = 0f,
            targetStrikeGroupEntity = targetGroup,
            initialTactics = req.initialTactics,
            requestTag = req.requestTag,
        });

        DynamicBuffer<CreateSquadMemberTemplate> outMembers = em.AddBuffer<CreateSquadMemberTemplate>(commandEntity);
        FillTemplates(em, requestEntity, req, outMembers);
    }

    private static int CountMembers(EntityManager em, Entity requestEntity, in SpawnSquadRequest req)
    {
        int count = 0;
        if (em.HasBuffer<SpawnSquadRequestMemberElement>(requestEntity))
        {
            DynamicBuffer<SpawnSquadRequestMemberElement> members = em.GetBuffer<SpawnSquadRequestMemberElement>(requestEntity);
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i].prefab != Entity.Null && members[i].count > 0)
                    count += members[i].count;
            }
        }
        else if (req.singlePrefab != Entity.Null && req.singlePrefabCount > 0)
        {
            count += req.singlePrefabCount;
        }

        return math.max(0, count);
    }

    private static void FillTemplates(EntityManager em, Entity requestEntity, in SpawnSquadRequest req, DynamicBuffer<CreateSquadMemberTemplate> outMembers)
    {
        int slotIndex = 0;
        if (em.HasBuffer<SpawnSquadRequestMemberElement>(requestEntity))
        {
            DynamicBuffer<SpawnSquadRequestMemberElement> members = em.GetBuffer<SpawnSquadRequestMemberElement>(requestEntity);
            for (int i = 0; i < members.Length; i++)
            {
                SpawnSquadRequestMemberElement src = members[i];
                if (src.prefab == Entity.Null || src.count <= 0)
                    continue;

                for (int c = 0; c < src.count; c++)
                {
                    outMembers.Add(new CreateSquadMemberTemplate
                    {
                        slotIndex = slotIndex,
                        memberPrefab = src.prefab,
                        memberPrefabIndex = src.memberPrefabIndex,
                    });
                    slotIndex++;
                }
            }
        }
        else if (req.singlePrefab != Entity.Null && req.singlePrefabCount > 0)
        {
            for (int i = 0; i < req.singlePrefabCount; i++)
            {
                outMembers.Add(new CreateSquadMemberTemplate
                {
                    slotIndex = slotIndex,
                    memberPrefab = req.singlePrefab,
                    memberPrefabIndex = req.singlePrefabMemberIndex,
                });
                slotIndex++;
            }
        }
    }
}
