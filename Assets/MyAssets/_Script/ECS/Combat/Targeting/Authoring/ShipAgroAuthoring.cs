using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class ShipAgroAuthoring : MonoBehaviour
{
    public float detectionTime;
    public bool needDistance;
    public float detectionRadius;

    class Baker : Baker<ShipAgroAuthoring>
    {
        public override void Bake(ShipAgroAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ShipAgro
            {
                detectionRadius = authoring.detectionRadius,
                needDistance = authoring.needDistance,
                detectionTime = authoring.detectionTime,
                attackRangeMin = int.MaxValue,
                attackRangeMax = 0,
                wasHit = false,         
                wasHitTimer = 0f,       
            });
            SetComponentEnabled<ShipAgro>(entity, false);
        }
    }
}

public struct ShipAgro : IComponentData, IEnableableComponent
{
    public bool needDistance;
    public float attackRangeMin;
    public float attackRangeMax;
    public float detectionRadius;
    public float timer;
    public float detectionTime;
    public Entity targetEntity;
    public float2 targetPosition;

    public bool wasHit;
    public float wasHitTimer;
}