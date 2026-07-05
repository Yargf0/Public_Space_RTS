using Unity.Entities;
using UnityEngine;

class UnitAuthoring : MonoBehaviour
{
    [Header("Unit")]
    public Faction Faction;
    public ShipSize ShipSize;

    [Header("Fog of War")]
    [Tooltip("When enabled, target search respects Visibility.")]
    public bool useFogOfWar;

    [Tooltip("Adds a circular Searchlight sensor to this ship.")]
    public bool addSelfSearchlight = true;

    public float searchlightRange = 35f;
    public float keepVisibleSeconds = 0.3f;
    public float scanInterval = 0.05f;

    class Baker : Baker<UnitAuthoring>
    {
        public override void Bake(UnitAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Unit
            {
                faction = authoring.Faction,
                shipSize = (byte)authoring.ShipSize,
            });

            AddDisabledEmbeddedActionStatuses(entity);

            // any ship can become visible, even if it don't use fog of war itself
            AddComponent(entity, new Visibility
            {
                visibleToFriendlyTimer = 0f,
                visibleToEnemyTimer = 0f,
            });
            SetComponentEnabled<Visibility>(entity, false);

            AddComponent(entity, new LastKnownTarget
            {
                target = Entity.Null,
                lastKnownPosition = default,
                searchTimer = 0f,
            });
            SetComponentEnabled<LastKnownTarget>(entity, false);

            if (!authoring.useFogOfWar)
            {
                return;
            }

            AddComponent<UseFogOfWar>(entity);

            if (!authoring.addSelfSearchlight)
            {
                return;
            }

            AddComponent(entity, new Searchlight
            {
                range = authoring.searchlightRange,
                coneAngle = 360f,
                opacity = 0f,
                scanInterval = authoring.scanInterval,
                keepVisibleSeconds = authoring.keepVisibleSeconds,
                scansFaction = VisibilityUtility.Opposite(authoring.Faction),
                observerFaction = authoring.Faction,
            });

            AddComponent(entity, new SearchlightState
            {
                ScanTimer = 0f,
            });

            AddComponent<SelfSearchlight>(entity);
        }

        private void AddDisabledEmbeddedActionStatuses(Entity entity)
        {
            AddComponent(entity, new EmpStatus
            {
                timer = 0f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
                disableWeapons = false,
            });
            SetComponentEnabled<EmpStatus>(entity, false);

            AddComponent(entity, new EmbeddedActionBuffStatus
            {
                timer = 0f,
                effectMultiplier = 1f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
            });
            SetComponentEnabled<EmbeddedActionBuffStatus>(entity, false);

            AddComponent(entity, new EmbeddedActionDebuffStatus
            {
                timer = 0f,
                effectMultiplier = 1f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
                disableWeapons = false,
            });
            SetComponentEnabled<EmbeddedActionDebuffStatus>(entity, false);
        }
    }
}

public struct Unit : IComponentData
{
    public Faction faction;
    public byte shipSize;
}

public struct UseFogOfWar : IComponentData
{
}
