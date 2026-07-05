using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// destroy visuals only when owner is really gone
// pooled/dead owner = just hide visuals and reset flags
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedActionVisualSystem))]
public partial struct EmbeddedActionVisualCleanupSystem : ISystem
{
    private ComponentLookup<EmbeddedWeaponHost> hostLookup;
    private BufferLookup<EmbeddedActionVisualRuntime> visualRuntimeLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;

    public void OnCreate(ref SystemState state)
    {
        hostLookup = state.GetComponentLookup<EmbeddedWeaponHost>(true);
        visualRuntimeLookup = state.GetBufferLookup<EmbeddedActionVisualRuntime>(false);
        healthLookup = state.GetComponentLookup<Health>(true);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(false);
        postTransformMatrixLookup = state.GetComponentLookup<PostTransformMatrix>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        hostLookup.Update(ref state);
        visualRuntimeLookup.Update(ref state);
        healthLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        postTransformMatrixLookup.Update(ref state);

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        bool changed = false;

        foreach ((RefRO<EmbeddedActionVisualOwner> ownerRef, Entity visualEntity) in
                 SystemAPI.Query<RefRO<EmbeddedActionVisualOwner>>()
                     .WithEntityAccess())
        {
            EmbeddedActionVisualOwner ownerData = ownerRef.ValueRO;
            Entity owner = ownerData.owner;
            int slotIndex = ownerData.slotIndex;

            bool ownerRelationLost = owner == Entity.Null ||
                                     !hostLookup.HasComponent(owner) ||
                                     !visualRuntimeLookup.HasBuffer(owner);

            if (ownerRelationLost)
            {
                ecb.DestroyEntity(visualEntity);
                changed = true;
                continue;
            }

            DynamicBuffer<EmbeddedActionVisualRuntime> visuals = visualRuntimeLookup[owner];
            int referencedIndex = -1;

            if (slotIndex >= 0 && slotIndex < visuals.Length && visuals[slotIndex].visualEntity == visualEntity)
            {
                referencedIndex = slotIndex;
            }
            else
            {
                // fallback for old baked data
                for (int i = 0; i < visuals.Length; i++)
                {
                    if (visuals[i].visualEntity == visualEntity)
                    {
                        referencedIndex = i;
                        break;
                    }
                }
            }

            if (referencedIndex < 0)
            {
                ecb.DestroyEntity(visualEntity);
                changed = true;
                continue;
            }

            if (healthLookup.TryGetComponent(owner, out Health hp) && hp.healthAmount <= 0f)
            {
                EmbeddedActionVisualRuntime visual = visuals[referencedIndex];
                if (visual.flags != 0 ||
                    visual.visibleUntil != 0f ||
                    visual.kind != (byte)EmbeddedActionVisualKind.None ||
                    visual.range != 0f ||
                    math.lengthsq(visual.startWorld) > 0f ||
                    math.lengthsq(visual.endWorld) > 0f)
                {
                    visual.visibleUntil = 0f;
                    visual.startWorld = float2.zero;
                    visual.endWorld = float2.zero;
                    visual.range = 0f;
                    visual.kind = (byte)EmbeddedActionVisualKind.None;
                    visual.flags = 0;
                    visuals[referencedIndex] = visual;
                }

                HideVisualIfNeeded(visualEntity);
            }
        }

        if (changed)
        {
            ecb.Playback(state.EntityManager);
        }
    }

    private void HideVisualIfNeeded(Entity visualEntity)
    {
        if (localTransformLookup.HasComponent(visualEntity))
        {
            LocalTransform transform = localTransformLookup[visualEntity];
            if (transform.Scale != 0f)
            {
                transform.Scale = 0f;
                localTransformLookup[visualEntity] = transform;
            }
        }

        if (postTransformMatrixLookup.HasComponent(visualEntity))
        {
            PostTransformMatrix matrix = postTransformMatrixLookup[visualEntity];
            if (math.lengthsq(matrix.Value.c0) > 0.0000001f || math.lengthsq(matrix.Value.c1) > 0.0000001f)
            {
                postTransformMatrixLookup[visualEntity] = new PostTransformMatrix
                {
                    Value = float4x4.TRS(float3.zero, quaternion.identity, new float3(0f, 0f, 1f))
                };
            }
        }
    }
}
