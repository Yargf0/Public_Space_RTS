using Unity.Entities;

public static class ShipCatalogApi
{
    public static bool TryGetCatalogEntity(EntityManager entityManager, out Entity catalogEntity)
    {
        EntityQuery query = entityManager.CreateEntityQuery(typeof(ShipCatalogTag), typeof(ShipCatalogElement));

        if (!query.TryGetSingletonEntity<ShipCatalogTag>(out catalogEntity))
        {
            catalogEntity = Entity.Null;
            return false;
        }

        return true;
    }

    public static bool TryGetCatalogBuffer(EntityManager entityManager, out DynamicBuffer<ShipCatalogElement> buffer)
    {
        EntityQuery query = entityManager.CreateEntityQuery(typeof(ShipCatalogTag), typeof(ShipCatalogElement));

        if (!query.TryGetSingletonBuffer<ShipCatalogElement>(out buffer))
        {
            buffer = default;
            return false;
        }

        return true;
    }

    public static bool TryGetSquadMemberBuffer(
        EntityManager entityManager,
        out DynamicBuffer<ShipCatalogSquadMemberElement> buffer)
    {
        EntityQuery query = entityManager.CreateEntityQuery(
            typeof(ShipCatalogTag),
            typeof(ShipCatalogSquadMemberElement));

        if (!query.TryGetSingletonBuffer<ShipCatalogSquadMemberElement>(out buffer))
        {
            buffer = default;
            return false;
        }

        return true;
    }

    public static bool TryGetShip(EntityManager entityManager, int shipId, out ShipCatalogElement ship)
    {
        if (!TryGetCatalogBuffer(entityManager, out DynamicBuffer<ShipCatalogElement> buffer))
        {
            ship = default;
            return false;
        }

        return TryGetShip(buffer, shipId, out ship);
    }

    public static bool TryGetShip(DynamicBuffer<ShipCatalogElement> buffer, int shipId, out ShipCatalogElement ship)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].id == shipId)
            {
                ship = buffer[i];
                return true;
            }
        }

        ship = default;
        return false;
    }

    public static bool HasValidSquadComposition(EntityManager entityManager, int productId)
    {
        if (!TryGetSquadMemberBuffer(entityManager, out DynamicBuffer<ShipCatalogSquadMemberElement> members))
        {
            return false;
        }

        return HasValidSquadComposition(members, productId);
    }

    public static bool HasValidSquadComposition(
        DynamicBuffer<ShipCatalogSquadMemberElement> members,
        int productId)
    {
        for (int i = 0; i < members.Length; i++)
        {
            ShipCatalogSquadMemberElement member = members[i];

            if (member.productId == productId &&
                member.prefab != Entity.Null &&
                member.count > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static int FillSquadRequestMembers(
        DynamicBuffer<ShipCatalogSquadMemberElement> sourceMembers,
        DynamicBuffer<SpawnSquadRequestMemberElement> targetMembers,
        int productId)
    {
        int added = 0;

        for (int i = 0; i < sourceMembers.Length; i++)
        {
            ShipCatalogSquadMemberElement source = sourceMembers[i];

            if (source.productId != productId)
                continue;

            if (source.prefab == Entity.Null || source.count <= 0)
                continue;

            targetMembers.Add(new SpawnSquadRequestMemberElement
            {
                prefab = source.prefab,
                count = source.count,
                memberPrefabIndex = source.memberPrefabIndex,
            });

            added++;
        }

        return added;
    }
}