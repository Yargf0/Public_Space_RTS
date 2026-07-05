using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public static class GridPickUtility
{
    public static bool TryPickShipAtScreenPoint(
        EntityManager em,
        Camera camera,
        Vector2 screenPoint,
        out Entity hitEntity)
    {
        hitEntity = Entity.Null;

        if (camera == null)
        {
            return false;
        }

        Vector3 screen = new Vector3(screenPoint.x, screenPoint.y, Mathf.Abs(camera.transform.position.z));
        Vector3 world = camera.ScreenToWorldPoint(screen);
        return TryPickShipAtWorldPoint(em, new float2(world.x, world.y), out hitEntity);
    }

    public static bool TryPickShipAtWorldPoint(
        EntityManager em,
        float2 worldPoint,
        out Entity hitEntity)
    {
        hitEntity = Entity.Null;

        if (!TryGetGridData(em, out GridData gridData))
        {
            return false;
        }

        return TryPickShipAtWorldPoint(em, in gridData, worldPoint, out hitEntity);
    }

    public static bool TryPickShipAtWorldPoint(
        EntityManager em,
        in GridData gridData,
        float2 worldPoint,
        out Entity hitEntity)
    {
        hitEntity = Entity.Null;

        int2 centerCell = GridUtility.WorldToSmallCell(worldPoint);
        Entity bestEntity = Entity.Null;
        int bestPriority = int.MaxValue;
        float bestDistanceSq = float.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int2 cell = new int2(centerCell.x + dx, centerCell.y + dy);

                TryPickFromMap(
                    em,
                    gridData.FriendlyEntityMap,
                    cell,
                    worldPoint,
                    ref bestEntity,
                    ref bestPriority,
                    ref bestDistanceSq);

                TryPickFromMap(
                    em,
                    gridData.EnemyEntityMap,
                    cell,
                    worldPoint,
                    ref bestEntity,
                    ref bestPriority,
                    ref bestDistanceSq);
            }
        }

        hitEntity = bestEntity;
        return hitEntity != Entity.Null;
    }

    public static int GetPickPriority(ShipSize size)
    {
        if ((size & ShipSize.RocketSmall) != 0) return 0;
        if ((size & ShipSize.Small) != 0) return 1;
        if ((size & ShipSize.Medium) != 0) return 2;
        if ((size & ShipSize.RocketBig) != 0) return 3;
        if ((size & ShipSize.Big) != 0) return 4;

        return 100;
    }

    // fallback for click paths. hover should use overload with cached GridData
    private static bool TryGetGridData(EntityManager em, out GridData gridData)
    {
        EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<GridData>());

        if (query.IsEmpty)
        {
            query.Dispose();
            gridData = default;
            return false;
        }

        gridData = query.GetSingleton<GridData>();
        query.Dispose();
        return true;
    }

    private static void TryPickFromMap(
        EntityManager em,
        NativeParallelMultiHashMap<int2, Grid> map,
        int2 cell,
        float2 worldPoint,
        ref Entity bestEntity,
        ref int bestPriority,
        ref float bestDistanceSq)
    {
        if (!map.IsCreated)
        {
            return;
        }

        if (!map.TryGetFirstValue(cell, out Grid candidate, out var iterator))
        {
            return;
        }

        do
        {
            if (!ObbContains(em, in candidate, worldPoint))
            {
                continue;
            }

            int priority = GetPickPriority((ShipSize)candidate.ShipSize);
            float distanceSq = math.distancesq(worldPoint, candidate.Position);

            if (priority < bestPriority ||
                (priority == bestPriority && distanceSq < bestDistanceSq))
            {
                bestPriority = priority;
                bestDistanceSq = distanceSq;
                bestEntity = candidate.Entity;
            }
        }
        while (map.TryGetNextValue(out candidate, ref iterator));
    }

    private static bool ObbContains(
        EntityManager em,
        in Grid candidate,
        float2 worldPoint)
    {
        if (candidate.Entity == Entity.Null || !em.Exists(candidate.Entity))
        {
            return false;
        }

        if (!em.HasComponent<LocalTransform>(candidate.Entity))
        {
            return false;
        }

        LocalTransform transform = em.GetComponentData<LocalTransform>(candidate.Entity);
        quaternion rotation = transform.Rotation;

        float3 fwd3 = math.mul(rotation, new float3(0f, 1f, 0f));
        float2 fwd = math.normalizesafe(fwd3.xy, new float2(0f, 1f));
        float2 right = new float2(fwd.y, -fwd.x);

        float2 local = worldPoint - candidate.Position;
        float along = math.dot(local, fwd);
        float across = math.dot(local, right);

        return math.abs(along) <= candidate.CollisionRadius.y &&
               math.abs(across) <= candidate.CollisionRadius.x;
    }
}
