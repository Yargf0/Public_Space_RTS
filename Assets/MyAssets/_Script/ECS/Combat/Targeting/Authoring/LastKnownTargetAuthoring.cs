using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class LastKnownTargetAuthoring : MonoBehaviour
{
    class Baker : Baker<LastKnownTargetAuthoring>
    {
        public override void Bake(LastKnownTargetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new LastKnownTarget
            {
                target = Entity.Null,
                lastKnownPosition = float2.zero,
                searchTimer = 0f,
            });

            SetComponentEnabled<LastKnownTarget>(entity, false);
        }
    }
}

public struct LastKnownTarget : IComponentData, IEnableableComponent
{
    public Entity target;
    public float2 lastKnownPosition;
    public float searchTimer;
}
