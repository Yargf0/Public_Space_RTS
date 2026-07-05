using Unity.Entities;
using UnityEngine;

public class FindTargetAuthoring : MonoBehaviour
{
    public float range;
    public float detectionTime;
    public bool rangeWorkAfterLockTarget = true;
    public ShipSize allowedTargets;
    public ShipSize priorityTargets;
    public class Baker : Baker<FindTargetAuthoring>
    {
        public override void Bake(FindTargetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FindTarget
            {
                range = authoring.range,
                rangeWorkAfterLockTarget = authoring.rangeWorkAfterLockTarget,
                detectionTime = authoring.detectionTime,
                allowedTargets = (byte)authoring.allowedTargets,
                priorityTargets = (byte)authoring.priorityTargets,
            });
            SetComponentEnabled<FindTarget>(entity, false);
        }
    }
}

public struct FindTarget : IComponentData, IEnableableComponent
{
    public float range;
    public bool rangeWorkAfterLockTarget;
    public float timer;
    public float detectionTime;
    public byte allowedTargets;
    public byte priorityTargets;
}
