using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(ProductionStartSystem))]
[UpdateBefore(typeof(ProductionCompleteSystem))]
public partial struct ProductionProgressSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        float dt = SystemAPI.Time.DeltaTime;

        foreach ((RefRW<ActiveProduction> activeProduction,
                  RefRO<ProducerConfig> config,
                  RefRO<ProducerState> producerState,
                  Entity entity)
                 in SystemAPI.Query<RefRW<ActiveProduction>, RefRO<ProducerConfig>, RefRO<ProducerState>>().WithEntityAccess())
        {
            if (!activeProduction.ValueRO.isActive)
                continue;

            if (!ProductionUtility.CanProducerWork(entityManager, entity, producerState.ValueRO, out _))
                continue;

            if (activeProduction.ValueRO.timer <= 0f)
                continue;

            float speed = math.max(0f, config.ValueRO.buildSpeed);
            activeProduction.ValueRW.timer -= dt * speed;

            if (activeProduction.ValueRW.timer < 0f)
                activeProduction.ValueRW.timer = 0f;
        }
    }
}