using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
partial struct SearchlightVisualSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ComponentLookup<Searchlight> searchlightLookup = SystemAPI.GetComponentLookup<Searchlight>(true);

        foreach ((
                    RefRW<PostTransformMatrix> post,
                    RefRW<LightMask_IsCircle> isCircleProp,
                    RefRW<LightMask_Opacity> opacityProp,
                    RefRO<Searchlight> searchlight
                )
                in SystemAPI.Query<
                    RefRW<PostTransformMatrix>,
                    RefRW<LightMask_IsCircle>,
                    RefRW<LightMask_Opacity>,
                    RefRO<Searchlight>>()
                .WithAll<SearchlightVisual>()
                .WithNone<SearchlightVisualOwner>())
        {
            ApplyVisual(searchlight.ValueRO, post, isCircleProp, opacityProp);
        }

        foreach ((
                    RefRW<PostTransformMatrix> post,
                    RefRW<LightMask_IsCircle> isCircleProp,
                    RefRW<LightMask_Opacity> opacityProp,
                    RefRO<SearchlightVisualOwner> owner
                )
                in SystemAPI.Query<
                    RefRW<PostTransformMatrix>,
                    RefRW<LightMask_IsCircle>,
                    RefRW<LightMask_Opacity>,
                    RefRO<SearchlightVisualOwner>>()
                .WithAll<SearchlightVisual>())
        {
            if (!searchlightLookup.TryGetComponent(owner.ValueRO.Owner, out Searchlight searchlight))
            {
                HideVisual(post, isCircleProp, opacityProp);
                continue;
            }

            ApplyVisual(searchlight, post, isCircleProp, opacityProp);
        }
    }

    private static void ApplyVisual(
        in Searchlight searchlight,
        RefRW<PostTransformMatrix> post,
        RefRW<LightMask_IsCircle> isCircleProp,
        RefRW<LightMask_Opacity> opacityProp)
    {
        float range = math.max(0.01f, searchlight.range);
        float angle = searchlight.coneAngle;

        bool circle = angle >= 359.9f;

        isCircleProp.ValueRW.Value = circle ? 1f : 0f;
        opacityProp.ValueRW.Value = math.saturate(searchlight.opacity);

        float3 nonUniformScale;

        if (circle)
        {
            float d = 2f * range;
            nonUniformScale = new float3(d, d, 1f);
        }
        else
        {
            float halfRad = math.radians(angle * 0.5f);
            float width = 2f * range * math.tan(halfRad);

            nonUniformScale = new float3(width, range, 1f);
        }

        // Scale is uniform in Entities, non-uniform goes through PostTransformMatrix
        post.ValueRW.Value = float4x4.Scale(nonUniformScale);
    }

    private static void HideVisual(
        RefRW<PostTransformMatrix> post,
        RefRW<LightMask_IsCircle> isCircleProp,
        RefRW<LightMask_Opacity> opacityProp)
    {
        isCircleProp.ValueRW.Value = 1f;
        opacityProp.ValueRW.Value = 0f;
        post.ValueRW.Value = float4x4.identity;
    }
}
