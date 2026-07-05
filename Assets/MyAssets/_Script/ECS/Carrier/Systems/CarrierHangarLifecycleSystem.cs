using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SquadronCleanupSystem))]
public partial struct CarrierHangarLifecycleSystem : ISystem
{
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<ShipStateComponent> shipStateLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CarrierTag>();
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(isReadOnly: true);
        shipStateLookup = state.GetComponentLookup<ShipStateComponent>(isReadOnly: true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        EntityManager em = state.EntityManager;
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        shipAgroLookup.Update(ref state);
        shipStateLookup.Update(ref state);

        foreach ((
            RefRW<CarrierHangarState> hangar,
            RefRO<LocalTransform> transform,
            Entity carrierEntity)
            in SystemAPI.Query<
                    RefRW<CarrierHangarState>,
                    RefRO<LocalTransform>>()
                .WithAll<CarrierTag>()
                .WithEntityAccess())
        {
            DynamicBuffer<CarrierSquadronTemplateElement> templates =
                SystemAPI.GetBuffer<CarrierSquadronTemplateElement>(carrierEntity);

            DynamicBuffer<CarrierSquadronSlotElement> slots =
                SystemAPI.GetBuffer<CarrierSquadronSlotElement>(carrierEntity);

            CarrierDebugSettings debugSettings = GetDebugSettings(em, carrierEntity);
            bool debugEnabled = debugSettings.enableLogs;

            int activeCount = 0;
            float2 carrierPos = transform.ValueRO.Position.xy;

            for (int i = 0; i < slots.Length; i++)
            {
                CarrierSquadronSlotElement slot = slots[i];

                if (slot.squadronEntity != Entity.Null && !em.Exists(slot.squadronEntity))
                {
                    Entity missingSquad = slot.squadronEntity;
                    CarrierSlotState previousState = slot.state;
                    slot.squadronEntity = Entity.Null;

                    if (slot.state == CarrierSlotState.Launched || slot.state == CarrierSlotState.Returning)
                    {
                        slot.state = CarrierSlotState.QueuedForRebuild;
                        slot.timer = 0f;

#if UNITY_EDITOR
                        if (debugEnabled && debugSettings.logSlotStateChanges)
                        {
                            Debug.LogWarning($"[CarrierHangar] Squadron entity disappeared, slot queued for rebuild. carrier={carrierEntity.Index}:{carrierEntity.Version} slot={i} missingSquad={missingSquad.Index}:{missingSquad.Version} previousState={(int)previousState}");
                        }
#endif
                    }
                }

                switch (slot.state)
                {
                    case CarrierSlotState.Launched:
                    case CarrierSlotState.Returning:
                        activeCount++;

                        HandleRecovery(
                            em,
                            ref ecb,
                            carrierEntity,
                            i,
                            carrierPos,
                            hangar.ValueRO,
                            templates,
                            debugEnabled,
                            debugSettings,
                            ref slot);
                        break;

                    case CarrierSlotState.Servicing:
                        slot.timer -= dt;
                        if (slot.timer <= 0f)
                        {
                            slot.timer = 0f;
                            slot.state = CarrierSlotState.Ready;

#if UNITY_EDITOR
                            if (debugEnabled && debugSettings.logSlotStateChanges)
                                Debug.Log($"[CarrierHangar] Slot service finished. carrier={carrierEntity.Index}:{carrierEntity.Version} slot={i} state={(int)CarrierSlotState.Ready}");
#endif
                        }
                        break;

                    case CarrierSlotState.QueuedForRebuild:
                        if (hangar.ValueRO.rebuildingSlotIndex < 0)
                        {
                            if (slot.templateIndex >= 0 && slot.templateIndex < templates.Length)
                            {
                                CarrierSquadronTemplateElement template = templates[slot.templateIndex];

                                slot.state = CarrierSlotState.Rebuilding;
                                slot.timer = template.rebuildTime;
                                hangar.ValueRW.rebuildingSlotIndex = i;

#if UNITY_EDITOR
                                if (debugEnabled && debugSettings.logSlotStateChanges)
                                {
                                    Debug.Log($"[CarrierHangar] Start rebuilding slot. carrier={carrierEntity.Index}:{carrierEntity.Version} slot={i} template={slot.templateIndex} rebuildTime={template.rebuildTime}");
                                }
#endif
                            }
                        }
                        break;

                    case CarrierSlotState.Rebuilding:
                        if (hangar.ValueRO.rebuildingSlotIndex == i)
                        {
                            slot.timer -= dt;
                            if (slot.timer <= 0f)
                            {
                                slot.timer = 0f;
                                slot.state = CarrierSlotState.Ready;
                                hangar.ValueRW.rebuildingSlotIndex = -1;

#if UNITY_EDITOR
                                if (debugEnabled && debugSettings.logSlotStateChanges)
                                    Debug.Log($"[CarrierHangar] Rebuild finished. carrier={carrierEntity.Index}:{carrierEntity.Version} slot={i} state={(int)CarrierSlotState.Ready}");
#endif
                            }
                        }
                        break;

                    case CarrierSlotState.Ready:
                        break;
                }

                slots[i] = slot;
            }

            hangar.ValueRW.activeSquadrons = activeCount;

            if (hangar.ValueRO.stance == CarrierStance.AutoLaunch)
            {
                bool carrierHasAggro = HasCarrierAggro(
                    carrierEntity,
                    ref shipAgroLookup,
                    ref shipStateLookup);

                if (!carrierHasAggro)
                {
                    // don't tick launch timer while carrier is idle
                    // so first launch after aggro waits normal interval, no instant dump of all slots
                    hangar.ValueRW.launchTimer = math.max(0.05f, hangar.ValueRO.launchInterval);
                    continue;
                }

                hangar.ValueRW.launchTimer -= dt;

                if (hangar.ValueRO.launchTimer <= 0f)
                {
                    hangar.ValueRW.launchTimer = math.max(0.05f, hangar.ValueRO.launchInterval);

                    TryLaunchOne(
                        ref ecb,
                        carrierEntity,
                        carrierPos,
                        ResolveCarrierFaction(em, carrierEntity),
                        templates,
                        slots,
                        debugEnabled,
                        debugSettings);
                }
            }
        }

        ecb.Playback(em);
        ecb.Dispose();
    }

    private static bool HasCarrierAggro(
        Entity carrierEntity,
        ref ComponentLookup<ShipAgro> shipAgroLookup,
        ref ComponentLookup<ShipStateComponent> shipStateLookup)
    {
        // carrier itself must have real aggro target
        if (shipAgroLookup.HasComponent(carrierEntity) &&
            shipAgroLookup.IsComponentEnabled(carrierEntity))
        {
            ShipAgro agro = shipAgroLookup[carrierEntity];
            if (agro.targetEntity != Entity.Null)
                return true;
        }

        // fallback for direct attack order / forced combat
        if (shipStateLookup.HasComponent(carrierEntity))
        {
            ShipStateComponent shipState = shipStateLookup[carrierEntity];
            if (shipState.currentState == ShipState.InCombat && shipState.forcedTarget != Entity.Null)
                return true;
        }

        return false;
    }

    private static void TryLaunchOne(
        ref EntityCommandBuffer ecb,
        Entity carrierEntity,
        float2 carrierPos,
        Faction faction,
        DynamicBuffer<CarrierSquadronTemplateElement> templates,
        DynamicBuffer<CarrierSquadronSlotElement> slots,
        bool debugEnabled,
        CarrierDebugSettings debugSettings)
    {
        int bestSlot = -1;
        int bestPriority = int.MinValue;

        for (int i = 0; i < slots.Length; i++)
        {
            CarrierSquadronSlotElement slot = slots[i];

            if (slot.state != CarrierSlotState.Ready || slot.squadronEntity != Entity.Null)
                continue;

            if (slot.templateIndex < 0 || slot.templateIndex >= templates.Length)
                continue;

            int priority = templates[slot.templateIndex].launchPriority;
            if (bestSlot < 0 || priority > bestPriority)
            {
                bestSlot = i;
                bestPriority = priority;
            }
        }

        if (bestSlot < 0)
            return;

        CarrierSquadronSlotElement chosenSlot = slots[bestSlot];
        CarrierSquadronTemplateElement template = templates[chosenSlot.templateIndex];

        if (template.memberPrefab == Entity.Null || template.membersPerSquadron <= 0)
        {
#if UNITY_EDITOR
            if (debugEnabled && debugSettings.logLaunch)
            {
                Debug.LogWarning($"[CarrierHangar] Cannot launch slot because template is invalid. carrier={carrierEntity.Index}:{carrierEntity.Version} slot={bestSlot} template={chosenSlot.templateIndex} prefab={template.memberPrefab.Index}:{template.memberPrefab.Version} members={template.membersPerSquadron}");
            }
#endif
            return;
        }

        float2 launchPos = carrierPos + new float2(0f, template.formationSpacing + 2f);

        Entity requestEntity = ecb.CreateEntity();

        ecb.AddComponent(requestEntity, new CreateSquadCommand
        {
            faction = faction,
            role = SquadRole.Interceptor,
            initialState = ShipState.MovingToTarget,
            initialTargetPosition = launchPos,

            memberCount = template.membersPerSquadron,
            formation = template.formation,
            spacing = template.formationSpacing,

            spawnAnchor = launchPos,

            // squadrons stay near carrier by default
            // this is not recovery, recovery only when slot.state == Returning
            anchorEntity = carrierEntity,

            defaultFireMode = FireMode.FireAtWill,
            defaultMoveMode = MoveMode.AttackMove,
            initialTactics = Tactics.Neutral,

            origin = SquadOrigin.Carrier,
            originEntity = carrierEntity,
            carrierSlotIndex = bestSlot,
            initialEndurance = template.endurance,

            targetStrikeGroupEntity = Entity.Null,
            requestTag = 0,
        });

        DynamicBuffer<CreateSquadMemberTemplate> requestTemplates =
            ecb.AddBuffer<CreateSquadMemberTemplate>(requestEntity);

        for (int i = 0; i < template.membersPerSquadron; i++)
        {
            requestTemplates.Add(new CreateSquadMemberTemplate
            {
                slotIndex = i,
                memberPrefab = template.memberPrefab,
                memberPrefabIndex = template.memberPrefabIndex,
            });
        }

        // real squad entity is created by SquadSpawnSystem
        // it must write squadEntity back to this slot
        chosenSlot.squadronEntity = Entity.Null;
        chosenSlot.state = CarrierSlotState.Launched;
        chosenSlot.timer = 0f;
        slots[bestSlot] = chosenSlot;

#if UNITY_EDITOR
        if (debugEnabled && debugSettings.logLaunch)
        {
            Debug.Log($"[CarrierHangar] Launch request created. carrier={carrierEntity.Index}:{carrierEntity.Version} slot={bestSlot} template={chosenSlot.templateIndex} members={template.membersPerSquadron} endurance={template.endurance} leash={template.leashDistance} recallHealth={template.recallAtHealthFraction} request={requestEntity.Index}:{requestEntity.Version}");
        }
#endif
    }

    private static void HandleRecovery(
        EntityManager em,
        ref EntityCommandBuffer ecb,
        Entity carrierEntity,
        int slotIndex,
        float2 carrierPos,
        CarrierHangarState hangar,
        DynamicBuffer<CarrierSquadronTemplateElement> templates,
        bool debugEnabled,
        CarrierDebugSettings debugSettings,
        ref CarrierSquadronSlotElement slot)
    {
        // squadron can escort with anchorEntity = carrier
        // absorb only when slot is Returning
        if (slot.state != CarrierSlotState.Returning)
            return;

        if (slot.squadronEntity == Entity.Null ||
            !em.Exists(slot.squadronEntity) ||
            !em.HasComponent<SquadComponent>(slot.squadronEntity) ||
            !em.HasBuffer<SquadMember>(slot.squadronEntity))
        {
            return;
        }

        SquadComponent squad = em.GetComponentData<SquadComponent>(slot.squadronEntity);
        if (squad.origin != SquadOrigin.Carrier)
            return;

        if (!AllAliveMembersInsideRecoveryRadius(
                em,
                slot.squadronEntity,
                carrierPos,
                hangar.recoveryRadius,
                out int aliveMembers,
                out Entity farthestShip,
                out float farthestDistance))
        {
            return;
        }

        float serviceTime = 0f;
        if (slot.templateIndex >= 0 && slot.templateIndex < templates.Length)
            serviceTime = templates[slot.templateIndex].serviceTime;

        Entity recoveredSquad = slot.squadronEntity;
        DestroyWholeSquad(ref ecb, em, slot.squadronEntity);

        slot.squadronEntity = Entity.Null;
        slot.state = CarrierSlotState.Servicing;
        slot.timer = serviceTime;

#if UNITY_EDITOR
        if (debugEnabled && debugSettings.logRecovery)
        {
            Debug.Log($"[CarrierHangar] Squadron recovered into carrier. carrier={carrierEntity.Index}:{carrierEntity.Version} slot={slotIndex} squad={recoveredSquad.Index}:{recoveredSquad.Version} recoveryRadius={hangar.recoveryRadius} serviceTime={serviceTime}");
        }
#endif
    }

    private static bool AllAliveMembersInsideRecoveryRadius(
        EntityManager em,
        Entity squadEntity,
        float2 carrierPos,
        float recoveryRadius,
        out int aliveMembers,
        out Entity farthestShip,
        out float farthestDistance)
    {
        aliveMembers = 0;
        farthestShip = Entity.Null;
        farthestDistance = 0f;

        if (!em.HasBuffer<SquadMember>(squadEntity))
            return false;

        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
        if (members.Length == 0)
            return false;

        float recoveryRadiusSq = recoveryRadius * recoveryRadius;
        float farthestDistanceSq = 0f;
        bool allInside = true;

        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null || !em.Exists(ship))
                continue;

            if (!em.HasComponent<LocalTransform>(ship))
                continue;

            aliveMembers++;

            float2 shipPos = em.GetComponentData<LocalTransform>(ship).Position.xy;
            float distanceSq = math.distancesq(shipPos, carrierPos);
            if (distanceSq > farthestDistanceSq)
            {
                farthestDistanceSq = distanceSq;
                farthestShip = ship;
            }

            if (distanceSq > recoveryRadiusSq)
                allInside = false;
        }

        farthestDistance = math.sqrt(farthestDistanceSq);
        return aliveMembers > 0 && allInside;
    }

    private static void DestroyWholeSquad(
        ref EntityCommandBuffer ecb,
        EntityManager em,
        Entity squadEntity)
    {
        if (squadEntity == Entity.Null || !em.Exists(squadEntity))
            return;

        if (em.HasBuffer<SquadMember>(squadEntity))
        {
            DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);

            for (int i = 0; i < members.Length; i++)
            {
                Entity ship = members[i].ship;
                if (ship != Entity.Null && em.Exists(ship))
                    ecb.DestroyEntity(ship);
            }
        }

        ecb.DestroyEntity(squadEntity);
    }

    private static CarrierDebugSettings GetDebugSettings(EntityManager em, Entity carrier)
    {
        if (carrier != Entity.Null && em.Exists(carrier) && em.HasComponent<CarrierDebugSettings>(carrier))
            return em.GetComponentData<CarrierDebugSettings>(carrier);

        return default;
    }

    private static Faction ResolveCarrierFaction(EntityManager em, Entity carrierEntity)
    {
        if (carrierEntity != Entity.Null && em.Exists(carrierEntity))
        {
            if (em.HasComponent<Unit>(carrierEntity))
                return em.GetComponentData<Unit>(carrierEntity).faction;

            if (em.HasComponent<Friendly>(carrierEntity))
                return Faction.Friendly;

            if (em.HasComponent<Enemy>(carrierEntity))
                return Faction.Enemy;
        }

        return Faction.Enemy;
    }
}
