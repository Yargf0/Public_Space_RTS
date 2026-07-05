using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SquadCommandDequeueSystem))]
public partial struct SquadPathSizeClassInitSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityQuery query = SystemAPI.QueryBuilder()
            .WithAll<SquadronTag, SquadComponent>()
            .WithNone<SquadPathSizeClass>()
            .Build();

        NativeArray<Entity> squads = query.ToEntityArray(Allocator.Temp);
        if (squads.Length == 0)
        {
            squads.Dispose();
            return;
        }

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        for (int i = 0; i < squads.Length; i++)
        {
            ecb.AddComponent(squads[i], new SquadPathSizeClass
            {
                Value = PathfindingSizeClass.Medium,
                Valid = false,
            });
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        squads.Dispose();
    }
}
