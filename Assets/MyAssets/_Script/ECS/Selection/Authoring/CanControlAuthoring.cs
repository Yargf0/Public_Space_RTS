using Unity.Entities;
using UnityEngine;

class CanControlAuthoring : MonoBehaviour
{

    class Baker : Baker<CanControlAuthoring>
    {
        public override void Bake(CanControlAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new CanControl());
        }
    }
}


public struct CanControl : IComponentData
{

}
