using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// boids (separation/cohesion/alignment) over spatial hash from ShipToGridSystem
// small ships update in turns, big ones every frame but separation only
[UpdateAfter(typeof(ShipToGridSystem))]
[UpdateBefore(typeof(SetTotalVelocitySystem))]
[BurstCompile]
public partial struct BoidSystem : ISystem
{
    private const float MinDistanceSq = 0.0001f;
    private const float WriteEpsilonSq = 0.0001f;
    private const float BigSeparationMaxLength = 1f;
    private const int MaxBigCellRange = 3;
    private const int MaxBigBoidNeighbors = 16;
    private const int SmallMediumStaggerFrameCount = 2;
    private const int BigStaggerFrameCount = 1;
    private const byte LargeBoidShipMask = (byte)ShipSize.Big;

    private uint frameIndex;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GridData gridData = SystemAPI.GetSingleton<GridData>();

        if (!gridData.EnemyEntityMap.IsCreated ||
            !gridData.FriendlyEntityMap.IsCreated ||
            !gridData.EnemyEntityBigMap.IsCreated ||
            !gridData.FriendlyEntityBigMap.IsCreated)
        {
            return;
        }

        uint currentFrame = frameIndex++;

        BoidJob job = new BoidJob
        {
            EnemyEntityMap = gridData.EnemyEntityMap,
            FriendlyEntityMap = gridData.FriendlyEntityMap,
            EnemyEntityBigMap = gridData.EnemyEntityBigMap,
            FriendlyEntityBigMap = gridData.FriendlyEntityBigMap,
            FrameIndex = currentFrame,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct BoidJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int2, Grid> EnemyEntityMap;
        [ReadOnly] public NativeParallelMultiHashMap<int2, Grid> FriendlyEntityMap;
        [ReadOnly] public NativeParallelMultiHashMap<int2, Grid> EnemyEntityBigMap;
        [ReadOnly] public NativeParallelMultiHashMap<int2, Grid> FriendlyEntityBigMap;

        public uint FrameIndex;

        private void Execute(
            Entity myEntity,
            in LocalTransform localTransform,
            in Boid boid,
            ref Velocity velocity,
            in Unit unit,
            in UnitCollisionRadius collisionRadius)
        {
            Velocity currentVelocity = velocity;
            float perceptionRadius = boid.neighborRadius;

            if (perceptionRadius <= 0f)
            {
                WriteBoidVelocitiesIfChanged(ref velocity, currentVelocity, float2.zero, float2.zero, float2.zero);
                return;
            }

            byte myShipSize = unit.shipSize;
            bool useBigGrid = IsLargeBoidShip(myShipSize);

            if (!ShouldUpdateThisFrame(myEntity.Index, useBigGrid, FrameIndex))
            {
                return;
            }

            float2 position = localTransform.Position.xy;
            if (!math.isfinite(position.x) || !math.isfinite(position.y))
            {
                WriteBoidVelocitiesIfChanged(ref velocity, currentVelocity, float2.zero, float2.zero, float2.zero);
                return;
            }

            NativeParallelMultiHashMap<int2, Grid> map = unit.faction == Faction.Enemy
                ? (useBigGrid ? EnemyEntityBigMap : EnemyEntityMap)
                : (useBigGrid ? FriendlyEntityBigMap : FriendlyEntityMap);

            float perceptionRadiusSq = perceptionRadius * perceptionRadius;
            float myRadius = RadiusFromHalfExtents(collisionRadius.collisionRadius);

            int2 centerCell = useBigGrid
                ? GridUtility.WorldToBigCell(position)
                : GridUtility.WorldToSmallCell(position);

            int cellRange = useBigGrid ? GetBigCellRange(perceptionRadius) : 1;

            float2 separation = float2.zero;
            float2 alignment = float2.zero;
            float2 cohesion = float2.zero;
            int neighborCount = 0;
            int alignmentCount = 0;
            bool stopBigScan = false;

            for (int dy = -cellRange; dy <= cellRange; dy++)
            {
                for (int dx = -cellRange; dx <= cellRange; dx++)
                {
                    int2 cell = centerCell + new int2(dx, dy);

                    if (!map.TryGetFirstValue(cell, out Grid neighbor, out NativeParallelMultiHashMapIterator<int2> iterator))
                    {
                        continue;
                    }

                    do
                    {
                        if (neighbor.Entity == myEntity)
                        {
                            continue;
                        }

                        if (!ShouldUseNeighborForBoid(myShipSize, neighbor.ShipSize))
                        {
                            continue;
                        }

                        float2 diff = position - neighbor.Position;
                        float distSq = math.lengthsq(diff);

                        if (distSq > perceptionRadiusSq)
                        {
                            continue;
                        }

                        if (distSq < MinDistanceSq)
                        {
                            if (!useBigGrid)
                            {
                                continue;
                            }

                            diff = GetFallbackAway(myEntity.Index, neighbor.Entity.Index);
                            distSq = MinDistanceSq;
                        }

                        neighborCount++;

                        float dist = math.sqrt(distSq);
                        float2 away = diff / dist;
                        float neighborRadius = RadiusFromHalfExtents(neighbor.CollisionRadius);
                        float combinedRadius = myRadius + neighborRadius;

                        float perception01 = math.saturate((perceptionRadius - dist) / math.max(perceptionRadius, 0.0001f));
                        float overlap01 = combinedRadius > 0.0001f
                            ? math.saturate((combinedRadius - dist) / combinedRadius)
                            : 0f;

                        float separationStrength = 0.25f + perception01 + overlap01 * 3f;
                        separation += away * separationStrength;

                        if (!useBigGrid)
                        {
                            cohesion += neighbor.Position;

                            if (math.lengthsq(neighbor.Heading) > MinDistanceSq)
                            {
                                alignment += neighbor.Heading;
                                alignmentCount++;
                            }
                        }

                        if (useBigGrid && neighborCount >= MaxBigBoidNeighbors)
                        {
                            stopBigScan = true;
                            break;
                        }
                    }
                    while (map.TryGetNextValue(out neighbor, ref iterator));

                    if (stopBigScan)
                    {
                        break;
                    }
                }

                if (stopBigScan)
                {
                    break;
                }
            }

            float2 nextSeparationVelocity;
            float2 nextAlignmentVelocity = float2.zero;
            float2 nextCohesionVelocity = float2.zero;

            if (neighborCount == 0)
            {
                nextSeparationVelocity = float2.zero;
            }
            else if (useBigGrid)
            {
                nextSeparationVelocity = ClampLength(separation, BigSeparationMaxLength) * boid.separationWeight;
            }
            else
            {
                nextSeparationVelocity = math.normalizesafe(separation) * boid.separationWeight;

                float2 centerOfMass = cohesion / neighborCount;
                float2 toCenter = centerOfMass - position;

                if (math.lengthsq(toCenter) > 0.01f)
                {
                    nextCohesionVelocity = math.normalizesafe(toCenter) * boid.cohesionWeight;
                }

                float2 myHeading = float2.zero;

                if (math.lengthsq(currentVelocity.velocity) > MinDistanceSq)
                {
                    myHeading = math.normalize(currentVelocity.velocity);
                }
                else if (math.lengthsq(currentVelocity.flowFieldVelocity) > MinDistanceSq)
                {
                    myHeading = math.normalize(currentVelocity.flowFieldVelocity);
                }

                if (alignmentCount > 0)
                {
                    float2 avgNeighborHeading = math.normalizesafe(alignment / alignmentCount);
                    float2 alignmentSteer = avgNeighborHeading - myHeading;
                    nextAlignmentVelocity = math.normalizesafe(alignmentSteer) * boid.alignmentWeight;
                }
            }

            WriteBoidVelocitiesIfChanged(ref velocity, currentVelocity, nextSeparationVelocity, nextAlignmentVelocity, nextCohesionVelocity);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLargeBoidShip(byte shipSize)
    {
        return (shipSize & LargeBoidShipMask) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldUseNeighborForBoid(byte myShipSize, byte neighborShipSize)
    {
        if (!IsLargeBoidShip(myShipSize))
        {
            return true;
        }

        return IsLargeBoidShip(neighborShipSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldUpdateThisFrame(int entityIndex, bool useBigGrid, uint frameIndex)
    {
        int staggerFrameCount = useBigGrid ? BigStaggerFrameCount : SmallMediumStaggerFrameCount;

        if (staggerFrameCount <= 1)
        {
            return true;
        }

        uint stableIndex = (uint)entityIndex;
        return ((stableIndex + frameIndex) % (uint)staggerFrameCount) == 0u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBigCellRange(float neighborRadius)
    {
        int range = (int)math.ceil(math.max(0f, neighborRadius) / math.max(0.0001f, GameConstants.BigGridCellSize));
        return math.clamp(range, 1, MaxBigCellRange);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float RadiusFromHalfExtents(float2 halfExtents)
    {
        return math.cmax(math.abs(halfExtents));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 ClampLength(float2 value, float maxLength)
    {
        float lenSq = math.lengthsq(value);
        float maxLenSq = maxLength * maxLength;

        if (lenSq <= maxLenSq || lenSq <= MinDistanceSq)
        {
            return value;
        }

        return value * math.rsqrt(lenSq) * maxLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 GetFallbackAway(int myEntityIndex, int neighborEntityIndex)
    {
        uint hash = math.hash(new int2(myEntityIndex, neighborEntityIndex));
        float angle = (float)(hash & 1023u) * (math.PI * 2f / 1024f);
        math.sincos(angle, out float s, out float c);
        return new float2(c, s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Different(float2 a, float2 b)
    {
        return math.lengthsq(a - b) > WriteEpsilonSq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldWriteBoidVelocities(Velocity current, float2 separation, float2 alignment, float2 cohesion)
    {
        return Different(current.separationVelocity, separation)
            || Different(current.alignmentVelocity, alignment)
            || Different(current.cohesionVelocity, cohesion);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBoidVelocitiesIfChanged(ref Velocity velocity, Velocity current, float2 separation, float2 alignment, float2 cohesion)
    {
        if (!ShouldWriteBoidVelocities(current, separation, alignment, cohesion))
        {
            return;
        }

        velocity.separationVelocity = separation;
        velocity.alignmentVelocity = alignment;
        velocity.cohesionVelocity = cohesion;
    }
}
