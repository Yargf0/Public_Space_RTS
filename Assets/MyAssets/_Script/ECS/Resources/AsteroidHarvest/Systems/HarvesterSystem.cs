using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Harvesters use a two-step loop: find a nearby asteroid, then gather by tick.

#region Find Target

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AsteroidGridRegisterSystem))]
public partial struct HarvesterFindTargetSystem : ISystem
{
    private NativeList<AsteroidGridEntry> candidates;
    private ComponentLookup<AsteroidData> asteroidLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        candidates = new NativeList<AsteroidGridEntry>(32, Allocator.Persistent);
        asteroidLookup = state.GetComponentLookup<AsteroidData>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);

        state.RequireForUpdate<AsteroidGridData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        asteroidLookup.Update(ref state);
        transformLookup.Update(ref state);

        AsteroidGridData gridData = SystemAPI.GetSingleton<AsteroidGridData>();

        foreach ((RefRW<HarvesterShip> harvester, RefRO<LocalTransform> transform)
                 in SystemAPI.Query<RefRW<HarvesterShip>, RefRO<LocalTransform>>()
                     .WithAll<Friendly>())
        {
            if (IsTargetValid(harvester.ValueRO.targetAsteroid, transform.ValueRO.Position.xy, harvester.ValueRO.harvestRadius))
            {
                continue;
            }

            harvester.ValueRW.targetAsteroid = Entity.Null;

            harvester.ValueRW.searchTimer -= dt;
            if (harvester.ValueRO.searchTimer > 0f)
            {
                continue;
            }
            harvester.ValueRW.searchTimer = harvester.ValueRO.searchInterval;

            float2 pos = transform.ValueRO.Position.xy;
            float radius = harvester.ValueRO.harvestRadius;

            int2 minCell = GridUtility.WorldToSmallCell(pos - radius);
            int2 maxCell = GridUtility.WorldToSmallCell(pos + radius);

            candidates.Clear();

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    if (!gridData.Map.TryGetFirstValue(new int2(x, y), out AsteroidGridEntry entry, out var it))
                    {
                        continue;
                    }
                    do
                    {
                        candidates.Add(entry);
                    }
                    while (gridData.Map.TryGetNextValue(out entry, ref it));
                }
            }

            Entity bestEntity = Entity.Null;
            float bestDistSq = float.MaxValue;
            float radiusSq = radius * radius;

            for (int i = 0; i < candidates.Length; i++)
            {
                float distSq = math.distancesq(candidates[i].Position, pos);
                if (distSq <= radiusSq && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestEntity = candidates[i].Entity;
                }
            }

            harvester.ValueRW.targetAsteroid = bestEntity;
        }
    }

    private bool IsTargetValid(Entity target, float2 harvesterPos, float radius)
    {
        if (target == Entity.Null)
        {
            return false;
        }

        if (!asteroidLookup.HasComponent(target))
        {
            return false;
        }

        if (asteroidLookup[target].currentAmount <= 0f)
        {
            return false;
        }

        if (!transformLookup.HasComponent(target))
        {
            return false;
        }

        float2 asteroidPos = transformLookup[target].Position.xy;
        return math.distancesq(asteroidPos, harvesterPos) <= radius * radius;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (candidates.IsCreated)
        {
            candidates.Dispose();
        }
    }
}

#endregion

#region Gather

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HarvesterFindTargetSystem))]
public partial struct HarvesterGatherSystem : ISystem
{
    private ComponentLookup<AsteroidData> asteroidLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        asteroidLookup = state.GetComponentLookup<AsteroidData>(false);
        state.RequireForUpdate<ResourceData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        asteroidLookup.Update(ref state);

        RefRW<ResourceData> resourceData = SystemAPI.GetSingletonRW<ResourceData>();

        foreach (RefRW<HarvesterShip> harvester
                 in SystemAPI.Query<RefRW<HarvesterShip>>()
                     .WithAll<Friendly>())
        {
            Entity target = harvester.ValueRO.targetAsteroid;
            if (target == Entity.Null || !asteroidLookup.HasComponent(target))
            {
                continue;
            }

            harvester.ValueRW.tickTimer -= dt;
            if (harvester.ValueRO.tickTimer > 0f)
            {
                continue;
            }
            harvester.ValueRW.tickTimer = harvester.ValueRO.tickInterval;

            AsteroidData asteroid = asteroidLookup[target];
            if (asteroid.currentAmount <= 0f)
            {
                harvester.ValueRW.targetAsteroid = Entity.Null;
                continue;
            }

            float gathered = math.min(harvester.ValueRO.amountPerTick, asteroid.currentAmount);

            asteroid.currentAmount -= gathered;
            asteroidLookup[target] = asteroid;

            ResourceData res = resourceData.ValueRO;
            switch (asteroid.resourceType)
            {
                case AsteroidResourceType.Metal:
                    res.metal += gathered;
                    break;
                case AsteroidResourceType.Crystal:
                    res.crystal += gathered;
                    break;
            }
            resourceData.ValueRW = res;
        }
    }
}

#endregion
