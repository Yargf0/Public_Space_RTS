using Unity.Entities;
using UnityEngine;

// static obstacle for FlowField grid baking
public class ObstacleAuthoring : MonoBehaviour
{
    [Header("Obstacle")]
    [Tooltip("Hard obstacle radius in world units. Cells inside become non-walkable.")]
    public float Radius = 3f;

    [Tooltip("Extra soft radius around the obstacle. Cells inside receive higher movement cost.")]
    public float SoftMargin = 2f;

    private class Baker : Baker<ObstacleAuthoring>
    {
        public override void Bake(ObstacleAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.WorldSpace);

            AddComponent(entity, new ObstacleSourceComponent
            {
                Radius = authoring.Radius,
                SoftMargin = authoring.SoftMargin,
            });
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        float hardRadius = Mathf.Max(0f, Radius);
        float softRadius = Mathf.Max(hardRadius, Radius + SoftMargin);
        Vector3 center = transform.position;

        // red circle = hard blocked zone
        UnityEditor.Handles.color = new Color(1f, 0.1f, 0.1f, 1f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.forward, hardRadius);

        // yellow circle = soft high-cost zone
        UnityEditor.Handles.color = new Color(1f, 0.85f, 0.05f, 1f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.forward, softRadius);
    }
#endif
}

// obstacle source, read by ObstacleBakeSystem
public struct ObstacleSourceComponent : IComponentData
{
    public float Radius;
    public float SoftMargin;
}
