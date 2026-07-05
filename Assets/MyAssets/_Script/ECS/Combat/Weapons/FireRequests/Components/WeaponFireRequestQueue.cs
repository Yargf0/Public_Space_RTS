using Unity.Entities;

// queue so we don't create entity for every shot
public struct WeaponFireRequestQueueSingleton : IComponentData
{
}

[InternalBufferCapacity(0)]
public struct BallisticWeaponFireRequestElement : IBufferElementData
{
    public WeaponFireRequest Value;
}

[InternalBufferCapacity(0)]
public struct RocketWeaponFireRequestElement : IBufferElementData
{
    public WeaponFireRequest Value;
}

[InternalBufferCapacity(0)]
public struct HitscanWeaponFireRequestElement : IBufferElementData
{
    public WeaponFireRequest Value;
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct WeaponFireRequestQueueBootstrapSystem : ISystem
{
    private EntityQuery queueQuery;

    public void OnCreate(ref SystemState state)
    {
        queueQuery = state.GetEntityQuery(ComponentType.ReadOnly<WeaponFireRequestQueueSingleton>());
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity queueEntity;
        if (queueQuery.IsEmptyIgnoreFilter)
        {
            queueEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<WeaponFireRequestQueueSingleton>(queueEntity);
        }
        else
        {
            queueEntity = queueQuery.GetSingletonEntity();
        }

        if (!state.EntityManager.HasComponent<BallisticWeaponFireRequestElement>(queueEntity))
        {
            state.EntityManager.AddBuffer<BallisticWeaponFireRequestElement>(queueEntity);
        }

        if (!state.EntityManager.HasComponent<RocketWeaponFireRequestElement>(queueEntity))
        {
            state.EntityManager.AddBuffer<RocketWeaponFireRequestElement>(queueEntity);
        }

        if (!state.EntityManager.HasComponent<HitscanWeaponFireRequestElement>(queueEntity))
        {
            state.EntityManager.AddBuffer<HitscanWeaponFireRequestElement>(queueEntity);
        }

        state.Enabled = false;
    }
}
