using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateAfter(typeof(CarrierSquadSelectionBlockSystem))]
[UpdateBefore(typeof(ResetEventSystem))]
public partial struct SelectedVisualSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ComponentLookup<ShipSquadRef> shipSquadLookup =
            SystemAPI.GetComponentLookup<ShipSquadRef>(true);

        ComponentLookup<SquadComponent> squadLookup =
            SystemAPI.GetComponentLookup<SquadComponent>(true);

        foreach ((RefRW<Selected> selected, Entity entity)
            in SystemAPI.Query<RefRW<Selected>>()
                .WithPresent<Selected>()
                .WithEntityAccess())
        {
            Entity visualEntity = selected.ValueRO.VisualEntity;

            // visual entity can be destroyed already or have no LocalTransform
            if (visualEntity == Entity.Null || !SystemAPI.HasComponent<LocalTransform>(visualEntity))
            {
                selected.ValueRW.OnSelected = false;
                selected.ValueRW.OnDeselected = false;
                continue;
            }

            RefRW<LocalTransform> visualLocalTransform =
                SystemAPI.GetComponentRW<LocalTransform>(visualEntity);

            if (IsCarrierSquadMember(entity, shipSquadLookup, squadLookup))
            {
                // carrier squadrons are drones, no selection rings for them
                visualLocalTransform.ValueRW.Scale = 0f;
                selected.ValueRW.OnSelected = false;
                selected.ValueRW.OnDeselected = false;
                continue;
            }

            if (selected.ValueRO.OnDeselected)
            {
                visualLocalTransform.ValueRW.Scale = 0f;
            }

            if (selected.ValueRO.OnSelected)
            {
                visualLocalTransform.ValueRW.Scale = selected.ValueRO.ShowScale;
            }

            // event flags live one frame
            selected.ValueRW.OnSelected = false;
            selected.ValueRW.OnDeselected = false;
        }
    }

    private static bool IsCarrierSquadMember(
        Entity entity,
        ComponentLookup<ShipSquadRef> shipSquadLookup,
        ComponentLookup<SquadComponent> squadLookup)
    {
        if (!shipSquadLookup.HasComponent(entity))
            return false;

        ShipSquadRef shipRef = shipSquadLookup[entity];
        Entity squadEntity = shipRef.squad;

        if (squadEntity == Entity.Null || !squadLookup.HasComponent(squadEntity))
            return false;

        SquadComponent squad = squadLookup[squadEntity];
        return squad.origin == SquadOrigin.Carrier;
    }
}