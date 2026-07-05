using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class SearchlightVisualAuthoring : MonoBehaviour
{
    [Tooltip("Optional owner whose Searchlight drives this visual. Leave empty when this entity has its own Searchlight.")]
    public GameObject searchlightOwner;

    class Baker : Baker<SearchlightVisualAuthoring>
    {
        public override void Bake(SearchlightVisualAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);

            AddComponent<SearchlightVisual>(entity);

            AddComponent(entity, new LightMask_IsCircle { Value = 0f });
            AddComponent(entity, new LightMask_Opacity { Value = 0.1f });

            AddComponent<PostTransformMatrix>(entity);
            SetComponent(entity, new PostTransformMatrix { Value = float4x4.identity });

            if (authoring.searchlightOwner == null)
                return;

            if (authoring.GetComponent<SearchlightAuthoring>() != null)
            {
                Debug.LogError($"SearchlightVisualAuthoring '{authoring.name}': Owner-driven visual must not also have SearchlightAuthoring, otherwise it will scan twice.", authoring);
            }

            AddComponent(entity, new SearchlightVisualOwner
            {
                Owner = GetEntity(authoring.searchlightOwner, TransformUsageFlags.Dynamic),
            });
        }
    }
}

public struct SearchlightVisual : IComponentData
{

}

public struct SearchlightVisualOwner : IComponentData
{
    public Entity Owner;
}

public struct SelfSearchlightVisualInstance : IComponentData
{
    public Entity Visual;
}

public struct SelfSearchlightVisualRuntime : IComponentData
{

}

[MaterialProperty("_IsCircle")]
public struct LightMask_IsCircle : IComponentData
{
    public float Value;
}
[MaterialProperty("_Opacity")]
public struct LightMask_Opacity : IComponentData
{
    public float Value;
}
