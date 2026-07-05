using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ForcedTargetValidationSystem))]
[UpdateBefore(typeof(SquadDefaultsSystem))]
public partial struct SquadReferenceValidationSystem : ISystem
{
    private const double ValidationInterval = 0.5;

    private double nextValidationTime;

    public void OnUpdate(ref SystemState state)
    {
        double now = SystemAPI.Time.ElapsedTime;
        if (now < nextValidationTime)
            return;

        nextValidationTime = now + ValidationInterval;

        EntityManager em = state.EntityManager;
        foreach (RefRW<SquadComponent> squad in SystemAPI.Query<RefRW<SquadComponent>>().WithAll<SquadronTag>())
        {
            SquadComponent value = squad.ValueRO;
            if (SquadCommandApplyUtility.CleanupInvalidReferences(em, ref value))
                squad.ValueRW = value;
        }
    }
}
