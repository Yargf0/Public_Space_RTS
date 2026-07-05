using Unity.Entities;
using UnityEngine;

public class UnitGroupAuthoring : MonoBehaviour
{
    class Baker : Baker<UnitGroupAuthoring>
    {
        public override void Bake(UnitGroupAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitGroup {  });
        }
    }
}
public struct UnitGroup : IComponentData
{
    public Entity GroupEntity;
}