using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateBefore(typeof(SelectedVisualSystem))]
public partial struct ExpandSelectionToSquadSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;

        ComponentLookup<SquadComponent> squadLookup =
            SystemAPI.GetComponentLookup<SquadComponent>(true);

        NativeHashSet<Entity> squadsToExpand = new NativeHashSet<Entity>(32, Allocator.Temp);
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach ((RefRO<ShipSquadRef> shipRef, Entity ship)
            in SystemAPI.Query<RefRO<ShipSquadRef>>()
                .WithAll<Selected>()
                .WithEntityAccess())
        {
            if (!SystemAPI.IsComponentEnabled<Selected>(ship))
                continue;

            Entity squadEntity = shipRef.ValueRO.squad;
            if (squadEntity == Entity.Null || !em.Exists(squadEntity) || !squadLookup.HasComponent(squadEntity))
                continue;

            SquadComponent squad = squadLookup[squadEntity];

            // carrier squadrons are drones, not selectable, no squad expand
            if (squad.origin == SquadOrigin.Carrier)
            {
                Selected selected = em.GetComponentData<Selected>(ship);
                selected.OnSelected = false;
                selected.OnDeselected = true;

                ecb.SetComponent(ship, selected);
                ecb.SetComponentEnabled<Selected>(ship, false);
                continue;
            }

            squadsToExpand.Add(squadEntity);
        }

        NativeArray<Entity> squads = squadsToExpand.ToNativeArray(Allocator.Temp);

        for (int i = 0; i < squads.Length; i++)
        {
            Entity squadEntity = squads[i];
            if (!em.Exists(squadEntity) ||
                !em.HasBuffer<SquadMember>(squadEntity) ||
                !squadLookup.HasComponent(squadEntity))
            {
                continue;
            }

            SquadComponent squad = squadLookup[squadEntity];
            if (squad.origin == SquadOrigin.Carrier)
                continue;

            DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);

            for (int m = 0; m < members.Length; m++)
            {
                Entity ship = members[m].ship;
                if (ship == Entity.Null || !em.Exists(ship) || !em.HasComponent<Selected>(ship))
                    continue;

                if (!em.IsComponentEnabled<Selected>(ship))
                {
                    Selected selected = em.GetComponentData<Selected>(ship);
                    selected.OnSelected = true;
                    selected.OnDeselected = false;

                    ecb.SetComponent(ship, selected);
                    ecb.SetComponentEnabled<Selected>(ship, true);
                }
            }
        }

        ecb.Playback(em);
        ecb.Dispose();

        squads.Dispose();
        squadsToExpand.Dispose();
    }
}