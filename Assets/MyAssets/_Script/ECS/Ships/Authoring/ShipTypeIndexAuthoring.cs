using Unity.Entities;
using UnityEngine;

public class ShipTypeIndexAuthoring : MonoBehaviour
{
    public ShipType shipType;

    class Baker : Baker<ShipTypeIndexAuthoring>
    {
        public override void Bake(ShipTypeIndexAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ShipTypeIndex
            {
                Value = (int)authoring.shipType,
            });
        }
    }
}
public struct ShipTypeIndex : IComponentData
{
    public int Value;
}