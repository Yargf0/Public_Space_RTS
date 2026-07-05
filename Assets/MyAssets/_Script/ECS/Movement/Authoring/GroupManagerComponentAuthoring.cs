using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class GroupManagerComponentAuthoring : MonoBehaviour
{
    class Baker : Baker<GroupManagerComponentAuthoring>
    {
        public override void Bake(GroupManagerComponentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new GroupManagerComponent
            ());
        }
    }
}
public struct GroupManagerComponent : IComponentData
{
    public float2 position;
    public bool addOrCreateGroup;
    public bool setTargetWithoutGroup;
    public bool partOfSwarm;
    public PathfindingSizeClass overrideSizeClass;
    public bool useOverrideSizeClass;
}

