using Unity.Entities;
using UnityEngine;

class HealthAuthoring : MonoBehaviour
{
    public float healthAmount;
    public float healthAmountMax;
    public bool destroyOnZeroHealth = true;

    class Baker : Baker<HealthAuthoring>
    {
        public override void Bake(HealthAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Health
            {
                healthAmount = authoring.healthAmount,
                healthAmountMax = authoring.healthAmountMax,
                onHealthChanged = true,
                destroyOnZeroHealth = authoring.destroyOnZeroHealth,
            });
        }
    }
}

public struct Health : IComponentData
{
    public float healthAmount;
    public float healthAmountMax;
    public bool onHealthChanged;
    public bool destroyOnZeroHealth;
}