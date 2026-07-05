using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// authoring for beam prefab (quad + additive material)
// use with SelfDeleterAuthoring, lifetime ~0.05-0.1 sec
public class HitScanBeamAuthoring : MonoBehaviour
{
    class Baker : Baker<HitScanBeamAuthoring>
    {
        public override void Bake(HitScanBeamAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);

            // PostTransformMatrix for non-uniform scale (beam width x length)
            AddComponent<PostTransformMatrix>(entity);
            SetComponent(entity, new PostTransformMatrix { Value = float4x4.identity });
        }
    }
}