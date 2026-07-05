using Unity.Entities;
using UnityEngine;

public class ExplosionParticleAuthoring : MonoBehaviour
{
    public float playTime;
    public GameObject sparkGameObject;
    public GameObject flashGameObject;
    public GameObject fireGameObject;
    public GameObject smokeGameObject;
    class Baker : Baker<ExplosionParticleAuthoring>
    {
        public override void Bake(ExplosionParticleAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ExplosionParticleStarter
            {
                sparkEntity = GetEntity(authoring.sparkGameObject, TransformUsageFlags.Dynamic),
                flashEntity = GetEntity(authoring.flashGameObject, TransformUsageFlags.Dynamic),
                fireEntity = GetEntity(authoring.fireGameObject, TransformUsageFlags.Dynamic),
                smokeEntity = GetEntity(authoring.smokeGameObject, TransformUsageFlags.Dynamic),
                playTime = authoring.playTime,
            });
        }
    }
}
public struct ExplosionParticleStarter : IComponentData
{
    public Entity sparkEntity;
    public Entity flashEntity;
    public Entity fireEntity;
    public Entity smokeEntity;

    public float playTime;
}
