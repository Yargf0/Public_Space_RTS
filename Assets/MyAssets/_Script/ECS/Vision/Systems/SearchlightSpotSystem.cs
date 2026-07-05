using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ShipToGridSystem))]
partial struct SearchlightSpotSystem : ISystem
{
    private Entity gridEntity;
    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<Visibility> visibilityLookup;
    private bool getData;

    // max scans per frame so one wave don't scan all at once
    private const int MaxSearchlightScansPerFrame = 64;
    private int remainingScanBudget;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        getData = false;
        gridDataLookup = state.GetComponentLookup<GridData>(isReadOnly: true);
        visibilityLookup = state.GetComponentLookup<Visibility>(isReadOnly: false);

        state.RequireForUpdate<GridData>();
    }

    // random interval per light (0.9..1.1) so timers don't fire together
    private static float JitteredInterval(Entity entity, float interval)
    {
        uint hash = math.hash(new uint2((uint)entity.Index, (uint)entity.Version));
        float t = (hash & 1023u) / 1023f;
        return interval * math.lerp(0.9f, 1.1f, t);
    }

    // first timer random, must not be 0 or seed check runs again
    private static float InitialTimerSeed(Entity entity, float interval)
    {
        uint hash = math.hash(new uint2((uint)entity.Index * 0x9E3779B9u + 1u, (uint)entity.Version));
        float t = ((hash & 1023u) + 1u) / 1024f;
        return interval * t;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!getData)
        {
            gridEntity = SystemAPI.GetSingletonEntity<GridData>();
            getData = true;
        }

        gridDataLookup.Update(ref state);
        visibilityLookup.Update(ref state);

        GridData gridData = gridDataLookup.GetRefRO(gridEntity).ValueRO;
        float dt = SystemAPI.Time.DeltaTime;
        remainingScanBudget = MaxSearchlightScansPerFrame;

        foreach ((RefRO<LocalToWorld> ltw, RefRO<Searchlight> light, RefRW<SearchlightState> lightState, Entity entity)
            in SystemAPI.Query<RefRO<LocalToWorld>, RefRO<Searchlight>, RefRW<SearchlightState>>()
                .WithEntityAccess())
        {
            float scanInterval = light.ValueRO.scanInterval;

            // new light, timer 0, random start. scanInterval <= 0 = every frame
            if (scanInterval > 0f && lightState.ValueRO.ScanTimer == 0f)
            {
                lightState.ValueRW.ScanTimer = InitialTimerSeed(entity, scanInterval);
                continue;
            }

            float scanTimer = lightState.ValueRO.ScanTimer - dt;
            if (scanTimer > 0f)
            {
                lightState.ValueRW.ScanTimer = scanTimer;
                continue;
            }

            // no budget, retry next frame
            if (remainingScanBudget <= 0)
            {
                lightState.ValueRW.ScanTimer = math.min(scanTimer, -1e-4f);
                continue;
            }

            remainingScanBudget--;
            lightState.ValueRW.ScanTimer = scanInterval > 0f ? JitteredInterval(entity, scanInterval) : 0f;

            float2 lightPos = ltw.ValueRO.Position.xy;
            float range = light.ValueRO.range;
            float rangeSq = range * range;

            float3 fwd3 = math.mul(ltw.ValueRO.Rotation, new float3(0f, 1f, 0f));
            float2 forward = math.normalizesafe(fwd3.xy, new float2(0f, 1f));

            float coneAngleDeg = light.ValueRO.coneAngle;
            bool isCircle = coneAngleDeg >= 359.9f;
            float cosHalfAngle = 0f;

            if (!isCircle)
            {
                float halfRad = math.radians(coneAngleDeg * 0.5f);
                cosHalfAngle = math.cos(halfRad);
            }

            int2 minCell = GridUtility.WorldToSmallCell(lightPos - range);
            int2 maxCell = GridUtility.WorldToSmallCell(lightPos + range);
            NativeParallelMultiHashMap<int2, Grid> map = CombatUtility.GetEntityMap(in gridData, light.ValueRO.scansFaction);

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    if (!map.TryGetFirstValue(new int2(x, y), out Grid grid, out NativeParallelMultiHashMapIterator<int2> it))
                    {
                        continue;
                    }

                    do
                    {
                        float2 toTarget = grid.Position - lightPos;
                        float distSq = math.lengthsq(toTarget);

                        if (distSq > rangeSq)
                        {
                            continue;
                        }

                        if (!isCircle)
                        {
                            float2 dir = math.normalizesafe(toTarget, new float2(0f, 0f));
                            float dot = math.dot(forward, dir);

                            if (dot < cosHalfAngle)
                            {
                                continue;
                            }
                        }

                        VisibilityUtility.RefreshVisibilityForFaction(
                            grid.Entity,
                            light.ValueRO.observerFaction,
                            light.ValueRO.keepVisibleSeconds,
                            ref visibilityLookup);
                    }
                    while (map.TryGetNextValue(out grid, ref it));
                }
            }
        }
    }
}
