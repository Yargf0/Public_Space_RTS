using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// soft body solver after movement, only Big vs Big overlap
// small ships can fly through big hulls. small steps to avoid jitter
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MoveVelocitySystem))]
[UpdateBefore(typeof(RotateToMovementSystem))]
public partial struct ShipBodySeparationSystem : ISystem
{
    private const float MinDistanceSq = 0.0001f;
    private const float BodySlop = 0.75f;
    private const float CorrectionPercent = 0.16f;
    private const float MaxCorrectionPerFrame = 0.45f;
    private const int MaxBigCellRange = 3;
    private const byte LargeShipMask = (byte)(ShipSize.Big | ShipSize.RocketBig);

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GridData gridData = SystemAPI.GetSingleton<GridData>();

        foreach ((
            RefRW<LocalTransform> localTransform,
            RefRW<Velocity> velocity,
            RefRO<Unit> unit,
            RefRO<UnitCollisionRadius> collisionRadius,
            Entity entity)
            in SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<Velocity>,
                RefRO<Unit>,
                RefRO<UnitCollisionRadius>>()
                .WithAll<ShipToGrid>()
                .WithAbsent<SquadronTag>()
                .WithAbsent<Rocket>()
                .WithAbsent<Bullet>()
                .WithEntityAccess())
        {
            if (!IsLargeShip(unit.ValueRO.shipSize))
                continue;

            float myRadius = RadiusFromHalfExtents(collisionRadius.ValueRO.collisionRadius);
            if (myRadius <= 0.0001f)
                continue;

            float3 worldPos3 = localTransform.ValueRO.Position;
            float2 position = worldPos3.xy;

            if (!math.isfinite(position.x) || !math.isfinite(position.y))
                continue;

            int2 centerCell = GridUtility.WorldToBigCell(position);
            int cellRange = GetBigCellRange(myRadius);

            float2 correction = float2.zero;
            int correctionCount = 0;

            ScanMap(entity, position, myRadius, centerCell, cellRange, gridData.FriendlyEntityBigMap, ref correction, ref correctionCount);
            ScanMap(entity, position, myRadius, centerCell, cellRange, gridData.EnemyEntityBigMap, ref correction, ref correctionCount);

            if (correctionCount == 0)
                continue;

            float correctionLengthSq = math.lengthsq(correction);
            if (correctionLengthSq <= MinDistanceSq)
                continue;

            float maxCorrection = math.max(0.05f, math.min(MaxCorrectionPerFrame, myRadius * 0.06f));
            float maxCorrectionSq = maxCorrection * maxCorrection;
            if (correctionLengthSq > maxCorrectionSq)
            {
                correction = math.normalizesafe(correction) * maxCorrection;
                correctionLengthSq = maxCorrectionSq;
            }

            worldPos3.x += correction.x;
            worldPos3.y += correction.y;
            worldPos3.z = GameConstants.GetShipZ(unit.ValueRO.shipSize);
            localTransform.ValueRW.Position = worldPos3;

            float2 correctionDir = correction / math.sqrt(correctionLengthSq);
            float2 currentVelocity = velocity.ValueRO.velocity;
            if (!math.isfinite(currentVelocity.x) || !math.isfinite(currentVelocity.y))
            {
                velocity.ValueRW.velocity = float2.zero;
                continue;
            }

            float inwardSpeed = -math.dot(currentVelocity, correctionDir);
            if (inwardSpeed > 0f)
                velocity.ValueRW.velocity = currentVelocity + correctionDir * inwardSpeed;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLargeShip(byte shipSize)
    {
        return (shipSize & LargeShipMask) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBigCellRange(float radius)
    {
        float scanRadius = math.max(GameConstants.BigGridCellSize, radius * 2f);
        int range = (int)math.ceil(scanRadius / math.max(0.0001f, GameConstants.BigGridCellSize));
        return math.clamp(range, 1, MaxBigCellRange);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float RadiusFromHalfExtents(float2 halfExtents)
    {
        return math.cmax(math.abs(halfExtents));
    }

    private static void ScanMap(
        Entity self,
        float2 position,
        float myRadius,
        int2 centerCell,
        int cellRange,
        NativeParallelMultiHashMap<int2, Grid> map,
        ref float2 correction,
        ref int correctionCount)
    {
        for (int dy = -cellRange; dy <= cellRange; dy++)
        {
            for (int dx = -cellRange; dx <= cellRange; dx++)
            {
                int2 cell = centerCell + new int2(dx, dy);
                if (!map.TryGetFirstValue(cell, out Grid neighbor, out NativeParallelMultiHashMapIterator<int2> iterator))
                    continue;

                do
                {
                    ResolveNeighbor(self, position, myRadius, neighbor, ref correction, ref correctionCount);
                }
                while (map.TryGetNextValue(out neighbor, ref iterator));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResolveNeighbor(
        Entity self,
        float2 position,
        float myRadius,
        Grid neighbor,
        ref float2 correction,
        ref int correctionCount)
    {
        if (neighbor.Entity == self)
            return;

        if (!IsLargeShip(neighbor.ShipSize))
            return;

        float neighborRadius = RadiusFromHalfExtents(neighbor.CollisionRadius);
        if (neighborRadius <= 0.0001f)
            return;

        float desiredDistance = myRadius + neighborRadius;
        if (desiredDistance <= 0.0001f)
            return;

        float2 diff = position - neighbor.Position;
        float distSq = math.lengthsq(diff);
        float desiredDistanceSq = desiredDistance * desiredDistance;

        if (distSq >= desiredDistanceSq)
            return;

        float dist;
        float2 away;
        if (distSq < MinDistanceSq)
        {
            away = StableFallbackDirection(self, neighbor.Entity);
            dist = 0.01f;
        }
        else
        {
            dist = math.sqrt(distSq);
            away = diff / dist;
        }

        float penetration = desiredDistance - dist - BodySlop;
        if (penetration <= 0f)
            return;

        correction += away * (penetration * CorrectionPercent);
        correctionCount++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 StableFallbackDirection(Entity a, Entity b)
    {
        uint hash = ((uint)a.Index * 73856093u) ^ ((uint)a.Version * 19349663u) ^ ((uint)b.Index * 83492791u) ^ ((uint)b.Version * 2971215073u);
        float angle = (hash & 1023u) * (6.28318530718f / 1024f);
        return new float2(math.cos(angle), math.sin(angle));
    }
}
