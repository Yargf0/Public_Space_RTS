using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class BulletAuthoring : MonoBehaviour
{
    public float speed;
    public float timer;
    public float distance;

    class Baker : Baker<BulletAuthoring>
    {
        public override void Bake(BulletAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Bullet
            {
                speed = authoring.speed,
                timer = authoring.timer,
                distance = authoring.distance,
            });

            // keep enabled on prefab, pooled instances get disabled after prewarm
            AddComponent<BulletActive>(entity);

            // archetype must be final before firing
            // hot path does only SetComponent, never AddComponent
            AddComponent(entity, new WeaponPayloadRuntime());

            // baked so prewarm can use SetComponentData
            AddComponent(entity, new ProjectilePoolMember
            {
                poolEntity = Entity.Null,
                prefabEntity = Entity.Null,
                kind = ProjectilePoolKind.Bullet,
                inPool = 0,
            });
        }
    }
}

public struct Bullet : IComponentData
{
    public float speed;
    public float timer;
    public float distance;
    public float2 movementVector;
}
