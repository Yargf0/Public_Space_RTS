using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class WaypointAuthoring : MonoBehaviour
{
    [Tooltip("Stable id referenced by MissionScript.")]
    public int id;

    [Tooltip("0 = point, >0 = zone radius.")]
    public float radius = 0f;

    private sealed class Baker : Baker<WaypointAuthoring>
    {
        public override void Bake(WaypointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new WaypointTag());
            AddComponent(entity, new Waypoint
            {
                id = authoring.id,
                position = new float2(authoring.transform.position.x, authoring.transform.position.y),
                radius = math.max(0f, authoring.radius),
            });

            if (authoring.radius > 0f)
                AddComponent(entity, new ZoneVolumeTag());
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = radius > 0f
            ? new Color(0.2f, 1f, 0.4f, 0.4f)
            : new Color(1f, 1f, 0.2f, 0.7f);

        if (radius > 0f)
            Gizmos.DrawWireSphere(transform.position, radius);
        else
            Gizmos.DrawWireCube(transform.position, Vector3.one * 2f);
    }
}
