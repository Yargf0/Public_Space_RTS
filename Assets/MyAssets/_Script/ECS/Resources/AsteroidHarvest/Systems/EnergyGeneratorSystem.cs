using Unity.Burst;
using Unity.Entities;

// passive energy generation
// every ship with EnergyGenerator adds energy to ResourceData per tick
// no spatial search needed, works while ship is alive
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HarvesterGatherSystem))]
public partial struct EnergyGeneratorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResourceData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        RefRW<ResourceData> resourceData = SystemAPI.GetSingletonRW<ResourceData>();

        foreach (RefRW<EnergyGenerator> generator
                 in SystemAPI.Query<RefRW<EnergyGenerator>>()
                     .WithAll<Friendly>())
        {
            generator.ValueRW.tickTimer -= dt;
            if (generator.ValueRO.tickTimer > 0f)
            {
                continue;
            }
            generator.ValueRW.tickTimer = generator.ValueRO.tickInterval;

            resourceData.ValueRW.energy += generator.ValueRO.energyPerTick;
        }
    }
}