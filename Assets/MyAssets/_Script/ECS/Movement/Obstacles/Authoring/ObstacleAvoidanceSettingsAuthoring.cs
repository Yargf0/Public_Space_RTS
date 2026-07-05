using Unity.Entities;
using UnityEngine;

// global settings for obstacle bake and avoidance, put next to FlowFieldDataAuthoring
public class ObstacleAvoidanceSettingsAuthoring : MonoBehaviour
{
    [Header("Bake")]
    [Tooltip("Cost of walking through a soft obstacle zone. A normal cell has Cost = 1.")]
    [Range(2, 100)]
    public int SoftCost = 10;

    [Header("Pathfinding Clearance")]
    [Tooltip("Extra radius added to hard obstacle cells for Small pathfinding layer.")]
    [Min(0f)]
    public float PathClearanceSmall = 0.5f;

    [Tooltip("Extra radius added to hard obstacle cells for Medium pathfinding layer.")]
    [Min(0f)]
    public float PathClearanceMedium = 1.5f;

    [Tooltip("Minimum extra radius added to hard obstacle cells for Large pathfinding layer.")]
    [Min(0f)]
    public float PathClearanceLarge = 3.0f;

    [Header("Avoidance")]
    [Tooltip("Base sampling radius in FlowField cells. Large ships expand it automatically by body size.")]
    [Range(1, 5)]
    public int SampleRadius = 2;

    [Tooltip("Local obstacle repulsion strength.")]
    public float Strength = 30f;

    [Tooltip("Maximum final repulsion vector length.")]
    public float MaxRepulsion = 50f;

    [Tooltip("Weak preventive force for FlowField movement states. 0.1 mostly avoids fighting the route, but protects obstacle edges.")]
    [Range(0f, 1f)]
    public float FlowFieldSafetyMultiplier = 0.1f;

    private class Baker : Baker<ObstacleAvoidanceSettingsAuthoring>
    {
        public override void Bake(ObstacleAvoidanceSettingsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ObstacleAvoidanceSettings
            {
                SoftCost = authoring.SoftCost,
                SampleRadius = authoring.SampleRadius,
                Strength = authoring.Strength,
                MaxRepulsion = authoring.MaxRepulsion,
                FlowFieldSafetyMultiplier = authoring.FlowFieldSafetyMultiplier,
                PathClearanceSmall = authoring.PathClearanceSmall,
                PathClearanceMedium = authoring.PathClearanceMedium,
                PathClearanceLarge = authoring.PathClearanceLarge,
            });
        }
    }
}

public struct ObstacleAvoidanceSettings : IComponentData
{
    public int SoftCost;
    public int SampleRadius;
    public float Strength;
    public float MaxRepulsion;
    public float FlowFieldSafetyMultiplier;
    public float PathClearanceSmall;
    public float PathClearanceMedium;
    public float PathClearanceLarge;

    public static ObstacleAvoidanceSettings CreateDefault()
    {
        return new ObstacleAvoidanceSettings
        {
            SoftCost = 10,
            SampleRadius = 2,
            Strength = 30f,
            MaxRepulsion = 50f,
            FlowFieldSafetyMultiplier = 0.1f,
            PathClearanceSmall = 0.5f,
            PathClearanceMedium = 1.5f,
            PathClearanceLarge = 3.0f,
        };
    }
}
