using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(ProductionRequestSystem))]
[UpdateBefore(typeof(ProductionProgressSystem))]
public partial struct ProductionStartSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        foreach ((RefRW<ActiveProduction> activeProduction,
                  RefRO<ProducerState> producerState,
                  DynamicBuffer<ProducerBuildQueueElement> queue,
                  DynamicBuffer<ProducerEvent> events,
                  Entity entity)
                 in SystemAPI.Query<RefRW<ActiveProduction>,
                                    RefRO<ProducerState>,
                                    DynamicBuffer<ProducerBuildQueueElement>,
                                    DynamicBuffer<ProducerEvent>>().WithEntityAccess())
        {
            if (activeProduction.ValueRO.isActive || queue.Length == 0)
                continue;

            if (!ProductionUtility.CanProducerWork(entityManager, entity, producerState.ValueRO, out _))
                continue;

            ProducerBuildQueueElement queueElement = queue[0];

            if (!IsQueueElementValid(entityManager, queueElement))
            {
                queue.RemoveAt(0);
                events.Add(new ProducerEvent
                {
                    type = (byte)ProducerEventType.QueueRejected,
                    shipId = queueElement.shipId,
                    reason = (byte)ProducerRejectReason.InvalidPrefab,
                });
                continue;
            }

            activeProduction.ValueRW.isActive = true;
            activeProduction.ValueRW.shipId = queueElement.shipId;
            activeProduction.ValueRW.productKind = queueElement.productKind;
            activeProduction.ValueRW.prefab = queueElement.prefab;
            activeProduction.ValueRW.timer = math.max(0f, queueElement.buildTime);
            activeProduction.ValueRW.totalTime = math.max(0f, queueElement.buildTime);

            queue.RemoveAt(0);

            events.Add(new ProducerEvent
            {
                type = (byte)ProducerEventType.Started,
                shipId = queueElement.shipId,
                reason = (byte)ProducerRejectReason.None,
            });
        }
    }

    private static bool IsQueueElementValid(EntityManager entityManager, ProducerBuildQueueElement queueElement)
    {
        if (queueElement.productKind == ProductionProductKind.Ship)
        {
            return queueElement.prefab != Entity.Null;
        }

        if (queueElement.productKind == ProductionProductKind.Squad)
        {
            return ShipCatalogApi.HasValidSquadComposition(entityManager, queueElement.shipId);
        }

        return false;
    }
}