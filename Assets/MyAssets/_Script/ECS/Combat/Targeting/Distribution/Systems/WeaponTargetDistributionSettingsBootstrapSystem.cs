using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct WeaponTargetDistributionSettingsBootstrapSystem : ISystem
{
    private EntityQuery settingsQuery;

    public void OnCreate(ref SystemState state)
    {
        settingsQuery = state.GetEntityQuery(ComponentType.ReadOnly<WeaponTargetDistributionSettings>());
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!settingsQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        Entity entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, WeaponTargetDistributionDefaults.Create());
    }
}
