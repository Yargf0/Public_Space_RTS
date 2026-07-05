using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StrikeGroupSummarySystem))]
[UpdateAfter(typeof(TacticsSyncSystem))]
[UpdateBefore(typeof(SquadCommandDequeueSystem))]
public partial struct StrikeGroupOrderExecutionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach ((RefRO<StrikeGroupData> data,
                  RefRO<StrikeGroupOrder> order,
                  RefRW<StrikeGroupOrderRuntime> runtime,
                  DynamicBuffer<StrikeGroupSquadElement> squads)
                 in SystemAPI.Query<RefRO<StrikeGroupData>, RefRO<StrikeGroupOrder>, RefRW<StrikeGroupOrderRuntime>, DynamicBuffer<StrikeGroupSquadElement>>()
                     .WithAll<StrikeGroupTag>())
        {
            if (order.ValueRO.version == runtime.ValueRO.appliedVersion)
                continue;

            float spreadRadius = order.ValueRO.radius > 0f ? order.ValueRO.radius : 32f;

            for (int i = 0; i < squads.Length; i++)
            {
                Entity squadEntity = squads[i].squadEntity;
                if (squadEntity == Entity.Null || !SystemAPI.Exists(squadEntity) || !SystemAPI.HasComponent<SquadComponent>(squadEntity))
                    continue;

                float2 slot = GroupSlotUtility.GetSlotOffset(i, squads.Length, spreadRadius);
                float2 anchorPos = order.ValueRO.targetPosition + slot;

                StanceResolution resolution = StanceResolver.Resolve(
                    order.ValueRO.stance,
                    order.ValueRO.targetEntity,
                    anchorPos);

                SquadComponent squad = SystemAPI.GetComponent<SquadComponent>(squadEntity);
                squad.anchorPosition = resolution.anchorPos;
                squad.anchorEntity = resolution.anchorEntity;
                squad.priorityTarget = resolution.priorityTarget;
                squad.defaultMoveMode = resolution.moveMode;
                squad.currentStance = order.ValueRO.stance;
                squad.lastGroupOrderVersion = order.ValueRO.version;
                SystemAPI.SetComponent(squadEntity, squad);
            }

            runtime.ValueRW.appliedVersion = order.ValueRO.version;
#if STRIKEGROUP_VERBOSE
            UnityEngine.Debug.Log($"[StrikeGroupOrder] group={data.ValueRO.groupId} faction={(int)data.ValueRO.faction} stance={(int)order.ValueRO.stance} version={order.ValueRO.version} squads={squads.Length} spreadRadius={spreadRadius}");
#endif
        }
    }
}
