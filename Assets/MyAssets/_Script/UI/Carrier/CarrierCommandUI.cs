using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class CarrierCommandUI : MonoBehaviour
{
    public void SetAutoLaunchOnSelected()
    {
        SetStanceOnSelected(CarrierStance.AutoLaunch);
    }

    public void SetHoldDeckOnSelected()
    {
        SetStanceOnSelected(CarrierStance.HoldDeck);
    }

    public void SetRecallAllOnSelected()
    {
        SetStanceOnSelected(CarrierStance.RecallAll);
    }

    public void ToggleLaunchModeOnSelected()
    {
        if (World.DefaultGameObjectInjectionWorld == null)
        {
            return;
        }

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<CarrierTag, CarrierHangarState, Selected>()
            .Build(entityManager);

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            CarrierHangarState hangar = entityManager.GetComponentData<CarrierHangarState>(entity);

            hangar.stance = hangar.stance == CarrierStance.AutoLaunch
                ? CarrierStance.HoldDeck
                : CarrierStance.AutoLaunch;

            entityManager.SetComponentData(entity, hangar);
        }

        entities.Dispose();
        query.Dispose();
    }

    private void SetStanceOnSelected(CarrierStance stance)
    {
        if (World.DefaultGameObjectInjectionWorld == null)
        {
            return;
        }

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<CarrierTag, CarrierHangarState, Selected>()
            .Build(entityManager);

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            CarrierHangarState hangar = entityManager.GetComponentData<CarrierHangarState>(entity);
            hangar.stance = stance;
            entityManager.SetComponentData(entity, hangar);
        }

        entities.Dispose();
        query.Dispose();
    }
}