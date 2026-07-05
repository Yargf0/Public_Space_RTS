using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct SquadronSpawnOptions
{
    public bool addSelected;
    public bool addCanControl;

    public static SquadronSpawnOptions PlayerControllable => new SquadronSpawnOptions { addSelected = true, addCanControl = true };
    public static SquadronSpawnOptions CarrierOwned => new SquadronSpawnOptions { addSelected = false, addCanControl = false };
    public static SquadronSpawnOptions AIControlled => new SquadronSpawnOptions { addSelected = false, addCanControl = false };
}

public struct SquadronSpawnEntry
{
    public Entity prefab;
    public int count;
    public int prefabIndex;
}

public static class SquadronSpawner
{
    public static Entity Spawn(
        EntityManager em,
        Entity memberPrefab,
        float2 position,
        Faction faction,
        int count,
        FormationType formation,
        float spacing,
        int memberPrefabIndex = 0)
    {
        return Spawn(em, memberPrefab, position, faction, count, formation, spacing, memberPrefabIndex, SquadronSpawnOptions.PlayerControllable);
    }

    public static Entity Spawn(
        EntityManager em,
        Entity memberPrefab,
        float2 position,
        Faction faction,
        int count,
        FormationType formation,
        float spacing,
        int memberPrefabIndex,
        SquadronSpawnOptions options)
    {
        if (memberPrefab == Entity.Null || count <= 0)
            return Entity.Null;

        NativeArray<SquadronSpawnEntry> entries = new NativeArray<SquadronSpawnEntry>(1, Allocator.Temp);
        entries[0] = new SquadronSpawnEntry { prefab = memberPrefab, count = count, prefabIndex = memberPrefabIndex };
        Entity squad = SpawnMixed(em, position, faction, formation, spacing, entries, options);
        entries.Dispose();
        return squad;
    }

    public static Entity SpawnMixed(
        EntityManager em,
        float2 position,
        Faction faction,
        FormationType formation,
        float spacing,
        NativeArray<SquadronSpawnEntry> entries,
        SquadronSpawnOptions options)
    {
        if (!entries.IsCreated || entries.Length == 0)
            return Entity.Null;

        int totalCount = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].prefab != Entity.Null && entries[i].count > 0)
                totalCount += entries[i].count;
        }

        if (totalCount <= 0)
            return Entity.Null;

        Entity squadEntity = CreateSquadEntity(
            em,
            position,
            Entity.Null,
            faction,
            SquadRole.Interceptor,
            ShipState.Idle,
            position,
            totalCount,
            formation,
            spacing,
            MoveMode.MoveAndEngage,
            FireMode.FireAtWill,
            Tactics.Neutral,
            SquadOrigin.ArmyPlan,
            Entity.Null,
            -1,
            0f,
            options);

        int slotIndex = 0;
        for (int e = 0; e < entries.Length; e++)
        {
            SquadronSpawnEntry entry = entries[e];
            if (entry.prefab == Entity.Null || entry.count <= 0)
                continue;

            for (int c = 0; c < entry.count; c++)
            {
                SpawnMemberIntoSlot(em, squadEntity, entry.prefab, position, formation, spacing, totalCount, slotIndex, entry.prefabIndex);
                slotIndex++;
            }
        }

        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
        ApplyInitialStateToMembers(em, members, position, ShipState.Idle, MoveMode.MoveAndEngage, FireMode.FireAtWill);
        SquadFormationRuntimeUtility.RebuildCompactFormationSlots(em, members);

        SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);
        squad.aliveCount = members.Length;
        em.SetComponentData(squadEntity, squad);
        SquadCommandApplyUtility.UpdateSquadPathSizeClass(em, squadEntity, members);

        return squadEntity;
    }

    public static Entity SpawnFromTemplates(
        EntityManager em,
        in CreateSquadCommand command,
        DynamicBuffer<CreateSquadMemberTemplate> templates)
    {
        NativeArray<CreateSquadMemberTemplate> templateCopy =
            new NativeArray<CreateSquadMemberTemplate>(templates.Length, Allocator.Temp);

        for (int i = 0; i < templates.Length; i++)
            templateCopy[i] = templates[i];

        Entity squadEntity = SpawnFromTemplateArray(em, command, templateCopy);

        templateCopy.Dispose();
        return squadEntity;
    }

    private static Entity SpawnFromTemplateArray(
        EntityManager em,
        in CreateSquadCommand command,
        NativeArray<CreateSquadMemberTemplate> templates)
    {
        int totalCount = command.memberCount > 0 ? command.memberCount : templates.Length;
        if (totalCount <= 0)
            return Entity.Null;

        bool directorOwnedGroup =
            command.targetStrikeGroupEntity != Entity.Null &&
            em.Exists(command.targetStrikeGroupEntity) &&
            em.HasComponent<StrikeGroupData>(command.targetStrikeGroupEntity) &&
            em.GetComponentData<StrikeGroupData>(command.targetStrikeGroupEntity).ownership == StrikeGroupOwnership.Director;

        SquadronSpawnOptions spawnOptions =
            command.origin == SquadOrigin.Carrier || directorOwnedGroup
                ? SquadronSpawnOptions.AIControlled
                : command.faction == Faction.Friendly
                    ? SquadronSpawnOptions.PlayerControllable
                    : SquadronSpawnOptions.AIControlled;

        Entity squadEntity = CreateSquadEntity(
            em,
            command.spawnAnchor,
            command.anchorEntity,
            command.faction,
            command.role,
            command.initialState,
            command.initialTargetPosition,
            totalCount,
            command.formation,
            command.spacing,
            command.defaultMoveMode,
            command.defaultFireMode,
            command.initialTactics,
            command.origin,
            command.originEntity,
            command.carrierSlotIndex,
            command.initialEndurance,
            spawnOptions);

        for (int i = 0; i < templates.Length; i++)
        {
            CreateSquadMemberTemplate template = templates[i];
            if (template.memberPrefab == Entity.Null)
                continue;

            int slot = template.slotIndex >= 0 ? template.slotIndex : i;

            SpawnMemberIntoSlot(
                em,
                squadEntity,
                template.memberPrefab,
                command.spawnAnchor,
                command.formation,
                command.spacing,
                totalCount,
                slot,
                template.memberPrefabIndex);
        }

        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
        ApplyInitialStateToMembers(em, members, command.initialTargetPosition, command.initialState, command.defaultMoveMode, command.defaultFireMode);
        SquadFormationRuntimeUtility.RebuildCompactFormationSlots(em, members);

        SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);
        squad.aliveCount = members.Length;
        em.SetComponentData(squadEntity, squad);
        SquadCommandApplyUtility.UpdateSquadPathSizeClass(em, squadEntity, members);

        return squadEntity;
    }

    private static void ApplyInitialStateToMembers(
        EntityManager em,
        DynamicBuffer<SquadMember> members,
        float2 targetPosition,
        ShipState initialState,
        MoveMode moveMode,
        FireMode fireMode)
    {
        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship != Entity.Null && em.Exists(ship))
                SquadConfigurator.ApplyInitialState(em, ship, targetPosition, initialState, moveMode, fireMode);
        }
    }

    public static int Reinforce(EntityManager em, Entity squadEntity, Entity fallbackPrefab, int maxToAdd = -1)
    {
        if (squadEntity == Entity.Null || !em.Exists(squadEntity) || !em.HasComponent<SquadComponent>(squadEntity))
            return 0;

        int added = 0;
        while (maxToAdd < 0 || added < maxToAdd)
        {
            if (!ReinforceFirstEmptySlot(em, squadEntity, fallbackPrefab))
                break;

            added++;
        }

        return added;
    }

    public static bool ReinforceFirstEmptySlot(EntityManager em, Entity squadEntity, Entity fallbackPrefab = default)
    {
        if (squadEntity == Entity.Null ||
            !em.Exists(squadEntity) ||
            !em.HasComponent<SquadComponent>(squadEntity) ||
            !em.HasBuffer<SquadMember>(squadEntity) ||
            !em.HasBuffer<SquadSlotTemplate>(squadEntity))
        {
            return false;
        }

        SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);
        if (squad.aliveCount >= squad.maxMembers || squad.aliveCount <= 0)
            return false;

        // Safe before structural changes.
        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
        int slot = FindFirstEmptySlot(members, squad.maxMembers);
        if (slot < 0)
            return false;

        // Copy needed template data before SpawnMemberIntoSlot.
        DynamicBuffer<SquadSlotTemplate> templates = em.GetBuffer<SquadSlotTemplate>(squadEntity);

        Entity prefab = ResolvePrefabForSlot(templates, slot, fallbackPrefab);
        if (prefab == Entity.Null)
            return false;

        int prefabIndex = ResolvePrefabIndexForSlot(templates, slot);

        // old buffer handles are dead after this call
        SpawnMemberIntoSlot(
            em,
            squadEntity,
            prefab,
            squad.anchorPosition,
            squad.formation,
            squad.spacing,
            squad.aliveCount + 1,
            slot,
            prefabIndex);

        // Re-fetch buffer after structural changes.
        DynamicBuffer<SquadMember> updatedMembers = em.GetBuffer<SquadMember>(squadEntity);
        SquadFormationRuntimeUtility.RebuildCompactFormationSlots(em, updatedMembers);

        squad.aliveCount = updatedMembers.Length;
        em.SetComponentData(squadEntity, squad);
        SquadCommandApplyUtility.UpdateSquadPathSizeClass(em, squadEntity, updatedMembers);

        return true;
    }

    private static Entity CreateSquadEntity(
        EntityManager em,
        float2 position,
        Entity anchorEntity,
        Faction faction,
        SquadRole role,
        ShipState initialState,
        float2 initialTargetPosition,
        int memberCount,
        FormationType formation,
        float spacing,
        MoveMode moveMode,
        FireMode fireMode,
        Tactics tactics,
        SquadOrigin origin,
        Entity originEntity,
        int carrierSlotIndex,
        float enduranceRemaining,
        SquadronSpawnOptions options)
    {
        Entity squadEntity = em.CreateEntity();
        em.AddComponentData(squadEntity, new SquadronTag());
        em.AddComponentData(squadEntity, new SquadComponent
        {
            squadId = (byte)(squadEntity.Index & 0xFF),
            faction = faction,
            role = role,
            maxMembers = memberCount,
            aliveCount = memberCount,
            formation = formation,
            spacing = spacing,
            anchorPosition = position,
            anchorEntity = anchorEntity,
            priorityTarget = Entity.Null,
            defaultFireMode = fireMode,
            defaultMoveMode = moveMode,
            tactics = tactics,
            currentStance = Stance.HoldPosition,
            lastGroupOrderVersion = 0,
            origin = origin,
            originEntity = originEntity,
            carrierSlotIndex = carrierSlotIndex,
            regenTimer = 0f,
            enduranceRemaining = enduranceRemaining,
        });

        em.AddComponentData(squadEntity, LocalTransform.FromPosition(new float3(position.x, position.y, GameConstants.SquadAnchorZ)));
        em.AddComponentData(squadEntity, new Unit { faction = faction, shipSize = (byte)ShipSize.Small });
        em.AddComponentData(squadEntity, new ShipStateComponent { mode = fireMode, moveMode = moveMode, currentState = initialState, previousState = ShipState.Idle, forcedTarget = Entity.Null });
        em.AddComponentData(squadEntity, new UnitMover { targetPos = initialTargetPosition, fightTarget = initialTargetPosition, followDistance = 0f });
        em.AddComponentData(squadEntity, new GroupManagerComponent());
        em.AddComponentData(squadEntity, new UnitGroup());
        em.AddComponentData(squadEntity, new Velocity());

        if (faction == Faction.Friendly)
            em.AddComponentData(squadEntity, new Friendly());
        else
            em.AddComponentData(squadEntity, new Enemy());

        if (options.addSelected)
        {
            em.AddComponentData(squadEntity, new Selected { ShowScale = 1.5f, VisualEntity = Entity.Null });
            em.SetComponentEnabled<Selected>(squadEntity, false);
        }

        if (options.addCanControl)
            em.AddComponentData(squadEntity, new CanControl());

        em.AddBuffer<SquadMember>(squadEntity);
        em.AddBuffer<SquadSlotTemplate>(squadEntity);
        em.AddBuffer<SquadCommandElement>(squadEntity);
        em.AddComponentData(squadEntity, new SquadPathSizeClass
        {
            Value = PathfindingSizeClass.Medium,
            Valid = false,
        });
        return squadEntity;
    }

    private static Entity SpawnMemberIntoSlot(
        EntityManager em,
        Entity squadEntity,
        Entity memberPrefab,
        float2 squadPosition,
        FormationType formation,
        float spacing,
        int totalMembers,
        int slot,
        int memberPrefabIndex)
    {
        // read length before structural changes, don't keep buffer handles across Instantiate
        int formationSlotIndex = em.GetBuffer<SquadMember>(squadEntity).Length;
        int formationMemberCount = math.max(1, totalMembers);

        Entity ship = em.Instantiate(memberPrefab);
        float2 offset = FormationUtility.GetSlotOffset(formation, formationSlotIndex, formationMemberCount, spacing);
        float2 shipPos = squadPosition + offset;

        SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
            em,
            ship,
            LocalTransform.FromPosition(new float3(shipPos.x, shipPos.y, ResolveShipZ(em, ship))));

        SquadConfigurator.AddOrSetComponent(em, ship, new ShipSquadRef
        {
            squad = squadEntity,
            slotIndex = slot,
            formationSlotIndex = formationSlotIndex
        });

        if (!em.HasComponent<ShipPriorityHint>(ship))
            em.AddComponentData(ship, new ShipPriorityHint { target = Entity.Null, weight = 50f });
        else
            em.SetComponentData(ship, new ShipPriorityHint { target = Entity.Null, weight = 50f });

        if (em.HasComponent<Selected>(ship))
            em.SetComponentEnabled<Selected>(ship, false);

        if (em.HasComponent<CanControl>(ship))
            em.RemoveComponent<CanControl>(ship);

        // re-fetch buffers only after all structural changes are done
        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
        members.Add(new SquadMember
        {
            ship = ship,
            slotIndex = slot,
            formationSlotIndex = formationSlotIndex
        });

        DynamicBuffer<SquadSlotTemplate> templates = em.GetBuffer<SquadSlotTemplate>(squadEntity);
        bool templateExists = false;
        for (int i = 0; i < templates.Length; i++)
        {
            if (templates[i].slotIndex == slot)
            {
                templateExists = true;
                break;
            }
        }

        if (!templateExists)
        {
            templates.Add(new SquadSlotTemplate { slotIndex = slot, memberPrefab = memberPrefab, memberPrefabIndex = memberPrefabIndex });
        }

        return ship;
    }

    private static int FindFirstEmptySlot(DynamicBuffer<SquadMember> members, int maxMembers)
    {
        for (int slot = 0; slot < maxMembers; slot++)
        {
            bool occupied = false;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i].slotIndex == slot)
                {
                    occupied = true;
                    break;
                }
            }

            if (!occupied)
                return slot;
        }

        return -1;
    }

    private static Entity ResolvePrefabForSlot(DynamicBuffer<SquadSlotTemplate> templates, int slot, Entity fallbackPrefab)
    {
        for (int i = 0; i < templates.Length; i++)
        {
            if (templates[i].slotIndex == slot && templates[i].memberPrefab != Entity.Null)
                return templates[i].memberPrefab;
        }

        return fallbackPrefab;
    }

    private static int ResolvePrefabIndexForSlot(DynamicBuffer<SquadSlotTemplate> templates, int slot)
    {
        for (int i = 0; i < templates.Length; i++)
        {
            if (templates[i].slotIndex == slot)
                return templates[i].memberPrefabIndex;
        }

        return 0;
    }
    private static float ResolveShipZ(EntityManager em, Entity ship)
    {
        if (ship != Entity.Null && em.Exists(ship) && em.HasComponent<Unit>(ship))
            return GameConstants.GetShipZ(em.GetComponentData<Unit>(ship).shipSize);

        return GameConstants.ShipDefaultZ;
    }

}
