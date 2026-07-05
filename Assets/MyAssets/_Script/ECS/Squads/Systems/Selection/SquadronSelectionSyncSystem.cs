using Unity.Entities;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateAfter(typeof(ExpandSelectionToSquadSystem))]
[UpdateBefore(typeof(CarrierSquadSelectionBlockSystem))]
public partial struct SquadronSelectionSyncSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach ((DynamicBuffer<SquadMember> members, Entity squadEntity)
            in SystemAPI.Query<DynamicBuffer<SquadMember>>()
                .WithAll<SquadronTag, Selected>()
                .WithEntityAccess())
        {
            bool squadSelected = SystemAPI.IsComponentEnabled<Selected>(squadEntity);

            for (int i = 0; i < members.Length; i++)
            {
                Entity ship = members[i].ship;
                if (ship == Entity.Null || !state.EntityManager.Exists(ship) || !state.EntityManager.HasComponent<Selected>(ship))
                    continue;

                bool shipSelected = SystemAPI.IsComponentEnabled<Selected>(ship);
                if (squadSelected == shipSelected)
                    continue;

                SystemAPI.SetComponentEnabled<Selected>(ship, squadSelected);
                Selected selected = SystemAPI.GetComponent<Selected>(ship);
                selected.OnSelected = squadSelected;
                selected.OnDeselected = !squadSelected;
                SystemAPI.SetComponent(ship, selected);
            }
        }
    }
}
