using Unity.Entities;

public static class ProductionUtility
{
    public static bool CanProducerWork(EntityManager entityManager, Entity producerEntity, ProducerState producerState, out ProducerRejectReason rejectReason)
    {
        if (!producerState.isEnabled)
        {
            rejectReason = ProducerRejectReason.ProducerDisabled;
            return false;
        }

        if (entityManager.HasComponent<Health>(producerEntity))
        {
            Health health = entityManager.GetComponentData<Health>(producerEntity);
            if (health.healthAmount <= 0f)
            {
                rejectReason = ProducerRejectReason.ProducerDead;
                return false;
            }
        }

        rejectReason = ProducerRejectReason.None;
        return true;
    }

    public static bool HasAllowedShip(DynamicBuffer<ProducerAllowedShipId> allowedShips, int shipId)
    {
        for (int i = 0; i < allowedShips.Length; i++)
        {
            if (allowedShips[i].shipId == shipId)
                return true;
        }

        return false;
    }

    public static bool CanAfford(ResourceData resourceData, Cost cost)
    {
        return resourceData.energy >= cost.Energy
            && resourceData.metal >= cost.Mineral
            && resourceData.crystal >= cost.Gas;
    }

    public static void Spend(ref ResourceData resourceData, Cost cost)
    {
        resourceData.energy -= cost.Energy;
        resourceData.metal -= cost.Mineral;
        resourceData.crystal -= cost.Gas;
    }

    public static void ApplyFactionToSpawnedEntity(EntityManager entityManager, Entity prefabEntity, Entity spawnedEntity, Faction faction, EntityCommandBuffer ecb)
    {
        if (entityManager.HasComponent<Unit>(prefabEntity))
        {
            Unit unit = entityManager.GetComponentData<Unit>(prefabEntity);
            unit.faction = faction;
            ecb.SetComponent(spawnedEntity, unit);
        }

        if (faction == Faction.Friendly)
        {
            if (entityManager.HasComponent<Enemy>(prefabEntity))
                ecb.RemoveComponent<Enemy>(spawnedEntity);

            if (!entityManager.HasComponent<Friendly>(prefabEntity))
                ecb.AddComponent<Friendly>(spawnedEntity);
        }
        else
        {
            if (entityManager.HasComponent<Friendly>(prefabEntity))
                ecb.RemoveComponent<Friendly>(spawnedEntity);

            if (!entityManager.HasComponent<Enemy>(prefabEntity))
                ecb.AddComponent<Enemy>(spawnedEntity);
        }
    }
}