using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class TargetAuthoring : MonoBehaviour
{
    public Faction targetFaction;
    class Baker : Baker<TargetAuthoring>
    {
        public override void Bake(TargetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Target
            {
               // targetEntity = GetEntity(authoring.targetGameObject, TransformUsageFlags.Dynamic)
               targetFaction= authoring.targetFaction,
            });
        }
    }
}

public struct Target : IComponentData
{
    public Entity targetEntity;
    public Faction targetFaction;
    public float2 targetPosition;
}

