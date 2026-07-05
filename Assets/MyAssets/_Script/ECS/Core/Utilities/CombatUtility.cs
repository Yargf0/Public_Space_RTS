using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class CombatUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetGridInRadius(float2 pos, float2 gridPos, float2 radius)
    {
        return gridPos.x - radius.x < pos.x
            && gridPos.x + radius.x > pos.x
            && gridPos.y - radius.y < pos.y
            && gridPos.y + radius.y > pos.y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyDamage(
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        Entity targetEntity,
        float damage)
    {
        if (healthLookup.HasComponent(targetEntity))
        {
            RefRW<Health> health = healthLookup.GetRefRW(targetEntity);
            health.ValueRW.healthAmount -= damage;
            health.ValueRW.onHealthChanged = true;
        }

        MarkWasHit(ref shipAgroLookup, targetEntity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MarkWasHit(
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        Entity targetEntity)
    {
        if (shipAgroLookup.HasComponent(targetEntity))
        {
            RefRW<ShipAgro> agro = shipAgroLookup.GetRefRW(targetEntity);
            agro.ValueRW.wasHit = true;
            agro.ValueRW.wasHitTimer = GameConstants.ReturnFireMemoryDuration;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static NativeParallelMultiHashMap<int2, Grid> GetEntityMap(
        in GridData gridData,
        Faction targetFaction)
    {
        return targetFaction == Faction.Enemy
            ? gridData.EnemyEntityMap
            : gridData.FriendlyEntityMap;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CircleIntersectsAABB(float2 circleCenter, float circleRadius, float2 boxCenter, float2 boxHalfSize)
    {
        float2 boxMin = boxCenter - boxHalfSize;
        float2 boxMax = boxCenter + boxHalfSize;
        float2 closestPoint = math.clamp(circleCenter, boxMin, boxMax);
        float2 delta = closestPoint - circleCenter;
        return math.lengthsq(delta) <= circleRadius * circleRadius;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CheckForHitInCell(
        in NativeParallelMultiHashMap<int2, Grid> map,
        int2 cell,
        float2 pos,
        float damage,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        EntityCommandBuffer ecb,
        Entity projectileEntity)
    {
        if (map.TryGetFirstValue(cell, out Grid grid, out var iterator))
        {
            do
            {
                if (GetGridInRadius(pos, grid.Position, grid.CollisionRadius))
                {
                    ApplyDamage(ref healthLookup, ref shipAgroLookup, grid.Entity, damage);
                    ecb.DestroyEntity(projectileEntity);
                    return true;
                }
            }
            while (map.TryGetNextValue(out grid, ref iterator));
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CheckForHitInLine(float2 lineStart, float2 lineEnd, float2 boxMin, float2 boxMax)
    {
        return RaycastSegmentAABB(lineStart, lineEnd, boxMin, boxMax, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool RaycastSegmentAABB(float2 lineStart, float2 lineEnd, float2 boxMin, float2 boxMax, out float hitT)
    {
        float2 dir = lineEnd - lineStart;
        hitT = 1f;

        if (math.lengthsq(dir) < 0.0001f)
        {
            bool inside = lineStart.x >= boxMin.x && lineStart.x <= boxMax.x
                && lineStart.y >= boxMin.y && lineStart.y <= boxMax.y;

            if (inside)
            {
                hitT = 0f;
            }

            return inside;
        }

        float2 invDir = 1f / dir;
        float2 t1 = (boxMin - lineStart) * invDir;
        float2 t2 = (boxMax - lineStart) * invDir;
        float2 tmin = math.min(t1, t2);
        float2 tmax = math.max(t1, t2);
        float tNear = math.max(tmin.x, tmin.y);
        float tFar = math.min(tmax.x, tmax.y);

        if (tNear > tFar || tNear > 1f || tFar < 0f)
        {
            return false;
        }

        hitT = math.clamp(math.max(tNear, 0f), 0f, 1f);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool RaycastAABB(float2 rayStart, float2 rayDir, float maxDist, float2 boxMin, float2 boxMax, out float hitDist)
    {
        hitDist = maxDist;

        if (math.lengthsq(rayDir) < 0.0001f)
        {
            bool inside = rayStart.x >= boxMin.x && rayStart.x <= boxMax.x
                && rayStart.y >= boxMin.y && rayStart.y <= boxMax.y;

            if (inside)
            {
                hitDist = 0f;
            }

            return inside;
        }

        float2 invDir = 1f / rayDir;
        float2 t1 = (boxMin - rayStart) * invDir;
        float2 t2 = (boxMax - rayStart) * invDir;
        float2 tmin = math.min(t1, t2);
        float2 tmax = math.max(t1, t2);
        float tNear = math.max(tmin.x, tmin.y);
        float tFar = math.min(tmax.x, tmax.y);

        if (tNear > tFar || tFar < 0f || tNear > maxDist)
        {
            return false;
        }

        hitDist = math.max(tNear, 0f);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyPayload(
        byte payloadKind,
        float damageAmount,
        float splashRadius,
        byte statusEffectKind,
        float statusDuration,
        float moveSpeedMultiplier,
        float accelerationMultiplier,
        bool disableWeapons,
        Entity directEntity,
        float2 impactPosition,
        in NativeParallelMultiHashMap<int2, Grid> map,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref SystemState state)
    {
        switch ((WeaponPayloadKind)payloadKind)
        {
            case WeaponPayloadKind.Direct:
                {
                    if (directEntity != Entity.Null)
                    {
                        ApplyDamage(ref healthLookup, ref shipAgroLookup, directEntity, damageAmount);
                        ApplyStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            directEntity,
                            ref empStatusLookup,
                            ref state);
                    }
                    break;
                }

            case WeaponPayloadKind.Splash:
                {
                    if (splashRadius > 0.001f)
                    {
                        ApplyAreaDamage(
                            in map,
                            impactPosition,
                            splashRadius,
                            damageAmount,
                            ref healthLookup,
                            ref shipAgroLookup);

                        ApplyAreaStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            in map,
                            impactPosition,
                            splashRadius,
                            ref empStatusLookup,
                            ref state);
                    }
                    else if (directEntity != Entity.Null)
                    {
                        ApplyDamage(ref healthLookup, ref shipAgroLookup, directEntity, damageAmount);
                        ApplyStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            directEntity,
                            ref empStatusLookup,
                            ref state);
                    }
                    break;
                }

            case WeaponPayloadKind.DirectPlusSplash:
                {
                    if (directEntity != Entity.Null)
                    {
                        ApplyDamage(ref healthLookup, ref shipAgroLookup, directEntity, damageAmount);
                        ApplyStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            directEntity,
                            ref empStatusLookup,
                            ref state);
                    }

                    if (splashRadius > 0.001f)
                    {
                        ApplyAreaDamage(
                            in map,
                            impactPosition,
                            splashRadius,
                            damageAmount,
                            ref healthLookup,
                            ref shipAgroLookup,
                            directEntity);

                        ApplyAreaStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            in map,
                            impactPosition,
                            splashRadius,
                            ref empStatusLookup,
                            ref state,
                            directEntity);
                    }
                    break;
                }
        }
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyPayload(
        byte payloadKind,
        float damageAmount,
        float splashRadius,
        byte statusEffectKind,
        float statusDuration,
        float moveSpeedMultiplier,
        float accelerationMultiplier,
        bool disableWeapons,
        Entity directEntity,
        float2 impactPosition,
        in NativeParallelMultiHashMap<int2, Grid> map,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref EntityCommandBuffer ecb,
        Faction sourceFaction,
        ref SystemState state)
    {
        switch ((WeaponPayloadKind)payloadKind)
        {
            case WeaponPayloadKind.Direct:
                {
                    if (directEntity != Entity.Null)
                    {
                        ApplyDamageWithVisibilityRequest(
                            ref healthLookup,
                            ref shipAgroLookup,
                            ref ecb,
                            directEntity,
                            damageAmount,
                            sourceFaction);

                        ApplyStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            directEntity,
                            ref empStatusLookup,
                            ref state);
                    }
                    break;
                }

            case WeaponPayloadKind.Splash:
                {
                    if (splashRadius > 0.001f)
                    {
                        ApplyAreaDamageWithVisibilityRequest(
                            in map,
                            impactPosition,
                            splashRadius,
                            damageAmount,
                            ref healthLookup,
                            ref shipAgroLookup,
                            ref ecb,
                            sourceFaction);

                        ApplyAreaStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            in map,
                            impactPosition,
                            splashRadius,
                            ref empStatusLookup,
                            ref state);
                    }
                    else if (directEntity != Entity.Null)
                    {
                        ApplyDamageWithVisibilityRequest(
                            ref healthLookup,
                            ref shipAgroLookup,
                            ref ecb,
                            directEntity,
                            damageAmount,
                            sourceFaction);

                        ApplyStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            directEntity,
                            ref empStatusLookup,
                            ref state);
                    }
                    break;
                }

            case WeaponPayloadKind.DirectPlusSplash:
                {
                    if (directEntity != Entity.Null)
                    {
                        ApplyDamageWithVisibilityRequest(
                            ref healthLookup,
                            ref shipAgroLookup,
                            ref ecb,
                            directEntity,
                            damageAmount,
                            sourceFaction);

                        ApplyStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            directEntity,
                            ref empStatusLookup,
                            ref state);
                    }

                    if (splashRadius > 0.001f)
                    {
                        ApplyAreaDamageWithVisibilityRequest(
                            in map,
                            impactPosition,
                            splashRadius,
                            damageAmount,
                            ref healthLookup,
                            ref shipAgroLookup,
                            ref ecb,
                            sourceFaction,
                            directEntity);

                        ApplyAreaStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            in map,
                            impactPosition,
                            splashRadius,
                            ref empStatusLookup,
                            ref state,
                            directEntity);
                    }
                    break;
                }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyDamageWithVisibilityRequest(
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        ref EntityCommandBuffer ecb,
        Entity targetEntity,
        float damage,
        Faction sourceFaction)
    {
        ApplyDamage(ref healthLookup, ref shipAgroLookup, targetEntity, damage);
        CreateVisibilityHitRequest(ref ecb, targetEntity, sourceFaction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CreateVisibilityHitRequest(
        ref EntityCommandBuffer ecb,
        Entity targetEntity,
        Faction sourceFaction)
    {
        if (targetEntity == Entity.Null)
        {
            return;
        }

        Entity requestEntity = ecb.CreateEntity();
        ecb.AddComponent(requestEntity, new VisibilityHitRequest
        {
            targetEntity = targetEntity,
            observerFaction = sourceFaction,
            duration = GameConstants.HitVisibilityRefreshDuration,
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyAreaDamageWithVisibilityRequest(
        in NativeParallelMultiHashMap<int2, Grid> map,
        float2 center,
        float blowRadius,
        float damage,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        ref EntityCommandBuffer ecb,
        Faction sourceFaction)
    {
        ApplyAreaDamageWithVisibilityRequest(
            in map,
            center,
            blowRadius,
            damage,
            ref healthLookup,
            ref shipAgroLookup,
            ref ecb,
            sourceFaction,
            Entity.Null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyAreaDamageWithVisibilityRequest(
        in NativeParallelMultiHashMap<int2, Grid> map,
        float2 center,
        float blowRadius,
        float damage,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        ref EntityCommandBuffer ecb,
        Faction sourceFaction,
        Entity excludedEntity)
    {
        float2 radiusVec = new float2(blowRadius, blowRadius);
        int2 minCell = GridUtility.WorldToSmallCell(center - radiusVec);
        int2 maxCell = GridUtility.WorldToSmallCell(center + radiusVec);

        int cellCountX = maxCell.x - minCell.x + 1;
        int cellCountY = maxCell.y - minCell.y + 1;
        int estimatedCapacity = math.max(16, cellCountX * cellCountY * 8);

        NativeParallelHashSet<Entity> processedEntities = new NativeParallelHashSet<Entity>(estimatedCapacity, Allocator.Temp);

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                if (!map.TryGetFirstValue(new int2(x, y), out Grid grid, out var it))
                {
                    continue;
                }

                do
                {
                    if (grid.Entity == excludedEntity || !processedEntities.Add(grid.Entity))
                    {
                        continue;
                    }

                    if (CircleIntersectsAABB(center, blowRadius, grid.Position, grid.CollisionRadius))
                    {
                        ApplyDamageWithVisibilityRequest(
                            ref healthLookup,
                            ref shipAgroLookup,
                            ref ecb,
                            grid.Entity,
                            damage,
                            sourceFaction);
                    }
                }
                while (map.TryGetNextValue(out grid, ref it));
            }
        }

        processedEntities.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyStatusEffect(
        byte statusEffectKind,
        float statusDuration,
        float moveSpeedMultiplier,
        float accelerationMultiplier,
        bool disableWeapons,
        Entity targetEntity,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref SystemState state)
    {
        if ((WeaponStatusEffectKind)statusEffectKind != WeaponStatusEffectKind.Emp)
        {
            return;
        }

        if (targetEntity == Entity.Null || !empStatusLookup.HasComponent(targetEntity))
        {
            return;
        }

        RefRW<EmpStatus> empStatus = empStatusLookup.GetRefRW(targetEntity);
        empStatus.ValueRW.timer = math.max(empStatus.ValueRO.timer, statusDuration);
        empStatus.ValueRW.moveSpeedMultiplier = math.min(empStatus.ValueRO.moveSpeedMultiplier, moveSpeedMultiplier);
        empStatus.ValueRW.accelerationMultiplier = math.min(empStatus.ValueRO.accelerationMultiplier, accelerationMultiplier);
        empStatus.ValueRW.disableWeapons = empStatus.ValueRO.disableWeapons || disableWeapons;
        empStatusLookup.SetComponentEnabled(targetEntity, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyAreaStatusEffect(
        byte statusEffectKind,
        float statusDuration,
        float moveSpeedMultiplier,
        float accelerationMultiplier,
        bool disableWeapons,
        in NativeParallelMultiHashMap<int2, Grid> map,
        float2 center,
        float radius,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref SystemState state)
    {
        ApplyAreaStatusEffect(
            statusEffectKind,
            statusDuration,
            moveSpeedMultiplier,
            accelerationMultiplier,
            disableWeapons,
            in map,
            center,
            radius,
            ref empStatusLookup,
            ref state,
            Entity.Null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyAreaStatusEffect(
        byte statusEffectKind,
        float statusDuration,
        float moveSpeedMultiplier,
        float accelerationMultiplier,
        bool disableWeapons,
        in NativeParallelMultiHashMap<int2, Grid> map,
        float2 center,
        float radius,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref SystemState state,
        Entity excludedEntity)
    {
        if ((WeaponStatusEffectKind)statusEffectKind == WeaponStatusEffectKind.None || radius <= 0.001f)
        {
            return;
        }

        float2 radiusVec = new float2(radius, radius);
        int2 minCell = GridUtility.WorldToSmallCell(center - radiusVec);
        int2 maxCell = GridUtility.WorldToSmallCell(center + radiusVec);
        NativeParallelHashSet<Entity> processedEntities = new NativeParallelHashSet<Entity>(32, Allocator.Temp);

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                if (!map.TryGetFirstValue(new int2(x, y), out Grid grid, out var it))
                {
                    continue;
                }

                do
                {
                    if (grid.Entity == excludedEntity || !processedEntities.Add(grid.Entity))
                    {
                        continue;
                    }

                    if (CircleIntersectsAABB(center, radius, grid.Position, grid.CollisionRadius))
                    {
                        ApplyStatusEffect(
                            statusEffectKind,
                            statusDuration,
                            moveSpeedMultiplier,
                            accelerationMultiplier,
                            disableWeapons,
                            grid.Entity,
                            ref empStatusLookup,
                            ref state);
                    }
                }
                while (map.TryGetNextValue(out grid, ref it));
            }
        }

        processedEntities.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyAreaDamage(
        in NativeParallelMultiHashMap<int2, Grid> map,
        float2 center,
        float blowRadius,
        float damage,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup)
    {
        ApplyAreaDamage(
            in map,
            center,
            blowRadius,
            damage,
            ref healthLookup,
            ref shipAgroLookup,
            Entity.Null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyAreaDamage(
        in NativeParallelMultiHashMap<int2, Grid> map,
        float2 center,
        float blowRadius,
        float damage,
        ref ComponentLookup<Health> healthLookup,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        Entity excludedEntity)
    {
        float2 radiusVec = new float2(blowRadius, blowRadius);
        int2 minCell = GridUtility.WorldToSmallCell(center - radiusVec);
        int2 maxCell = GridUtility.WorldToSmallCell(center + radiusVec);

        int cellCountX = maxCell.x - minCell.x + 1;
        int cellCountY = maxCell.y - minCell.y + 1;
        int estimatedCapacity = math.max(16, cellCountX * cellCountY * 8);

        NativeParallelHashSet<Entity> processedEntities = new NativeParallelHashSet<Entity>(estimatedCapacity, Allocator.Temp);

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                if (!map.TryGetFirstValue(new int2(x, y), out Grid grid, out var it))
                {
                    continue;
                }

                do
                {
                    if (grid.Entity == excludedEntity || !processedEntities.Add(grid.Entity))
                    {
                        continue;
                    }

                    if (CircleIntersectsAABB(center, blowRadius, grid.Position, grid.CollisionRadius))
                    {
                        ApplyDamage(ref healthLookup, ref shipAgroLookup, grid.Entity, damage);
                    }
                }
                while (map.TryGetNextValue(out grid, ref it));
            }
        }

        processedEntities.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 GetSpawnWorldPos(in LocalToWorld localToWorld, float3 localSpawnPoint)
    {
        float3 pos = math.transform(localToWorld.Value, localSpawnPoint);
        pos.z = GameConstants.ProjectileZ;
        return pos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 GetTurretForward(in LocalToWorld localToWorld)
    {
        float3 fwd3 = math.mul(localToWorld.Rotation, new float3(0f, 1f, 0f));
        return math.normalize(fwd3.xy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasLimitedWeaponRotation(in WeaponProfileBlob profile)
    {
        return profile.rotate && profile.limitRotation && profile.rotationLimitAngle < 179.999f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NormalizeAngleRad(float angle)
    {
        return math.atan2(math.sin(angle), math.cos(angle));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ClampAngleAroundRad(float angle, float center, float maxDelta)
    {
        float delta = NormalizeAngleRad(angle - center);
        return center + math.clamp(delta, -maxDelta, maxDelta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDirectionAlignedWithWeaponForward(
        float2 weaponForward,
        float2 desiredDirection,
        float maxAngleDifferenceDegrees = 3f)
    {
        float2 forward = math.normalizesafe(weaponForward, new float2(0f, 1f));
        float2 desired = math.normalizesafe(desiredDirection, forward);
        float alignmentCos = math.cos(math.radians(math.max(0.01f, maxAngleDifferenceDegrees)));
        return math.dot(forward, desired) >= alignmentCos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTargetInFiringCone(
        in LocalToWorld localToWorld,
        float2 targetPosition,
        bool rotate,
        float firingConeCos = 0.85f)
    {
        if (rotate)
        {
            return true;
        }

        float2 turretForward = GetTurretForward(in localToWorld);
        float2 toTarget = math.normalizesafe(targetPosition - localToWorld.Position.xy, turretForward);
        return math.dot(toTarget, turretForward) >= firingConeCos;
    }
}