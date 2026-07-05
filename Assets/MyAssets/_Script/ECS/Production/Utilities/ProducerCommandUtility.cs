using Unity.Entities;
using Unity.Mathematics;

public static class ProducerCommandUtility
{
    public static bool TryAddBuildRequest(EntityManager entityManager, Entity producerEntity, int shipId, int count = 1)
    {
        if (!entityManager.Exists(producerEntity))
            return false;

        if (!entityManager.HasBuffer<ProducerBuildRequest>(producerEntity))
            return false;

        DynamicBuffer<ProducerBuildRequest> requests = entityManager.GetBuffer<ProducerBuildRequest>(producerEntity);
        requests.Add(new ProducerBuildRequest
        {
            shipId = shipId,
            count = math.max(1, count),
        });

        return true;
    }

    public static bool TryRemoveQueueAtIndex(EntityManager entityManager, Entity producerEntity, int queueIndex)
    {
        if (!entityManager.Exists(producerEntity))
            return false;

        if (!entityManager.HasBuffer<ProducerBuildQueueElement>(producerEntity))
            return false;

        DynamicBuffer<ProducerBuildQueueElement> queue = entityManager.GetBuffer<ProducerBuildQueueElement>(producerEntity);
        if (queueIndex < 0 || queueIndex >= queue.Length)
            return false;

        queue.RemoveAt(queueIndex);
        return true;
    }

    public static bool TrySetRallyPoint(EntityManager entityManager, Entity producerEntity, float3 point)
    {
        if (!entityManager.Exists(producerEntity))
            return false;

        if (!entityManager.HasComponent<ProducerRallyPoint>(producerEntity))
            return false;

        ProducerRallyPoint rallyPoint = entityManager.GetComponentData<ProducerRallyPoint>(producerEntity);
        rallyPoint.mode = (byte)RallyPointMode.FollowPoint;
        rallyPoint.worldPoint = point;
        rallyPoint.followEntity = Entity.Null;
        entityManager.SetComponentData(producerEntity, rallyPoint);

        return true;
    }

    public static bool TrySetRallyFollow(EntityManager entityManager, Entity producerEntity, Entity followEntity)
    {
        if (!entityManager.Exists(producerEntity))
            return false;

        if (!entityManager.HasComponent<ProducerRallyPoint>(producerEntity))
            return false;

        ProducerRallyPoint rallyPoint = entityManager.GetComponentData<ProducerRallyPoint>(producerEntity);
        rallyPoint.mode = (byte)RallyPointMode.FollowEntity;
        rallyPoint.followEntity = followEntity;
        entityManager.SetComponentData(producerEntity, rallyPoint);

        return true;
    }
}