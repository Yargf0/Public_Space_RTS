using Unity.Entities;
using UnityEngine;

public class VisibilityAuthoring : MonoBehaviour
{
    [Header("Runtime Visibility")]
    public float visibleToFriendlyTimer;
    public float visibleToEnemyTimer;

    class Baker : Baker<VisibilityAuthoring>
    {
        public override void Bake(VisibilityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Visibility
            {
                visibleToFriendlyTimer = authoring.visibleToFriendlyTimer,
                visibleToEnemyTimer = authoring.visibleToEnemyTimer,
            });

            SetComponentEnabled<Visibility>(entity, authoring.visibleToFriendlyTimer > 0f || authoring.visibleToEnemyTimer > 0f);
        }
    }
}
