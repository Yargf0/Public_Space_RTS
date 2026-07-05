using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class EnemyAuthoring : MonoBehaviour
{
    class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Enemy());
        }
    }
}

public struct Enemy : IComponentData
{

}

