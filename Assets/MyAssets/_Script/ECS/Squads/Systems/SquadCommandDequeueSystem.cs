using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ForcedTargetValidationSystem))]
[UpdateBefore(typeof(SquadDefaultsSystem))]
public partial struct SquadCommandDequeueSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        const float reachedDistanceSq = 4f;
        EntityManager em = state.EntityManager;

        foreach ((RefRW<SquadComponent> squad, DynamicBuffer<SquadCommandElement> queue, DynamicBuffer<SquadMember> members, Entity squadEntity)
            in SystemAPI.Query<RefRW<SquadComponent>, DynamicBuffer<SquadCommandElement>, DynamicBuffer<SquadMember>>()
                .WithAll<SquadronTag>()
                .WithEntityAccess())
        {
            // carrier squadrons are controlled by carrier only
            if (squad.ValueRO.origin == SquadOrigin.Carrier)
            {
                if (queue.Length > 0)
                    queue.Clear();

                continue;
            }

            if (queue.Length == 0)
                continue;

            SquadComponent currentSquad = squad.ValueRO;
            // only squad refs here, members are cleaned in ForcedTargetValidationSystem
            if (SquadCommandApplyUtility.CleanupInvalidReferences(em, ref currentSquad))
                squad.ValueRW = currentSquad;

            SquadCommandElement command = SquadCommandApplyUtility.ResolveMoveMode(currentSquad, queue[0]);

            SquadComponent beforeApply = currentSquad;
            SquadComponent updatedSquad = currentSquad;
            bool commandAffectsMembers = SquadCommandApplyUtility.ApplyToSquad(
                em,
                members,
                ref updatedSquad,
                command,
                out bool commandFinished,
                reachedDistanceSq);

            squad.ValueRW = updatedSquad;

            bool applyMembers = commandAffectsMembers && SquadCommandApplyUtility.ShouldApplyToMembersAfterApply(
                em,
                beforeApply,
                updatedSquad,
                command);

            if (applyMembers)
                SquadCommandApplyUtility.ApplyToMembers(em, squadEntity, members, updatedSquad, command);

            if (commandFinished && queue.Length > 0)
                queue.RemoveAt(0);
        }
    }
}
