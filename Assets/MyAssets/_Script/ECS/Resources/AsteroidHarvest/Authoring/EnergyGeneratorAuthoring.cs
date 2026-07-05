using Unity.Entities;
using UnityEngine;

public class EnergyGeneratorAuthoring : MonoBehaviour
{
    [Header("Energy generation")]
    public float energyPerTick = 2f;
    public float tickInterval = 1f;

    class Baker : Baker<EnergyGeneratorAuthoring>
    {
        public override void Bake(EnergyGeneratorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new EnergyGenerator
            {
                energyPerTick = authoring.energyPerTick,
                tickInterval = authoring.tickInterval,
                tickTimer = 0f,
            });
        }
    }
}

// energy generator ship component
// adds energy to ResourceData every tick while alive
public struct EnergyGenerator : IComponentData
{
    public float energyPerTick;
    public float tickInterval;
    public float tickTimer;
}