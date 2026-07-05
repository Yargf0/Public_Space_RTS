using Unity.Entities;
using UnityEngine;

public class BlowRadiusAuthoring : MonoBehaviour
{
    public float blowRadius;
    class Baker : Baker<BlowRadiusAuthoring>
    {
        public override void Bake(BlowRadiusAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BlowRadius
            {
                blowRadius = authoring.blowRadius,
                firstTime = false,
            });
        }
    }
}
public struct BlowRadius : IComponentData
{
    public float blowRadius;
    public bool firstTime;
}