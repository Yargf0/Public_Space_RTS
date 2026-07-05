using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class UnitCollisionRadiusAuthoring : MonoBehaviour
{
    public float2 collisionRadius = new float2(1f, 1f);

    [Header("Gizmos")]
    public bool drawAlways = true;
    public bool drawOnlySelected = false;
    public Color gizmoColor = new Color(0f, 1f, 1f, 0.9f);

    class Baker : Baker<UnitCollisionRadiusAuthoring>
    {
        public override void Bake(UnitCollisionRadiusAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitCollisionRadius
            {
                collisionRadius = authoring.collisionRadius,
            });
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawAlways) return;
        if (drawOnlySelected) return;
        DrawRadiusGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (drawOnlySelected || drawAlways)
            DrawRadiusGizmo();
    }

    private void DrawRadiusGizmo()
    {
        float rx = Mathf.Max(0f, Mathf.Abs(collisionRadius.x));
        float ry = Mathf.Max(0f, Mathf.Abs(collisionRadius.y));
        if (rx <= 0f && ry <= 0f) return;

        Handles.color = gizmoColor;

        Matrix4x4 old = Handles.matrix;
        Handles.matrix = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(rx, ry, 1f));

        Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f);

        Handles.matrix = old;
    }
#endif
}

public struct UnitCollisionRadius : IComponentData
{
    public float2 collisionRadius; 
}
