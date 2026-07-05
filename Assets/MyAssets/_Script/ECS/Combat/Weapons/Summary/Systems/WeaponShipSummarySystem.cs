using Unity.Entities;
using Unity.Mathematics;

public struct WeaponShipSummaryInitialized : IComponentData
{
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ShipAgroSystem))]
[UpdateBefore(typeof(FightSystem))]
public partial struct WeaponShipSummarySystem : ISystem
{
    private const float DefaultIdealDistanceFactor = 0.7f;
    private const float MaxSafeIdealDistanceFactor = 0.90f;

    private EntityQuery shipsNeedingSummaryQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WeaponProfileDatabase>();

        shipsNeedingSummaryQuery = SystemAPI.QueryBuilder()
            .WithAll<ShipAgro, FightLogic>()
            .WithNone<WeaponShipSummaryInitialized>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
            .Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (shipsNeedingSummaryQuery.CalculateEntityCount() == 0)
            return;

        WeaponProfileDatabase database = SystemAPI.GetSingleton<WeaponProfileDatabase>();
        ref WeaponProfileDatabaseBlob root = ref database.Value.Value;

        BufferLookup<EmbeddedWeaponSlot> embeddedSlotLookup = SystemAPI.GetBufferLookup<EmbeddedWeaponSlot>(true);
        ComponentLookup<EmbeddedWeaponHost> embeddedHostLookup = SystemAPI.GetComponentLookup<EmbeddedWeaponHost>(true);

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((RefRW<ShipAgro> shipAgro, RefRW<FightLogic> fightLogic, Entity shipEntity)
            in SystemAPI.Query<RefRW<ShipAgro>, RefRW<FightLogic>>()
                .WithNone<WeaponShipSummaryInitialized>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            float attackRangeMin = float.MaxValue;
            float attackRangeMax = 0f;
            bool foundWeapon = false;

            bool hasEmbeddedSlots = embeddedSlotLookup.HasBuffer(shipEntity);
            if (hasEmbeddedSlots)
            {
                DynamicBuffer<EmbeddedWeaponSlot> slots = embeddedSlotLookup[shipEntity];

                for (int i = 0; i < slots.Length; i++)
                {
                    int profileIndex = slots[i].profileIndex;
                    if ((uint)profileIndex >= (uint)root.Profiles.Length)
                        continue;

                    WeaponProfileBlob profile = root.Profiles[profileIndex];
                    if (profile.attackDistance <= 0f)
                        continue;

                    foundWeapon = true;
                    attackRangeMin = math.min(attackRangeMin, profile.attackDistance);
                    attackRangeMax = math.max(attackRangeMax, profile.attackDistance);
                }
            }

            if (!foundWeapon)
            {
                // embedded ships may get slots later (SubScene loading), don't lock them with atkMax=0
                if (embeddedHostLookup.HasComponent(shipEntity) || hasEmbeddedSlots)
                    continue;

                // weaponless non-embedded ships should not be checked forever
                ecb.AddComponent<WeaponShipSummaryInitialized>(shipEntity);
                continue;
            }

            shipAgro.ValueRW.attackRangeMin = attackRangeMin;
            shipAgro.ValueRW.attackRangeMax = attackRangeMax;

            if (shipAgro.ValueRO.needDistance && shipAgro.ValueRO.detectionRadius <= 0f)
                shipAgro.ValueRW.detectionRadius = attackRangeMax;

            float defaultIdealDistance = attackRangeMax * DefaultIdealDistanceFactor;
            float maxSafeIdealDistance = attackRangeMax * MaxSafeIdealDistanceFactor;

            if (fightLogic.ValueRO.idealDistance <= 0f || fightLogic.ValueRO.idealDistance > maxSafeIdealDistance)
                fightLogic.ValueRW.idealDistance = defaultIdealDistance;

            ecb.AddComponent<WeaponShipSummaryInitialized>(shipEntity);
        }

        ecb.Playback(state.EntityManager);
    }
}