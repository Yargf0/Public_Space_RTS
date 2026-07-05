using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SquadRegenSystem))]
public partial struct SquadronCleanupSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        NativeList<Entity> deadSquads = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<SquadComponent> squad, Entity squadEntity)
            in SystemAPI.Query<RefRO<SquadComponent>>()
                .WithAll<SquadronTag>()
                .WithEntityAccess())
        {
            if (squad.ValueRO.aliveCount <= 0)
                deadSquads.Add(squadEntity);
        }

        EntityManager em = state.EntityManager;
        for (int i = 0; i < deadSquads.Length; i++)
        {
            Entity squadEntity = deadSquads[i];
            if (squadEntity == Entity.Null || !em.Exists(squadEntity))
                continue;

            // clean StrikeGroupSquadElement right away, don't wait for SummarySystem
            SquadConfigurator.DetachSquadFromStrikeGroup(em, squadEntity);

            if (em.Exists(squadEntity))
                em.DestroyEntity(squadEntity);
        }

        deadSquads.Dispose();
    }
}
