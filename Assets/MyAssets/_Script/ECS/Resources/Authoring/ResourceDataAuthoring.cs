using Unity.Entities;
using UnityEngine;

// resource singleton, one for whole game
// HarvesterSystem adds, BuildSystem spends, ResourceUi shows
public class ResourceDataAuthoring : MonoBehaviour
{
    public float startMetal = 200f;
    public float startEnergy = 100f;
    public float startCrystal = 0f;

    class Baker : Baker<ResourceDataAuthoring>
    {
        public override void Bake(ResourceDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ResourceData
            {
                metal = authoring.startMetal,
                energy = authoring.startEnergy,
                crystal = authoring.startCrystal,
            });
        }
    }
}

public struct ResourceData : IComponentData
{
    public float metal;
    public float energy;
    public float crystal;
}