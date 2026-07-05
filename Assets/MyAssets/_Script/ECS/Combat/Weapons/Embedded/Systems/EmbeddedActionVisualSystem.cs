using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// renders Beam/Aura visuals. no Instantiate, InitSystem made them already
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedAuraActionSystem))]
[UpdateBefore(typeof(EmbeddedWeaponVisualSyncSystem))]
public partial struct EmbeddedActionVisualSystem : ISystem
{
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;

    public void OnCreate(ref SystemState state)
    {
        localTransformLookup = state.GetComponentLookup<LocalTransform>(false);
        postTransformMatrixLookup = state.GetComponentLookup<PostTransformMatrix>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float now = (float)SystemAPI.Time.ElapsedTime;

        localTransformLookup.Update(ref state);
        postTransformMatrixLookup.Update(ref state);

        foreach ((DynamicBuffer<EmbeddedActionVisualRuntime> visuals, RefRW<EmbeddedActionHostRuntime> hostRuntime) in
                 SystemAPI.Query<DynamicBuffer<EmbeddedActionVisualRuntime>, RefRW<EmbeddedActionHostRuntime>>()
                     .WithAll<EmbeddedWeaponHost>())
        {
            DynamicBuffer<EmbeddedActionVisualRuntime> visualBuffer = visuals;

            if (!EmbeddedActionRuntimeUtility.IsActionHostWorkDue(hostRuntime.ValueRO.nextVisualWorkTime, now))
            {
                continue;
            }

            for (int i = 0; i < visualBuffer.Length; i++)
            {
                EmbeddedActionVisualRuntime visual = visualBuffer[i];
                if (visual.visualEntity == Entity.Null || !localTransformLookup.HasComponent(visual.visualEntity))
                {
                    // broken visual must not keep Dirty/Visible forever or system wakes every frame
                    visual.visualEntity = Entity.Null;
                    ResetVisualRuntime(ref visual);
                    visualBuffer[i] = visual;
                    continue;
                }

                bool visible = (visual.flags & EmbeddedActionVisualRuntimeFlags.Visible) != 0;
                bool dirty = (visual.flags & EmbeddedActionVisualRuntimeFlags.Dirty) != 0;

                if (!visible)
                {
                    continue;
                }

                if (visual.visibleUntil <= now)
                {
                    HideVisual(visual.visualEntity);
                    ResetVisualRuntime(ref visual);
                    visualBuffer[i] = visual;
                    continue;
                }

                if (!dirty)
                {
                    continue;
                }

                switch ((EmbeddedActionVisualKind)visual.kind)
                {
                    case EmbeddedActionVisualKind.Beam:
                        ApplyBeamVisual(in visual);
                        break;

                    case EmbeddedActionVisualKind.Aura:
                        ApplyAuraVisual(in visual);
                        break;
                }

                visual.flags = (byte)(visual.flags & ~EmbeddedActionVisualRuntimeFlags.Dirty);
                visualBuffer[i] = visual;
            }

            EmbeddedActionHostRuntime runtime = hostRuntime.ValueRO;
            EmbeddedActionRuntimeUtility.RefreshHostVisualWorkTime(ref runtime, visualBuffer);
            hostRuntime.ValueRW = runtime;
        }
    }

    private static void ResetVisualRuntime(ref EmbeddedActionVisualRuntime visual)
    {
        visual.visibleUntil = 0f;
        visual.startWorld = float2.zero;
        visual.endWorld = float2.zero;
        visual.range = 0f;
        visual.kind = (byte)EmbeddedActionVisualKind.None;
        visual.flags = 0;
    }

    private void ApplyBeamVisual(in EmbeddedActionVisualRuntime visual)
    {
        float2 delta = visual.endWorld - visual.startWorld;
        float length = math.length(delta);
        if (length <= 0.001f)
        {
            HideVisual(visual.visualEntity);
            return;
        }

        float2 dir = delta / length;
        float angle = math.atan2(dir.y, dir.x) - math.PI * 0.5f;

        LocalTransform transform = localTransformLookup[visual.visualEntity];
        transform.Position = new float3(visual.startWorld.x, visual.startWorld.y, GameConstants.ProjectileZ);
        transform.Rotation = quaternion.RotateZ(angle);
        transform.Scale = 1f;
        localTransformLookup[visual.visualEntity] = transform;

        if (postTransformMatrixLookup.HasComponent(visual.visualEntity))
        {
            postTransformMatrixLookup[visual.visualEntity] = new PostTransformMatrix
            {
                Value = float4x4.TRS(
                    new float3(0f, length * 0.5f, 0f),
                    quaternion.identity,
                    new float3(math.max(0.01f, visual.width), length, 1f))
            };
        }
    }

    private void ApplyAuraVisual(in EmbeddedActionVisualRuntime visual)
    {
        LocalTransform transform = localTransformLookup[visual.visualEntity];
        transform.Position = new float3(visual.startWorld.x, visual.startWorld.y, GameConstants.ProjectileZ);
        transform.Rotation = quaternion.identity;
        transform.Scale = 1f;
        localTransformLookup[visual.visualEntity] = transform;

        if (postTransformMatrixLookup.HasComponent(visual.visualEntity))
        {
            float diameter = math.max(0.01f, visual.range * 2f);
            postTransformMatrixLookup[visual.visualEntity] = new PostTransformMatrix
            {
                Value = float4x4.TRS(
                    float3.zero,
                    quaternion.identity,
                    new float3(diameter, diameter, 1f))
            };
        }
    }

    private void HideVisual(Entity visualEntity)
    {
        LocalTransform transform = localTransformLookup[visualEntity];
        transform.Scale = 0f;
        localTransformLookup[visualEntity] = transform;

        if (postTransformMatrixLookup.HasComponent(visualEntity))
        {
            postTransformMatrixLookup[visualEntity] = new PostTransformMatrix
            {
                Value = float4x4.TRS(float3.zero, quaternion.identity, new float3(0f, 0f, 1f))
            };
        }
    }
}
