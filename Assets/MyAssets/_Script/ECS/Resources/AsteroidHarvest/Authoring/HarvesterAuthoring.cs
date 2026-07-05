using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class HarvesterAuthoring : MonoBehaviour
{
    [Header("Mining")]
    public float harvestRadius = 15f;
    public float amountPerTick = 5f;
    public float tickInterval = 1f;

    [Header("Asteroid search")]
    public float searchInterval = 0.5f;

    [Header("Gizmos")]
    public bool drawRadiusAlways = true;
    public bool drawRadiusOnlySelected = false;
    public Color radiusColor = new Color(1f, 0.8f, 0f, 0.4f);

    class Baker : Baker<HarvesterAuthoring>
    {
        public override void Bake(HarvesterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new HarvesterShip
            {
                harvestRadius = authoring.harvestRadius,
                amountPerTick = authoring.amountPerTick,
                tickInterval = authoring.tickInterval,
                tickTimer = 0f,
                searchInterval = authoring.searchInterval,
                searchTimer = 0f,
                targetAsteroid = Entity.Null,
            });
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawRadiusAlways || drawRadiusOnlySelected)
        {
            return;
        }
        DrawHarvestRadius();
    }

    private void OnDrawGizmosSelected()
    {
        if (drawRadiusOnlySelected || drawRadiusAlways)
        {
            DrawHarvestRadius();
        }
    }

    private void DrawHarvestRadius()
    {
        if (harvestRadius <= 0f)
        {
            return;
        }

        Handles.color = radiusColor;
        Handles.DrawWireDisc(transform.position, Vector3.forward, harvestRadius);
    }
#endif
}

// harvester ship component, put on prefab next to ShipBaseAuthoring
public struct HarvesterShip : IComponentData
{
    // search and mining radius
    public float harvestRadius;

    // units per mining tick
    public float amountPerTick;

    // mining tick interval
    public float tickInterval;
    public float tickTimer;

    // how often to search new asteroid
    public float searchInterval;
    public float searchTimer;

    // current target asteroid
    public Entity targetAsteroid;
}