using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateAfter(typeof(SquadronSelectionSyncSystem))]
[UpdateBefore(typeof(SelectedVisualSystem))]
public partial struct CarrierSquadSelectionBlockSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        ComponentLookup<SquadComponent> squadLookup =
            SystemAPI.GetComponentLookup<SquadComponent>(true);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach ((RefRO<ShipSquadRef> squadRef, Entity shipEntity)
            in SystemAPI.Query<RefRO<ShipSquadRef>>()
                .WithAll<Selected>()
                .WithEntityAccess())
        {
            Entity squadEntity = squadRef.ValueRO.squad;
            if (squadEntity == Entity.Null)
                continue;

            if (!squadLookup.HasComponent(squadEntity))
                continue;

            SquadComponent squad = squadLookup[squadEntity];

            if (squad.origin != SquadOrigin.Carrier)
                continue;

            // carrier squadrons are drones, player can't select or control them
            ecb.RemoveComponent<Selected>(shipEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}