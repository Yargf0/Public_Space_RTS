using Unity.Entities;
using UnityEngine;

class ShipGridAuthoring : MonoBehaviour
{
    class Baker : Baker<ShipGridAuthoring>
    {
        public override void Bake(ShipGridAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ShipToGrid());
        }
    }
}

public struct ShipToGrid : IComponentData
{
}
