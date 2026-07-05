using Unity.Entities;
using UnityEngine;

public class SelfDeleterAuthoring : MonoBehaviour
{
    public float LifeTime=1;
    class Baker : Baker<SelfDeleterAuthoring>
    {
        public override void Bake(SelfDeleterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SelfDeleter
            {
                LifeTime = authoring.LifeTime
            });
        }
    }
}
public struct SelfDeleter : IComponentData
{
    public float LifeTime;
}
