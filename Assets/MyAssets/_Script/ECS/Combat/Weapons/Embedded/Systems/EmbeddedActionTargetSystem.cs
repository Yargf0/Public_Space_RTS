using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// target search for embedded BeamOverTime actions
// searchTimer is absolute nextSearchTime
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedActionRuntimeInitSystem))]
[UpdateAfter(typeof(EmbeddedWeaponAimSystem))]
[UpdateBefore(typeof(EmbeddedBeamActionSystem))]
public partial struct EmbeddedActionTargetSystem : ISystem
{
    private ComponentLookup<GridData> gridDataLookup;
    private ComponentLookup<Unit> unitLookup;
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<ShipStateComponent> shipStateLookup;
    private ComponentLookup<UseFogOfWar> useFogOfWarLookup;
    private ComponentLookup<Visibility> visibilityLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<UnitCollisionRadius> collisionRadiusLookup;

    public void OnCreate(ref SystemState state)
    {
        gridDataLookup = state.GetComponentLookup<GridData>(true);
        unitLookup = state.GetComponentLookup<Unit>(true);
        healthLookup = state.GetComponentLookup<Health>(true);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(true);
        shipStateLookup = state.GetComponentLookup<ShipStateComponent>(true);
        useFogOfWarLookup = state.GetComponentLookup<UseFogOfWar>(true);
        visibilityLookup = state.GetComponentLookup<Visibility>(true);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(true);
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(true);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(true);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        collisionRadiusLookup = state.GetComponentLookup<UnitCollisionRadius>(true);
        state.RequireForUpdate<GridData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<GridData>(out Entity gridEntity))
        {
            return;
        }

        float now = (float)SystemAPI.Time.ElapsedTime;

        gridDataLookup.Update(ref state);
        unitLookup.Update(ref state);
        healthLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        shipStateLookup.Update(ref state);
        useFogOfWarLookup.Update(ref state);
        visibilityLookup.Update(ref state);
        empStatusLookup.Update(ref state);
        buffStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        collisionRadiusLookup.Update(ref state);

        if (!gridDataLookup.TryGetComponent(gridEntity, out GridData gridData))
        {
            return;
        }

        foreach ((RefRO<LocalTransform> shipLocal,
                  DynamicBuffer<EmbeddedWeaponSlot> slots,
                  DynamicBuffer<EmbeddedActionSlot> actions,
                  RefRW<EmbeddedActionHostRuntime> hostRuntime,
                  Entity shipEntity) in
                 SystemAPI.Query<RefRO<LocalTransform>, DynamicBuffer<EmbeddedWeaponSlot>, DynamicBuffer<EmbeddedActionSlot>, RefRW<EmbeddedActionHostRuntime>>()
                     .WithAll<EmbeddedWeaponHost>()
                     .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedWeaponSlot> slotBuffer = slots;
            DynamicBuffer<EmbeddedActionSlot> actionBuffer = actions;

            if (hostRuntime.ValueRO.hasBeamActions == 0 ||
                !EmbeddedActionRuntimeUtility.IsActionHostWorkDue(hostRuntime.ValueRO.nextTargetWorkTime, now))
            {
                continue;
            }

            if (!unitLookup.TryGetComponent(shipEntity, out Unit ownerUnit))
            {
                hostRuntime.ValueRW.nextTargetWorkTime = now + 0.25f;
                continue;
            }

            if (healthLookup.TryGetComponent(shipEntity, out Health ownerHealth) && ownerHealth.healthAmount <= 0f)
            {
                hostRuntime.ValueRW.nextTargetWorkTime = now + 0.25f;
                continue;
            }

            bool ownerUsesFogOfWar = useFogOfWarLookup.HasComponent(shipEntity);
            bool ownerWeaponsDisabled = EmbeddedActionStatusUtility.AreWeaponsDisabled(shipEntity, ref empStatusLookup, ref debuffStatusLookup);
            bool fireAllowed = EmbeddedActionRulesUtility.IsFireAllowed(shipEntity, ref shipStateLookup, ref shipAgroLookup);

            LocalTransform shipTransform = shipLocal.ValueRO;
            quaternion shipRot = shipTransform.Rotation;
            float3 shipPos = shipTransform.Position;
            Faction ownerFaction = ownerUnit.faction;
            int count = math.min(slotBuffer.Length, actionBuffer.Length);

            for (int i = 0; i < count; i++)
            {
                EmbeddedWeaponSlot slot = slotBuffer[i];
                EmbeddedActionSlot action = actionBuffer[i];

                if ((EmbeddedWeaponSlotRole)slot.role == EmbeddedWeaponSlotRole.Damage ||
                    !EmbeddedActionRuntimeUtility.IsBeamAction(in action))
                {
                    continue;
                }

                if (now < action.searchTimer)
                {
                    continue;
                }

                EmbeddedActionTargetFilter targetFilter = (EmbeddedActionTargetFilter)action.targetFilter;
                EmbeddedActionEffectKind effectKind = (EmbeddedActionEffectKind)action.effectKind;

                bool actionChanged = false;
                if (EmbeddedActionRulesUtility.IsOffensiveAction(targetFilter, effectKind) && (!fireAllowed || ownerWeaponsDisabled))
                {
                    action.searchTimer = now + math.max(0.05f, action.searchInterval);
                    actionBuffer[i] = action;
                    continue;
                }

                float3 pivotWorld = shipPos + math.rotate(shipRot, slot.pivotLocalPosition);
                float2 pivotWorld2 = pivotWorld.xy;

                bool slotChanged = false;
                bool currentValid = false;
                float2 targetPosition = float2.zero;

                action.searchTimer = now + math.max(0.01f, action.searchInterval);
                actionChanged = true;

                if (slot.targetEntity != Entity.Null)
                {
                    currentValid = EmbeddedActionRuntimeUtility.IsValidActionTarget(
                        shipEntity,
                        slot.targetEntity,
                        pivotWorld2,
                        ownerFaction,
                        ownerUsesFogOfWar,
                        targetFilter,
                        effectKind,
                        in action,
                        ref unitLookup,
                        ref healthLookup,
                        ref localTransformLookup,
                        ref collisionRadiusLookup,
                        ref visibilityLookup,
                        out targetPosition,
                        out _);
                }

                if (currentValid)
                {
                    if (math.distancesq(slot.targetPositionWorld, targetPosition) > 0.0001f)
                    {
                        slot.targetPositionWorld = targetPosition;
                        slotChanged = true;
                    }
                }
                else
                {
                    if (slot.targetEntity != Entity.Null || math.lengthsq(slot.targetPositionWorld) > 0.0001f)
                    {
                        slot.targetEntity = Entity.Null;
                        slot.targetPositionWorld = float2.zero;
                        slotChanged = true;
                    }

                    if (targetFilter == EmbeddedActionTargetFilter.Self)
                    {
                        if (EmbeddedActionRuntimeUtility.IsValidActionTarget(
                                shipEntity,
                                shipEntity,
                                pivotWorld2,
                                ownerFaction,
                                ownerUsesFogOfWar,
                                targetFilter,
                                effectKind,
                                in action,
                                ref unitLookup,
                                ref healthLookup,
                                ref localTransformLookup,
                                ref collisionRadiusLookup,
                                ref visibilityLookup,
                                out float2 selfPosition,
                                out _))
                        {
                            slot.targetEntity = shipEntity;
                            slot.targetPositionWorld = selfPosition;
                            slotChanged = true;
                        }
                    }
                    else if (EmbeddedActionRuntimeUtility.TryFindActionTarget(
                                 shipEntity,
                                 pivotWorld2,
                                 ownerFaction,
                                 ownerUsesFogOfWar,
                                 targetFilter,
                                 effectKind,
                                 in action,
                                 in gridData,
                                 ref unitLookup,
                                 ref healthLookup,
                                 ref localTransformLookup,
                                 ref collisionRadiusLookup,
                                 ref visibilityLookup,
                                 out Entity bestTarget,
                                 out float2 bestTargetPosition))
                    {
                        slot.targetEntity = bestTarget;
                        slot.targetPositionWorld = bestTargetPosition;
                        slotChanged = true;
                    }
                }

                if (slotChanged)
                {
                    slotBuffer[i] = slot;
                }

                if (actionChanged)
                {
                    actionBuffer[i] = action;
                }
            }

            EmbeddedActionHostRuntime runtime = hostRuntime.ValueRO;
            EmbeddedActionRuntimeUtility.RefreshHostRuntimeFromActions(ref runtime, actionBuffer);
            runtime.nextVisualWorkTime = hostRuntime.ValueRO.nextVisualWorkTime;
            hostRuntime.ValueRW = runtime;
        }
    }
}
