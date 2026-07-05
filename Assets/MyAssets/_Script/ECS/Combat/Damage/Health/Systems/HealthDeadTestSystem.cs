using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SquadronHealthSystem))]
public partial struct HealthDeadTestSystem : ISystem
{
    private ComponentLookup<ShipSquadRef> shipSquadRefLookup;
    private ComponentLookup<SquadMemberDeathEventEmittedTag> deathEventEmittedLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<Unit> unitLookup;

    public void OnCreate(ref SystemState state)
    {
        shipSquadRefLookup = state.GetComponentLookup<ShipSquadRef>(true);
        deathEventEmittedLookup = state.GetComponentLookup<SquadMemberDeathEventEmittedTag>(true);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        unitLookup = state.GetComponentLookup<Unit>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        shipSquadRefLookup.Update(ref state);
        deathEventEmittedLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        unitLookup.Update(ref state);

        // no UpdateAfter(BulletMover etc) here, it makes cycle in system order
        // death from late weapons is handled next frame, ok
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((RefRO<Health> health, Entity entity)
            in SystemAPI.Query<RefRO<Health>>()
                .WithAll<CombatDeathVfxEmittedTag>()
                .WithEntityAccess())
        {
            if (health.ValueRO.healthAmount > 0f)
            {
                ecb.RemoveComponent<CombatDeathVfxEmittedTag>(entity);
            }
        }

        foreach ((RefRO<Health> health, Entity entity)
            in SystemAPI.Query<RefRO<Health>>()
                .WithAbsent<SquadronTag>()
                .WithEntityAccess())
        {
            if (health.ValueRO.healthAmount > 0f)
                continue;

            bool destroyEntity = health.ValueRO.destroyOnZeroHealth;
            bool hasSquadRef = shipSquadRefLookup.TryGetComponent(entity, out ShipSquadRef squadRef);
            bool deathEventAlreadyEmitted = deathEventEmittedLookup.HasComponent(entity);
            bool deathVfxAlreadyEmitted = SystemAPI.HasComponent<CombatDeathVfxEmittedTag>(entity);

            if (!deathVfxAlreadyEmitted &&
                localTransformLookup.TryGetComponent(entity, out LocalTransform deadTransform))
            {
                byte shipSize = (byte)ShipSize.Small;
                Faction faction = Faction.Friendly;
                if (unitLookup.TryGetComponent(entity, out Unit unit))
                {
                    shipSize = unit.shipSize;
                    faction = unit.faction;
                }

                CombatVfxRequestUtility.EnqueueDeathExplosion(
                    ref ecb,
                    deadTransform.Position,
                    shipSize,
                    faction);

                if (!destroyEntity)
                {
                    ecb.AddComponent<CombatDeathVfxEmittedTag>(entity);
                }
            }

            if (hasSquadRef && !deathEventAlreadyEmitted)
            {
                Entity eventEntity = ecb.CreateEntity();
                ecb.AddComponent(eventEntity, new SquadMemberDeathEvent
                {
                    squad = squadRef.squad,
                    ship = entity,
                    slotIndex = squadRef.slotIndex,
                });

                if (!destroyEntity)
                {
                    // guard, or entities at 0 hp spam death events every frame
                    ecb.AddComponent<SquadMemberDeathEventEmittedTag>(entity);
                    ecb.RemoveComponent<ShipSquadRef>(entity);
                }
            }

            if (destroyEntity)
            {
                // no Add/Remove on destroyed entity, ECB will crash
                ecb.DestroyEntity(entity);
            }
        }

        ecb.Playback(state.EntityManager);
    }
}
