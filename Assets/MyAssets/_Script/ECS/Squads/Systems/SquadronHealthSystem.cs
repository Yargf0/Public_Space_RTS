using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthDeadTestSystem))]
[UpdateBefore(typeof(CarrierRecallSystem))]
[UpdateBefore(typeof(SquadDefaultsSystem))]
[UpdateBefore(typeof(SquadRegenSystem))]
public partial struct SquadronHealthSystem : ISystem
{
    private const double FullValidationInterval = 0.75d;

    private EntityQuery deathEventQuery;
    private EntityStorageInfoLookup entityStorageLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<PathfindingSizeClassComponent> pathSizeLookup;
    private ComponentLookup<SquadPathSizeClass> squadPathSizeLookup;

    private double nextFullValidationTime;

    public void OnCreate(ref SystemState state)
    {
        deathEventQuery = state.GetEntityQuery(ComponentType.ReadOnly<SquadMemberDeathEvent>());
        entityStorageLookup = state.GetEntityStorageInfoLookup();
        healthLookup = state.GetComponentLookup<Health>(true);
        pathSizeLookup = state.GetComponentLookup<PathfindingSizeClassComponent>(true);
        squadPathSizeLookup = state.GetComponentLookup<SquadPathSizeClass>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        double now = SystemAPI.Time.ElapsedTime;
        bool hasDeathEvents = !deathEventQuery.IsEmptyIgnoreFilter;
        bool fullValidationDue = now >= nextFullValidationTime;

        if (!hasDeathEvents && !fullValidationDue)
            return;

        entityStorageLookup.Update(ref state);
        healthLookup.Update(ref state);
        pathSizeLookup.Update(ref state);
        squadPathSizeLookup.Update(ref state);

        EntityManager entityManager = state.EntityManager;
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        NativeList<Entity> dirtySquads = new NativeList<Entity>(Allocator.Temp);
        NativeParallelHashMap<Entity, byte> dirtySquadSet = new NativeParallelHashMap<Entity, byte>(64, Allocator.Temp);

        if (hasDeathEvents)
        {
            foreach ((RefRO<SquadMemberDeathEvent> deathEvent, Entity eventEntity)
                in SystemAPI.Query<RefRO<SquadMemberDeathEvent>>().WithEntityAccess())
            {
                Entity squadEntity = deathEvent.ValueRO.squad;
                if (IsValidSquad(entityManager, squadEntity))
                {
                    DynamicBuffer<SquadMember> members = entityManager.GetBuffer<SquadMember>(squadEntity);
                    if (RemoveMemberByEntityOrSlot(members, deathEvent.ValueRO.ship, deathEvent.ValueRO.slotIndex))
                    {
                        AddDirtySquad(squadEntity, ref dirtySquads, ref dirtySquadSet);
                    }
                }

                ecb.DestroyEntity(eventEntity);
            }
        }

        for (int i = 0; i < dirtySquads.Length; i++)
        {
            CompactAndRebuildSquad(entityManager, dirtySquads[i]);
        }

        if (fullValidationDue)
        {
            foreach ((RefRW<SquadComponent> squad, DynamicBuffer<SquadMember> members, Entity squadEntity)
                in SystemAPI.Query<RefRW<SquadComponent>, DynamicBuffer<SquadMember>>()
                    .WithAll<SquadronTag>()
                    .WithEntityAccess())
            {
                CompactAndRebuildSquad(entityManager, squadEntity, ref squad.ValueRW, members);
            }

            nextFullValidationTime = now + FullValidationInterval;
        }

        ecb.Playback(entityManager);
        dirtySquadSet.Dispose();
        dirtySquads.Dispose();
    }

    private static bool IsValidSquad(EntityManager entityManager, Entity squadEntity)
    {
        return squadEntity != Entity.Null &&
               entityManager.Exists(squadEntity) &&
               entityManager.HasComponent<SquadComponent>(squadEntity) &&
               entityManager.HasBuffer<SquadMember>(squadEntity);
    }

    private static void AddDirtySquad(
        Entity squadEntity,
        ref NativeList<Entity> dirtySquads,
        ref NativeParallelHashMap<Entity, byte> dirtySquadSet)
    {
        if (squadEntity == Entity.Null)
            return;

        if (dirtySquadSet.TryAdd(squadEntity, 1))
            dirtySquads.Add(squadEntity);
    }

    private static bool RemoveMemberByEntityOrSlot(DynamicBuffer<SquadMember> members, Entity ship, int slotIndex)
    {
        int removeIndex = -1;

        for (int i = 0; i < members.Length; i++)
        {
            SquadMember member = members[i];
            if (member.ship == ship)
            {
                removeIndex = i;
                break;
            }
        }

        if (removeIndex < 0 && slotIndex >= 0)
        {
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i].slotIndex == slotIndex)
                {
                    removeIndex = i;
                    break;
                }
            }
        }

        if (removeIndex < 0)
            return false;

        for (int i = removeIndex + 1; i < members.Length; i++)
            members[i - 1] = members[i];

        members.ResizeUninitialized(members.Length - 1);
        return true;
    }

    private void CompactAndRebuildSquad(EntityManager entityManager, Entity squadEntity)
    {
        if (!IsValidSquad(entityManager, squadEntity))
            return;

        SquadComponent squad = entityManager.GetComponentData<SquadComponent>(squadEntity);
        DynamicBuffer<SquadMember> members = entityManager.GetBuffer<SquadMember>(squadEntity);
        CompactAndRebuildSquad(entityManager, squadEntity, ref squad, members);
        entityManager.SetComponentData(squadEntity, squad);
    }

    private void CompactAndRebuildSquad(
        EntityManager entityManager,
        Entity squadEntity,
        ref SquadComponent squad,
        DynamicBuffer<SquadMember> members)
    {
        int oldLength = members.Length;
        int writeIndex = 0;
        bool memberListChanged = false;
        bool hasAnyMember = false;
        PathfindingSizeClass maxPathSize = PathfindingSizeClass.Small;

        for (int readIndex = 0; readIndex < oldLength; readIndex++)
        {
            SquadMember member = members[readIndex];
            Entity ship = member.ship;

            bool alive = ship != Entity.Null && entityStorageLookup.Exists(ship);
            if (alive && healthLookup.TryGetComponent(ship, out Health health))
                alive = health.healthAmount > 0f;

            if (!alive)
            {
                memberListChanged = true;
                continue;
            }

            PathfindingSizeClass memberPathSize = pathSizeLookup.TryGetComponent(ship, out PathfindingSizeClassComponent pathSize)
                ? pathSize.Value
                : PathfindingSizeClass.Medium;

            if (!hasAnyMember || GetPathSizeRank(memberPathSize) > GetPathSizeRank(maxPathSize))
                maxPathSize = memberPathSize;

            hasAnyMember = true;

            if (writeIndex != readIndex)
                members[writeIndex] = member;

            writeIndex++;
        }

        if (memberListChanged)
        {
            members.ResizeUninitialized(writeIndex);
            SquadFormationRuntimeUtility.RebuildCompactFormationSlots(entityManager, members);
            WriteSquadPathSize(squadEntity, maxPathSize, hasAnyMember);
        }

        if (squad.aliveCount != writeIndex)
            squad.aliveCount = writeIndex;
    }

    private void WriteSquadPathSize(Entity squadEntity, PathfindingSizeClass maxPathSize, bool hasAnyMember)
    {
        if (squadEntity == Entity.Null || !squadPathSizeLookup.HasComponent(squadEntity))
            return;

        SquadPathSizeClass next = new SquadPathSizeClass
        {
            Value = hasAnyMember ? maxPathSize : PathfindingSizeClass.Medium,
            Valid = hasAnyMember,
        };

        SquadPathSizeClass current = squadPathSizeLookup[squadEntity];
        if (current.Value == next.Value && current.Valid == next.Valid)
            return;

        squadPathSizeLookup[squadEntity] = next;
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
}
