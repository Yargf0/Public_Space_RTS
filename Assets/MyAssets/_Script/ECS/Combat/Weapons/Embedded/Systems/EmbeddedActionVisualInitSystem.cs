using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// creates one persistent visual per Beam/Aura slot
// idle visuals are scaled to zero, no Instantiate per tick
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EmbeddedActionTargetSystem))]
public partial struct EmbeddedActionVisualInitSystem : ISystem
{
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;
    private ComponentLookup<SelfDeleter> selfDeleterLookup;
    private ComponentLookup<Bullet> bulletLookup;
    private ComponentLookup<BulletActive> bulletActiveLookup;
    private ComponentLookup<Rocket> rocketLookup;
    private ComponentLookup<RocketActive> rocketActiveLookup;
    private ComponentLookup<WeaponPayloadRuntime> payloadLookup;
    private ComponentLookup<Target> targetLookup;
    private ComponentLookup<LastKnownTarget> lastKnownTargetLookup;
    private ComponentLookup<RocketLaunchScatter> rocketLaunchScatterLookup;
    private ComponentLookup<RocketTrailResetRequest> rocketTrailResetLookup;
    private ComponentLookup<ProjectilePoolMember> projectilePoolMemberLookup;
    private BufferLookup<EmbeddedActionVisualRuntime> visualRuntimeLookup;

    public void OnCreate(ref SystemState state)
    {
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        postTransformMatrixLookup = state.GetComponentLookup<PostTransformMatrix>(true);
        selfDeleterLookup = state.GetComponentLookup<SelfDeleter>(true);
        bulletLookup = state.GetComponentLookup<Bullet>(true);
        bulletActiveLookup = state.GetComponentLookup<BulletActive>(true);
        rocketLookup = state.GetComponentLookup<Rocket>(true);
        rocketActiveLookup = state.GetComponentLookup<RocketActive>(true);
        payloadLookup = state.GetComponentLookup<WeaponPayloadRuntime>(true);
        targetLookup = state.GetComponentLookup<Target>(true);
        lastKnownTargetLookup = state.GetComponentLookup<LastKnownTarget>(true);
        rocketLaunchScatterLookup = state.GetComponentLookup<RocketLaunchScatter>(true);
        rocketTrailResetLookup = state.GetComponentLookup<RocketTrailResetRequest>(true);
        projectilePoolMemberLookup = state.GetComponentLookup<ProjectilePoolMember>(true);
        visualRuntimeLookup = state.GetBufferLookup<EmbeddedActionVisualRuntime>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        localTransformLookup.Update(ref state);
        postTransformMatrixLookup.Update(ref state);
        selfDeleterLookup.Update(ref state);
        bulletLookup.Update(ref state);
        bulletActiveLookup.Update(ref state);
        rocketLookup.Update(ref state);
        rocketActiveLookup.Update(ref state);
        payloadLookup.Update(ref state);
        targetLookup.Update(ref state);
        lastKnownTargetLookup.Update(ref state);
        rocketLaunchScatterLookup.Update(ref state);
        rocketTrailResetLookup.Update(ref state);
        projectilePoolMemberLookup.Update(ref state);
        visualRuntimeLookup.Update(ref state);

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        bool changed = false;

        foreach ((DynamicBuffer<EmbeddedActionSlot> actions, Entity shipEntity) in
                 SystemAPI.Query<DynamicBuffer<EmbeddedActionSlot>>()
                     .WithAll<EmbeddedWeaponHost>()
                     .WithNone<EmbeddedActionVisualInitialized>()
                     .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedActionVisualRuntime> runtime = visualRuntimeLookup.HasBuffer(shipEntity)
                ? ecb.SetBuffer<EmbeddedActionVisualRuntime>(shipEntity)
                : ecb.AddBuffer<EmbeddedActionVisualRuntime>(shipEntity);

            for (int i = 0; i < actions.Length; i++)
            {
                EmbeddedActionSlot action = actions[i];
                Entity visualEntity = Entity.Null;

                EmbeddedActionDeliveryKind deliveryKind = (EmbeddedActionDeliveryKind)action.deliveryKind;
                if ((deliveryKind == EmbeddedActionDeliveryKind.BeamOverTime ||
                     deliveryKind == EmbeddedActionDeliveryKind.Aura) &&
                    action.visualPrefabEntity != Entity.Null)
                {
                    visualEntity = ecb.Instantiate(action.visualPrefabEntity);

                    if (localTransformLookup.HasComponent(action.visualPrefabEntity))
                    {
                        ecb.SetComponent(
                            visualEntity,
                            LocalTransform.FromPositionRotationScale(
                                new float3(0f, 0f, GameConstants.ProjectileZ),
                                quaternion.identity,
                                0f));
                    }

                    if (postTransformMatrixLookup.HasComponent(action.visualPrefabEntity))
                    {
                        ecb.SetComponent(visualEntity, new PostTransformMatrix
                        {
                            Value = float4x4.TRS(float3.zero, quaternion.identity, new float3(0f, 0f, 1f))
                        });
                    }

                    StripProjectileRuntimeComponents(ref ecb, visualEntity, action.visualPrefabEntity);
                    ecb.AddComponent(visualEntity, new EmbeddedActionVisualOwner
                    {
                        owner = shipEntity,
                        slotIndex = i
                    });
                }

                runtime.Add(new EmbeddedActionVisualRuntime
                {
                    visualEntity = visualEntity,
                    visibleUntil = 0f,
                    startWorld = float2.zero,
                    endWorld = float2.zero,
                    range = 0f,
                    width = math.max(0.01f, action.visualWidth),
                    kind = (byte)EmbeddedActionVisualKind.None,
                    flags = 0,
                });
            }

            ecb.AddComponent<EmbeddedActionVisualInitialized>(shipEntity);
            changed = true;
        }

        if (changed)
        {
            ecb.Playback(state.EntityManager);
        }
    }
    private void StripProjectileRuntimeComponents(ref EntityCommandBuffer ecb, Entity visualEntity, Entity prefabEntity)
    {
        if (selfDeleterLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<SelfDeleter>(visualEntity);
        }

        if (bulletLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<Bullet>(visualEntity);
        }

        if (bulletActiveLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<BulletActive>(visualEntity);
        }

        if (rocketLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<Rocket>(visualEntity);
        }

        if (rocketActiveLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<RocketActive>(visualEntity);
        }

        if (payloadLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<WeaponPayloadRuntime>(visualEntity);
        }

        if (targetLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<Target>(visualEntity);
        }

        if (lastKnownTargetLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<LastKnownTarget>(visualEntity);
        }

        if (rocketLaunchScatterLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<RocketLaunchScatter>(visualEntity);
        }

        if (rocketTrailResetLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<RocketTrailResetRequest>(visualEntity);
        }

        if (projectilePoolMemberLookup.HasComponent(prefabEntity))
        {
            ecb.RemoveComponent<ProjectilePoolMember>(visualEntity);
        }
    }

}
