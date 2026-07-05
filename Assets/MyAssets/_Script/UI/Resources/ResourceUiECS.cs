using TMPro;
using Unity.Entities;
using UnityEngine;

// reads ResourceData singleton and updates 3 resource labels
public class ResourceUiECS : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI metalText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI crystalText;

    private EntityManager entityManager;
    private EntityQuery resourceQuery;
    private bool initialized;

    private void TryInitialize()
    {
        if (initialized)
        {
            return;
        }

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        entityManager = world.EntityManager;
        resourceQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ResourceData>());
        initialized = true;
    }

    private void Update()
    {
        TryInitialize();
        if (!initialized || resourceQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        ResourceData resourceData = resourceQuery.GetSingleton<ResourceData>();

        if (metalText != null)
        {
            metalText.text = ((int)resourceData.metal).ToString();
        }

        if (energyText != null)
        {
            energyText.text = ((int)resourceData.energy).ToString();
        }

        if (crystalText != null)
        {
            crystalText.text = ((int)resourceData.crystal).ToString();
        }
    }

}