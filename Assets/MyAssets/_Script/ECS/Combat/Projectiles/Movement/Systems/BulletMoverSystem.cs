using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(BallisticFireRequestExecutionSystem))]
partial struct BulletMoverSystem : ISystem
{
    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private ComponentLookup<ProjectilePoolMember> projectilePoolMemberLookup;
    private ComponentLookup<ProjectilePool> projectilePoolLookup;
    private BufferLookup<ProjectilePoolFreeElement> projectilePoolFreeLookup;
    private Entity gridEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridData>();
        gridDataLookup = state.GetComponentLookup<GridData>(isReadOnly: true);
        healthLookup = state.GetComponentLookup<Health>(isReadOnly: false);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(isReadOnly: false);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(isReadOnly: false);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(isReadOnly: true);
        projectilePoolMemberLookup = state.GetComponentLookup<ProjectilePoolMember>(isReadOnly: false);
        projectilePoolLookup = state.GetComponentLookup<ProjectilePool>(isReadOnly: false);
        projectilePoolFreeLookup = state.GetBufferLookup<ProjectilePoolFreeElement>(isReadOnly: false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        gridEntity = SystemAPI.GetSingletonEntity<GridData>();

        gridDataLookup.Update(ref state);
        healthLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        empStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);
        projectilePoolMemberLookup.Update(ref state);
        projectilePoolLookup.Update(ref state);
        projectilePoolFreeLookup.Update(ref state);

        GridData gridData = gridDataLookup.GetRefRO(gridEntity).ValueRO;
        float deltaTime = SystemAPI.Time.DeltaTime;
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((RefRW<LocalTransform> localTransform, RefRW<Bullet> bullet, RefRO<WeaponPayloadRuntime> payload, RefRO<Target> target, Entity entity)
            in SystemAPI.Query<RefRW<LocalTransform>, RefRW<Bullet>, RefRO<WeaponPayloadRuntime>, RefRO<Target>>().WithAll<BulletActive>().WithEntityAccess())
        {
            bullet.ValueRW.timer -= deltaTime;
            if (bullet.ValueRO.timer < 0f)
            {
                ProjectilePoolUtility.ReleaseBulletOrDestroy(ref entityCommandBuffer, entity, localTransform.ValueRO, ref projectilePoolMemberLookup, ref projectilePoolLookup, ref projectilePoolFreeLookup);
                continue;
            }

            float2 prevPos = localTransform.ValueRO.Position.xy;
            float2 vector = bullet.ValueRO.movementVector;
            float3 movement = new float3(vector.x, vector.y, 0f) * bullet.ValueRO.speed * deltaTime;
            localTransform.ValueRW.Position += movement;

            float movedDist = math.length(movement.xy);
            bullet.ValueRW.distance -= movedDist;
            if (bullet.ValueRO.distance < 0f)
            {
                ProjectilePoolUtility.ReleaseBulletOrDestroy(ref entityCommandBuffer, entity, localTransform.ValueRO, ref projectilePoolMemberLookup, ref projectilePoolLookup, ref projectilePoolFreeLookup);
                continue;
            }

            float2 currentPos = localTransform.ValueRO.Position.xy;
            int2 cellStart = GridUtility.WorldToSmallCell(prevPos);
            int2 cellEnd = GridUtility.WorldToSmallCell(currentPos);
            int2 cellMin = math.min(cellStart, cellEnd);
            int2 cellMax = math.max(cellStart, cellEnd);
            NativeParallelMultiHashMap<int2, Grid> map = CombatUtility.GetEntityMap(in gridData, target.ValueRO.targetFaction);

            Entity hitEntity = Entity.Null;
            float closestT = 2f;

            for (int cx = cellMin.x; cx <= cellMax.x; cx++)
            {
                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    int2 cell = new int2(cx, cy);
                    if (!map.TryGetFirstValue(cell, out Grid grid, out var iterator))
                    {
                        continue;
                    }

                    do
                    {
                        float2 boxMin = grid.Position - grid.CollisionRadius;
                        float2 boxMax = grid.Position + grid.CollisionRadius;

                        if (CombatUtility.RaycastSegmentAABB(prevPos, currentPos, boxMin, boxMax, out float hitT) && hitT < closestT)
                        {
                            closestT = hitT;
                            hitEntity = grid.Entity;
                        }
                    }
                    while (map.TryGetNextValue(out grid, ref iterator));
                }
            }

            if (hitEntity != Entity.Null)
            {
                float2 impactPosition = math.lerp(prevPos, currentPos, closestT);
                CombatVfxRequestUtility.EnqueueImpactOrExplosion(
                    ref entityCommandBuffer,
                    payload.ValueRO.payloadKind,
                    payload.ValueRO.splashRadius,
                    payload.ValueRO.statusEffectKind,
                    impactPosition,
                    payload.ValueRO.ownerFaction);

                CombatUtility.ApplyPayload(
                    payload.ValueRO.payloadKind,
                    payload.ValueRO.damageAmount * EmbeddedActionStatusUtility.GetIncomingDamageMultiplier(hitEntity, ref debuffStatusLookup),
                    payload.ValueRO.splashRadius,
                    payload.ValueRO.statusEffectKind,
                    payload.ValueRO.statusDuration,
                    payload.ValueRO.moveSpeedMultiplier,
                    payload.ValueRO.accelerationMultiplier,
                    payload.ValueRO.disableWeapons,
                    hitEntity,
                    impactPosition,
                    in map,
                    ref healthLookup,
                    ref shipAgroLookup,
                    ref empStatusLookup,
                    ref entityCommandBuffer,
                    payload.ValueRO.ownerFaction,
                    ref state);

                ProjectilePoolUtility.ReleaseBulletOrDestroy(ref entityCommandBuffer, entity, localTransform.ValueRO, ref projectilePoolMemberLookup, ref projectilePoolLookup, ref projectilePoolFreeLookup);
            }
        }

        entityCommandBuffer.Playback(state.EntityManager);
    }
}
