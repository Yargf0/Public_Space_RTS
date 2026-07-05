using Unity.Entities;

[UpdateBefore(typeof(ProductionStartSystem))]
public partial struct ProductionRequestSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        if (!ShipCatalogApi.TryGetCatalogBuffer(entityManager, out DynamicBuffer<ShipCatalogElement> catalog))
            return;

        bool hasResourceData = SystemAPI.TryGetSingletonRW<ResourceData>(out RefRW<ResourceData> resourceData);

        foreach ((RefRO<ProducerConfig> config,
                  RefRO<ProducerState> producerState,
                  DynamicBuffer<ProducerAllowedShipId> allowedShips,
                  DynamicBuffer<ProducerBuildRequest> requests,
                  DynamicBuffer<ProducerBuildQueueElement> queue,
                  DynamicBuffer<ProducerEvent> events,
                  Entity entity)
                 in SystemAPI.Query<RefRO<ProducerConfig>,
                                    RefRO<ProducerState>,
                                    DynamicBuffer<ProducerAllowedShipId>,
                                    DynamicBuffer<ProducerBuildRequest>,
                                    DynamicBuffer<ProducerBuildQueueElement>,
                                    DynamicBuffer<ProducerEvent>>().WithEntityAccess())
        {
            for (int i = 0; i < requests.Length; i++)
            {
                ProducerBuildRequest request = requests[i];
                int requestCount = request.count < 1 ? 1 : request.count;

                for (int countIndex = 0; countIndex < requestCount; countIndex++)
                {
                    if (!ProductionUtility.CanProducerWork(entityManager, entity, producerState.ValueRO, out ProducerRejectReason rejectReason))
                    {
                        events.Add(new ProducerEvent
                        {
                            type = (byte)ProducerEventType.QueueRejected,
                            shipId = request.shipId,
                            reason = (byte)rejectReason,
                        });
                        continue;
                    }

                    if (!ShipCatalogApi.TryGetShip(catalog, request.shipId, out ShipCatalogElement ship))
                    {
                        events.Add(new ProducerEvent
                        {
                            type = (byte)ProducerEventType.QueueRejected,
                            shipId = request.shipId,
                            reason = (byte)ProducerRejectReason.ShipNotFound,
                        });
                        continue;
                    }

                    if (!ProductionUtility.HasAllowedShip(allowedShips, request.shipId))
                    {
                        events.Add(new ProducerEvent
                        {
                            type = (byte)ProducerEventType.QueueRejected,
                            shipId = request.shipId,
                            reason = (byte)ProducerRejectReason.ShipNotAllowed,
                        });
                        continue;
                    }

                    if (queue.Length >= config.ValueRO.queueCapacity)
                    {
                        events.Add(new ProducerEvent
                        {
                            type = (byte)ProducerEventType.QueueRejected,
                            shipId = request.shipId,
                            reason = (byte)ProducerRejectReason.QueueFull,
                        });
                        continue;
                    }

                    if (!IsCatalogProductValid(entityManager, ship))
                    {
                        events.Add(new ProducerEvent
                        {
                            type = (byte)ProducerEventType.QueueRejected,
                            shipId = request.shipId,
                            reason = (byte)ProducerRejectReason.InvalidPrefab,
                        });
                        continue;
                    }

                    if (!hasResourceData)
                    {
                        events.Add(new ProducerEvent
                        {
                            type = (byte)ProducerEventType.QueueRejected,
                            shipId = request.shipId,
                            reason = (byte)ProducerRejectReason.ResourceStateNotFound,
                        });
                        continue;
                    }

                    if (!ProductionUtility.CanAfford(resourceData.ValueRO, ship.cost))
                    {
                        events.Add(new ProducerEvent
                        {
                            type = (byte)ProducerEventType.QueueRejected,
                            shipId = request.shipId,
                            reason = (byte)ProducerRejectReason.NotEnoughResources,
                        });
                        continue;
                    }

                    ResourceData resourceValue = resourceData.ValueRO;
                    ProductionUtility.Spend(ref resourceValue, ship.cost);
                    resourceData.ValueRW = resourceValue;

                    queue.Add(new ProducerBuildQueueElement
                    {
                        shipId = request.shipId,
                        productKind = ship.productKind,
                        prefab = ship.prefab,
                        buildTime = ship.buildTime,
                        cost = ship.cost,
                    });

                    events.Add(new ProducerEvent
                    {
                        type = (byte)ProducerEventType.QueueAdded,
                        shipId = request.shipId,
                        reason = (byte)ProducerRejectReason.None,
                    });
                }
            }

            requests.Clear();
        }
    }

    private static bool IsCatalogProductValid(EntityManager entityManager, ShipCatalogElement product)
    {
        if (product.productKind == ProductionProductKind.Ship)
        {
            return product.prefab != Entity.Null;
        }

        if (product.productKind == ProductionProductKind.Squad)
        {
            return ShipCatalogApi.HasValidSquadComposition(entityManager, product.id);
        }

        return false;
    }
}