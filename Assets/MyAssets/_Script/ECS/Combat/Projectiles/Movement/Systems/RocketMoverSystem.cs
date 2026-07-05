using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct RocketMoverSystem : ISystem
{
    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private ComponentLookup<Velocity> targetVelocityLookup;

    private ComponentLookup<WeaponPayloadRuntime> payloadLookup;
    private ComponentLookup<Target> targetLookup;
    private ComponentLookup<LastKnownTarget> lastKnownTargetLookup;
    private ComponentLookup<RocketActive> rocketActiveLookup;
    private ComponentLookup<RocketLaunchScatter> launchScatterLookup;

    private ComponentLookup<ProjectilePoolMember> projectilePoolMemberLookup;
    private ComponentLookup<ProjectilePool> projectilePoolLookup;
    private BufferLookup<ProjectilePoolFreeElement> projectilePoolFreeLookup;
    private ComponentLookup<RocketTrailResetRequest> rocketTrailResetLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridData>();
        state.RequireForUpdate<EntitiesReferences>();

        gridDataLookup = state.GetComponentLookup<GridData>(isReadOnly: true);
        healthLookup = state.GetComponentLookup<Health>(isReadOnly: false);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(isReadOnly: false);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(isReadOnly: false);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(isReadOnly: true);
        targetVelocityLookup = state.GetComponentLookup<Velocity>(isReadOnly: true);

        payloadLookup = state.GetComponentLookup<WeaponPayloadRuntime>(isReadOnly: true);
        targetLookup = state.GetComponentLookup<Target>(isReadOnly: true);
        lastKnownTargetLookup = state.GetComponentLookup<LastKnownTarget>(isReadOnly: false);
        rocketActiveLookup = state.GetComponentLookup<RocketActive>(isReadOnly: true);
        launchScatterLookup = state.GetComponentLookup<RocketLaunchScatter>(isReadOnly: false);

        projectilePoolMemberLookup = state.GetComponentLookup<ProjectilePoolMember>(isReadOnly: false);
        projectilePoolLookup = state.GetComponentLookup<ProjectilePool>(isReadOnly: false);
        projectilePoolFreeLookup = state.GetBufferLookup<ProjectilePoolFreeElement>(isReadOnly: false);
        rocketTrailResetLookup = state.GetComponentLookup<RocketTrailResetRequest>(isReadOnly: false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        gridDataLookup.Update(ref state);
        healthLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        empStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);
        targetVelocityLookup.Update(ref state);

        payloadLookup.Update(ref state);
        targetLookup.Update(ref state);
        lastKnownTargetLookup.Update(ref state);
        rocketActiveLookup.Update(ref state);
        launchScatterLookup.Update(ref state);

        projectilePoolMemberLookup.Update(ref state);
        projectilePoolLookup.Update(ref state);
        projectilePoolFreeLookup.Update(ref state);
        rocketTrailResetLookup.Update(ref state);

        Entity gridEntity = SystemAPI.GetSingletonEntity<GridData>();
        GridData gridData = gridDataLookup.GetRefRO(gridEntity).ValueRO;
        EntitiesReferences entitiesReferences = SystemAPI.GetSingleton<EntitiesReferences>();

        float dt = SystemAPI.Time.DeltaTime;
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((RefRW<LocalTransform> localTransform,
                  RefRW<Rocket> rocket,
                  RefRW<Velocity> velocity,
                  Entity entity)
            in SystemAPI.Query<RefRW<LocalTransform>, RefRW<Rocket>, RefRW<Velocity>>()
                .WithAll<RocketActive>()
                .WithAll<WeaponPayloadRuntime>()
                .WithAll<Target>()
                .WithAll<LastKnownTarget>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            if (!rocketActiveLookup.IsComponentEnabled(entity))
            {
                continue;
            }

            WeaponPayloadRuntime payload = payloadLookup[entity];
            Target target = targetLookup[entity];

            LastKnownTarget lastKnownTarget = lastKnownTargetLookup[entity];
            bool lastKnownTargetEnabled = lastKnownTargetLookup.IsComponentEnabled(entity);

            rocket.ValueRW.timer -= dt;
            if (rocket.ValueRO.timer < 0f)
            {
                ProjectilePoolUtility.ReleaseRocketOrDestroy(
                    ref ecb,
                    entity,
                    ref projectilePoolMemberLookup,
                    ref projectilePoolLookup,
                    ref projectilePoolFreeLookup,
                    ref targetVelocityLookup,
                    ref lastKnownTargetLookup,
                    ref rocketTrailResetLookup);
                continue;
            }

            float2 rocketPos = localTransform.ValueRO.Position.xy;

            float2 currentDir = math.normalizesafe(
                velocity.ValueRO.velocity,
                new float2(0f, 1f));

            float2 newDir = currentDir;
            bool isLaunchScatterActive = launchScatterLookup.HasComponent(entity);

            // missile volley start phase, overrides homing for short time
            if (isLaunchScatterActive)
            {
                RocketLaunchScatter scatter = launchScatterLookup[entity];
                scatter.timer -= dt;
                scatter.distanceRemaining -= math.max(rocket.ValueRO.currentSpeed, 0f) * dt;

                newDir = math.normalizesafe(scatter.scatterDirection, currentDir);

                if (scatter.timer <= 0f || scatter.distanceRemaining <= 0f)
                {
                    ecb.RemoveComponent<RocketLaunchScatter>(entity);
                }
                else
                {
                    launchScatterLookup[entity] = scatter;
                }
            }
            // guided mode. rotationSpeed <= 0 means torpedo, keep launch direction
            else if (rocket.ValueRO.rotationSpeed > 0.001f &&
                (rocket.ValueRO.phase == (byte)RocketFlightPhase.Locked ||
                 rocket.ValueRO.phase == (byte)RocketFlightPhase.Lkp))
            {
                float2 targetPos = rocketPos + velocity.ValueRO.velocity;

                if (rocket.ValueRO.phase == (byte)RocketFlightPhase.Lkp && lastKnownTargetEnabled)
                {
                    targetPos = lastKnownTarget.lastKnownPosition;
                }
                else if (target.targetEntity != Entity.Null)
                {
                    targetPos = target.targetPosition;
                }

                float2 toTarget = targetPos - rocketPos;
                float2 desiredDir = math.normalizesafe(toTarget, currentDir);

                newDir = math.normalizesafe(currentDir + (desiredDir - currentDir) * rocket.ValueRO.rotationSpeed * dt, currentDir);
            }

            rocket.ValueRW.currentSpeed += rocket.ValueRO.acceleration * dt;
            rocket.ValueRW.currentSpeed = math.min(rocket.ValueRO.currentSpeed, rocket.ValueRO.maxSpeed);

            velocity.ValueRW.velocity = newDir * rocket.ValueRO.currentSpeed;

            // visual rotation follows movement
            localTransform.ValueRW.Rotation = RotationFromDirection(newDir);

            float2 prevPos = rocketPos;
            float2 currentPos = rocketPos + velocity.ValueRO.velocity * dt;

            float3 newPos = localTransform.ValueRO.Position;
            newPos.x = currentPos.x;
            newPos.y = currentPos.y;
            newPos.z = GameConstants.ProjectileZ;
            localTransform.ValueRW.Position = newPos;

            int2 cellStart = GridUtility.WorldToSmallCell(prevPos);
            int2 cellEnd = GridUtility.WorldToSmallCell(currentPos);
            int2 cellMin = math.min(cellStart, cellEnd);
            int2 cellMax = math.max(cellStart, cellEnd);

            NativeParallelMultiHashMap<int2, Grid> map = CombatUtility.GetEntityMap(in gridData, target.targetFaction);

            Entity hitEntity = Entity.Null;
            float closestT = 2f;

            for (int cx = cellMin.x; cx <= cellMax.x; cx++)
            {
                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    if (!map.TryGetFirstValue(new int2(cx, cy), out Grid gridCell, out var iterator))
                    {
                        continue;
                    }

                    do
                    {
                        float2 boxMin = gridCell.Position - gridCell.CollisionRadius;
                        float2 boxMax = gridCell.Position + gridCell.CollisionRadius;

                        if (CombatUtility.RaycastSegmentAABB(prevPos, currentPos, boxMin, boxMax, out float hitT) && hitT < closestT)
                        {
                            closestT = hitT;
                            hitEntity = gridCell.Entity;
                        }
                    }
                    while (map.TryGetNextValue(out gridCell, ref iterator));
                }
            }

            if (hitEntity != Entity.Null)
            {
                velocity.ValueRW.velocity = float2.zero;

                float2 impactPosition = math.lerp(prevPos, currentPos, closestT);
                CombatVfxRequestUtility.EnqueueImpactOrExplosion(
                    ref ecb,
                    payload.payloadKind,
                    payload.splashRadius,
                    payload.statusEffectKind,
                    impactPosition,
                    payload.ownerFaction);

                Entity explosionEntity = ecb.Instantiate(entitiesReferences.explosionPrefabEntity);
                float3 explosionPos = new float3(impactPosition.x, impactPosition.y, GameConstants.EffectsZ);

                SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
                    ref ecb,
                    explosionEntity,
                    LocalTransform.FromPosition(explosionPos),
                    true,
                    true);

                ecb.SetComponent(explosionEntity, new BlowRadius
                {
                    firstTime = true,
                    blowRadius = math.max(payload.splashRadius, 0.1f),
                });

                CombatUtility.ApplyPayload(
                    payload.payloadKind,
                    payload.damageAmount * EmbeddedActionStatusUtility.GetIncomingDamageMultiplier(hitEntity, ref debuffStatusLookup),
                    payload.splashRadius,
                    payload.statusEffectKind,
                    payload.statusDuration,
                    payload.moveSpeedMultiplier,
                    payload.accelerationMultiplier,
                    payload.disableWeapons,
                    hitEntity,
                    impactPosition,
                    in map,
                    ref healthLookup,
                    ref shipAgroLookup,
                    ref empStatusLookup,
                    ref ecb,
                    payload.ownerFaction,
                    ref state);

                ProjectilePoolUtility.ReleaseRocketOrDestroy(
                    ref ecb,
                    entity,
                    ref projectilePoolMemberLookup,
                    ref projectilePoolLookup,
                    ref projectilePoolFreeLookup,
                    ref targetVelocityLookup,
                    ref lastKnownTargetLookup,
                    ref rocketTrailResetLookup);
            }
        }

        ecb.Playback(state.EntityManager);
    }

    private static quaternion RotationFromDirection(float2 direction)
    {
        direction = math.normalizesafe(direction, new float2(0f, 1f));

        // 2D top-down, rocket nose is local +Y
        float angle = math.atan2(direction.y, direction.x) - math.radians(90f);

        return quaternion.RotateZ(angle);
    }
}
