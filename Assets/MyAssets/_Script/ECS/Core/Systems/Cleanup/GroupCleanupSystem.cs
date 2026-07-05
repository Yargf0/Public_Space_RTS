using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// deletes unused group entities (FlowField)
// every N frames: collect GroupEntity refs from units, delete GroupGridCell that nobody uses
// without this groups pile up forever and FindExistingGroup gets slower and slower
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[BurstCompile]
partial struct GroupCleanupSystem : ISystem
{
    // check every N frames
    private const int CleanupIntervalFrames = 120;
    private int frameCounter;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        frameCounter = 0;
        state.RequireForUpdate<GridEntityDatabase>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        frameCounter++;
        if (frameCounter < CleanupIntervalFrames)
        {
            return;
        }
        frameCounter = 0;

        // step 1: collect active GroupEntity
        var activeGroups = new NativeHashSet<Entity>(64, Allocator.Temp);

        foreach ((
            RefRO<UnitGroup> unitGroup,
            Entity entity)
            in SystemAPI.Query<
                RefRO<UnitGroup>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            Entity groupEntity = unitGroup.ValueRO.GroupEntity;
            if (groupEntity != Entity.Null)
            {
                activeGroups.Add(groupEntity);
            }
        }

        // step 2: find garbage GroupGridCell
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        var toRemove = new NativeList<Entity>(16, Allocator.Temp);

        foreach ((
            RefRO<GroupGridCell> groupGridCell,
            Entity entity)
            in SystemAPI.Query<
                RefRO<GroupGridCell>>()
                .WithEntityAccess())
        {
            if (!activeGroups.Contains(entity))
            {
                toRemove.Add(entity);
                ecb.DestroyEntity(entity);
            }
        }

        // step 3: clean dead refs from GridEntityElement buffer
        if (toRemove.Length > 0)
        {
            Entity dbEntity = SystemAPI.GetSingletonEntity<GridEntityDatabase>();
            DynamicBuffer<GridEntityElement> buffer =
                SystemAPI.GetBuffer<GridEntityElement>(dbEntity);

            // HashSet for fast lookup
            var removeSet = new NativeHashSet<Entity>(toRemove.Length, Allocator.Temp);
            for (int i = 0; i < toRemove.Length; i++)
            {
                removeSet.Add(toRemove[i]);
            }

            // reverse loop for safe remove
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                if (removeSet.Contains(buffer[i].Value) ||
                    !SystemAPI.Exists(buffer[i].Value))
                {
                    buffer.RemoveAt(i);
                }
            }

            removeSet.Dispose();
        }

        activeGroups.Dispose();
        toRemove.Dispose();
        ecb.Playback(state.EntityManager);
    }
}