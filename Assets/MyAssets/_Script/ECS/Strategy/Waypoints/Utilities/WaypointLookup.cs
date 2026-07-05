using Unity.Collections;
using Unity.Entities;

public static class WaypointLookup
{
    public static Entity FindById(EntityManager em, int id)
    {
        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadOnly<WaypointTag>(),
            ComponentType.ReadOnly<Waypoint>());

        NativeArray<Entity> all = query.ToEntityArray(Allocator.Temp);
        Entity result = Entity.Null;

        for (int i = 0; i < all.Length; i++)
        {
            Waypoint waypoint = em.GetComponentData<Waypoint>(all[i]);
            if (waypoint.id == id)
            {
                result = all[i];
                break;
            }
        }

        all.Dispose();
        query.Dispose();
        return result;
    }

    public static bool TryGetData(EntityManager em, int id, out Waypoint data)
    {
        Entity e = FindById(em, id);
        if (e == Entity.Null)
        {
            data = default;
            return false;
        }

        data = em.GetComponentData<Waypoint>(e);
        return true;
    }
}
