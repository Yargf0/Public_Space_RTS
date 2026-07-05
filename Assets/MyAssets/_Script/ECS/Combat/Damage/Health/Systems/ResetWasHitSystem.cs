using Unity.Burst;
using Unity.Entities;

// clears ReturnFire damage memory when timer expires
// runs late so combat systems still see hits this frame
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
partial struct ResetWasHitSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        // include disabled ShipAgro, ReturnFire memory can live longer than aggro
        foreach (
            RefRW<ShipAgro> shipAgro
            in SystemAPI.Query<
                RefRW<ShipAgro>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            // skip ships with no hit memory
            if (!shipAgro.ValueRO.wasHit)
            {
                continue;
            }

            // tick timer
            shipAgro.ValueRW.wasHitTimer -= dt;

            // expired = no ReturnFire
            if (shipAgro.ValueRO.wasHitTimer <= 0f)
            {
                shipAgro.ValueRW.wasHit = false;
                shipAgro.ValueRW.wasHitTimer = 0f;
            }
        }
    }
}
