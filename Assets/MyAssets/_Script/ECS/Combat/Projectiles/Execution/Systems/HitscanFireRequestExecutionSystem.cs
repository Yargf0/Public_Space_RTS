using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedWeaponFireRequestBuildSystem))]
public partial struct HitscanFireRequestExecutionSystem : ISystem
{
    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private EntityQuery hitscanQueueQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridData>();
        state.RequireForUpdate<WeaponProfileDatabase>();

        gridDataLookup = state.GetComponentLookup<GridData>(true);
        healthLookup = state.GetComponentLookup<Health>(false);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(false);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(false);
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(true);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(true);


        hitscanQueueQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<WeaponFireRequestQueueSingleton>()
            .WithAllRW<HitscanWeaponFireRequestElement>()
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        bool hasQueuedRequests = hitscanQueueQuery.TryGetSingletonBuffer<HitscanWeaponFireRequestElement>(out DynamicBuffer<HitscanWeaponFireRequestElement> queuedRequests) && queuedRequests.Length > 0;
        if (!hasQueuedRequests)
        {
            return;
        }

        if (!SystemAPI.TryGetSingleton<WeaponProfileDatabase>(out WeaponProfileDatabase database))
        {
            return;
        }

        gridDataLookup.Update(ref state);
        healthLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        empStatusLookup.Update(ref state);
        buffStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);

        ref WeaponProfileDatabaseBlob root = ref database.Value.Value;
        Entity gridEntity = SystemAPI.GetSingletonEntity<GridData>();
        GridData gridData = gridDataLookup.GetRefRO(gridEntity).ValueRO;

        NativeList<WeaponFireRequest> requests = new NativeList<WeaponFireRequest>(queuedRequests.Length, Allocator.Temp);
        for (int i = 0; i < queuedRequests.Length; i++)
        {
            requests.Add(queuedRequests[i].Value);
        }
        queuedRequests.Clear();

        if (requests.Length == 0)
        {
            requests.Dispose();
            return;
        }

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        for (int i = 0; i < requests.Length; i++)
        {
            WeaponFireRequest request = requests[i];
            if ((WeaponRequestKind)request.requestKind != WeaponRequestKind.Hitscan)
            {
                continue;
            }

            int profileIndex = request.profileIndex;
            if ((uint)profileIndex >= (uint)root.Profiles.Length)
            {
                continue;
            }

            WeaponProfileBlob profile = root.Profiles[profileIndex];
            float2 shootDir = math.normalizesafe(request.direction, new float2(0f, 1f));
            float maxRange = profile.attackDistance;
            float2 startPos = request.spawnPosition.xy;
            float2 endPos = startPos + shootDir * maxRange;
            NativeParallelMultiHashMap<int2, Grid> map = CombatUtility.GetEntityMap(in gridData, request.targetFaction);
            int2 cellStart = GridUtility.WorldToSmallCell(startPos);
            int2 cellEnd = GridUtility.WorldToSmallCell(endPos);
            int2 cellMin = math.min(cellStart, cellEnd);
            int2 cellMax = math.max(cellStart, cellEnd);
            float closestHitDist = maxRange;
            Entity hitEntity = Entity.Null;

            for (int cx = cellMin.x; cx <= cellMax.x; cx++)
            {
                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    if (!map.TryGetFirstValue(new int2(cx, cy), out Grid grid, out var iterator))
                    {
                        continue;
                    }

                    do
                    {
                        float2 boxMin = grid.Position - grid.CollisionRadius;
                        float2 boxMax = grid.Position + grid.CollisionRadius;

                        if (CombatUtility.RaycastAABB(startPos, shootDir, closestHitDist, boxMin, boxMax, out float hitDist) && hitDist < closestHitDist)
                        {
                            closestHitDist = hitDist;
                            hitEntity = grid.Entity;
                        }
                    }
                    while (map.TryGetNextValue(out grid, ref iterator));
                }
            }

            float2 impactPosition = hitEntity != Entity.Null ? startPos + shootDir * closestHitDist : endPos;
            CombatVfxRequestUtility.EnqueueBeam(ref ecb, in request, in profile, impactPosition);

            CombatUtility.ApplyPayload(
                profile.payloadKind,
                profile.damageAmount *
                    EmbeddedActionStatusUtility.GetOutgoingEffectMultiplier(request.ownerEntity, ref buffStatusLookup, ref debuffStatusLookup) *
                    EmbeddedActionStatusUtility.GetIncomingDamageMultiplier(hitEntity, ref debuffStatusLookup),
                profile.splashRadius,
                profile.statusEffectKind,
                profile.statusDuration,
                profile.moveSpeedMultiplier,
                profile.accelerationMultiplier,
                profile.disableWeapons,
                hitEntity,
                impactPosition,
                in map,
                ref healthLookup,
                ref shipAgroLookup,
                ref empStatusLookup,
                ref ecb,
                request.ownerFaction,
                ref state);

            CombatVfxRequestUtility.EnqueueImpactOrExplosion(
                ref ecb,
                profile.payloadKind,
                profile.splashRadius,
                profile.statusEffectKind,
                impactPosition,
                request.ownerFaction);

            if (request.ammoEntity != Entity.Null)
            {
                float beamLength = math.distance(startPos, impactPosition);
                float beamAngle = math.atan2(shootDir.y, shootDir.x) - math.PI / 2f;
                Entity beamEntity = state.EntityManager.Instantiate(request.ammoEntity);

                SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
                    state.EntityManager,
                    beamEntity,
                    LocalTransform.FromPositionRotation(
                        new float3(startPos.x, startPos.y, GameConstants.ProjectileZ),
                        quaternion.RotateZ(beamAngle)));

                if (SystemAPI.HasComponent<PostTransformMatrix>(beamEntity))
                {
                    SystemAPI.SetComponent(beamEntity, new PostTransformMatrix
                    {
                        Value = float4x4.TRS(
                            new float3(0f, beamLength * 0.5f, 0f),
                            quaternion.identity,
                            new float3(profile.hitscanBeamWidth, beamLength, 1f))
                    });
                }

                if (SystemAPI.HasComponent<SelfDeleter>(beamEntity))
                {
                    SystemAPI.SetComponent(beamEntity, new SelfDeleter
                    {
                        LifeTime = profile.lifetime
                    });
                }
                else
                {
                    state.EntityManager.AddComponentData(beamEntity, new SelfDeleter
                    {
                        LifeTime = profile.lifetime
                    });
                }
            }

        }

        ecb.Playback(state.EntityManager);
        requests.Dispose();
    }

}
