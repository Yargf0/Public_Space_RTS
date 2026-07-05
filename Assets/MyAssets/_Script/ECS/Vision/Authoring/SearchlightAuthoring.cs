using Unity.Entities;
using UnityEngine;

public class SearchlightAuthoring : MonoBehaviour
{
    [Header("Detection Cone")]
    public float range = 20f;

    [Range(1f, 360f)]
    public float coneAngle = 60f;

    public float opacity = 0.1f;

    [Header("Gameplay visibility")]
    public float keepVisibleSeconds = 0.1f;
    public float scanInterval = 0.02f;

    [Tooltip("Faction this sensor searches for.")]
    public Faction scansFaction = Faction.Enemy;

    [Tooltip("Faction that receives visibility from this sensor.")]
    public Faction observerFaction = Faction.Friendly;

    class Baker : Baker<SearchlightAuthoring>
    {
        public override void Bake(SearchlightAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Searchlight
            {
                range = authoring.range,
                coneAngle = authoring.coneAngle,
                opacity = authoring.opacity,
                keepVisibleSeconds = authoring.keepVisibleSeconds,
                scanInterval = authoring.scanInterval,
                scansFaction = authoring.scansFaction,
                observerFaction = authoring.observerFaction,
            });

            AddComponent(entity, new SearchlightState
            {
                ScanTimer = 0f
            });
        }
    }
}

public struct Searchlight : IComponentData
{
    public float range;
    public float coneAngle;
    public float opacity;
    public float scanInterval;
    public float keepVisibleSeconds;
    public Faction scansFaction;
    public Faction observerFaction;
}

public struct SearchlightState : IComponentData
{
    public float ScanTimer;
}

public struct SelfSearchlight : IComponentData
{

}
