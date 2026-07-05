using Unity.Collections;
using Unity.Entities;

public static class StrikeGroupLookup
{
    public static Entity FindByKey(
        EntityManager em,
        int groupId,
        Faction faction,
        StrikeGroupOwnership ownership,
        Entity ownerEntity)
    {
        if (groupId == 0) return Entity.Null;

        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadOnly<StrikeGroupTag>(),
            ComponentType.ReadOnly<StrikeGroupData>());

        NativeArray<Entity> all = query.ToEntityArray(Allocator.Temp);
        Entity result = Entity.Null;

        for (int i = 0; i < all.Length; i++)
        {
            StrikeGroupData data = em.GetComponentData<StrikeGroupData>(all[i]);
            if (data.groupId == groupId &&
                data.faction == faction &&
                data.ownership == ownership &&
                data.ownerEntity == ownerEntity)
            {
                result = all[i];
                break;
            }
        }

        all.Dispose();
        query.Dispose();
        return result;
    }

    public static Entity FindDirectorGroup(EntityManager em, int groupId, Faction faction, Entity directorEntity)
    {
        return FindByKey(em, groupId, faction, StrikeGroupOwnership.Director, directorEntity);
    }

    public static Entity FindPlayerGroup(EntityManager em, int groupId, Faction faction)
    {
        if (groupId == 0) return Entity.Null;

        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadOnly<StrikeGroupTag>(),
            ComponentType.ReadOnly<StrikeGroupData>());

        NativeArray<Entity> all = query.ToEntityArray(Allocator.Temp);
        Entity result = Entity.Null;

        for (int i = 0; i < all.Length; i++)
        {
            StrikeGroupData data = em.GetComponentData<StrikeGroupData>(all[i]);
            if (data.groupId == groupId &&
                data.faction == faction &&
                data.ownership == StrikeGroupOwnership.Player)
            {
                result = all[i];
                break;
            }
        }

        all.Dispose();
        query.Dispose();
        return result;
    }

    public static bool TryGetDirectorData(
        EntityManager em,
        int groupId,
        Faction faction,
        Entity directorEntity,
        out StrikeGroupData data)
    {
        Entity e = FindDirectorGroup(em, groupId, faction, directorEntity);
        if (e == Entity.Null)
        {
            data = default;
            return false;
        }

        data = em.GetComponentData<StrikeGroupData>(e);
        return true;
    }
}
