using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StrikeGroupOrderExecutionSystem))]
[UpdateBefore(typeof(SquadDefaultsSystem))]
public partial struct CarrierRecallSystem : ISystem
{
    private const int RecallReasonUnknown = 0;
    private const int RecallReasonEndurance = 1;
    private const int RecallReasonLeash = 2;
    private const int RecallReasonLowHealth = 4;
    private const int RecallReasonRecallAll = 8;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        EntityManager em = state.EntityManager;
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        ComponentLookup<ShipStateComponent> shipStateLookup =
            SystemAPI.GetComponentLookup<ShipStateComponent>(false);

        ComponentLookup<UnitMover> moverLookup =
            SystemAPI.GetComponentLookup<UnitMover>(false);

        ComponentLookup<ShipPriorityHint> hintLookup =
            SystemAPI.GetComponentLookup<ShipPriorityHint>(false);

        foreach ((RefRW<SquadComponent> squad, DynamicBuffer<SquadMember> members, Entity squadEntity)
            in SystemAPI.Query<RefRW<SquadComponent>, DynamicBuffer<SquadMember>>()
                .WithAll<SquadronTag>()
                .WithEntityAccess())
        {
            if (squad.ValueRO.origin != SquadOrigin.Carrier)
                continue;

            Entity carrier = squad.ValueRO.originEntity;
            if (carrier == Entity.Null || !em.Exists(carrier))
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[CarrierRecall] Destroy carrier squad because carrier is missing. squad={squadEntity.Index}:{squadEntity.Version} carrier={carrier.Index}:{carrier.Version} members={members.Length}");
#endif
                DestroyMembers(ref ecb, em, members);
                ecb.DestroyEntity(squadEntity);
                continue;
            }

            CarrierDebugSettings debugSettings = GetDebugSettings(em, carrier);
            bool debugEnabled = debugSettings.enableLogs;

            bool hasTransform = em.HasComponent<LocalTransform>(carrier);
            bool hasHangar = em.HasComponent<CarrierHangarState>(carrier);
            bool hasSlots = em.HasBuffer<CarrierSquadronSlotElement>(carrier);
            bool hasTemplates = em.HasBuffer<CarrierSquadronTemplateElement>(carrier);

            if (!hasTransform || !hasHangar || !hasSlots || !hasTemplates)
            {
#if UNITY_EDITOR
                if (debugEnabled)
                {
                    Debug.LogWarning($"[CarrierRecall] Skip carrier squad because carrier setup is incomplete. carrier={carrier.Index}:{carrier.Version} squad={squadEntity.Index}:{squadEntity.Version} hasTransform={hasTransform} hasHangar={hasHangar} hasSlots={hasSlots} hasTemplates={hasTemplates}");
                }
#endif
                continue;
            }

            CarrierHangarState hangar = em.GetComponentData<CarrierHangarState>(carrier);
            DynamicBuffer<CarrierSquadronSlotElement> slots = em.GetBuffer<CarrierSquadronSlotElement>(carrier);
            DynamicBuffer<CarrierSquadronTemplateElement> templates = em.GetBuffer<CarrierSquadronTemplateElement>(carrier);

            int slotIndex = squad.ValueRO.carrierSlotIndex;
            if (slotIndex < 0 || slotIndex >= slots.Length)
            {
#if UNITY_EDITOR
                if (debugEnabled)
                    Debug.LogWarning($"[CarrierRecall] Skip carrier squad because slot index is invalid. carrier={carrier.Index}:{carrier.Version} squad={squadEntity.Index}:{squadEntity.Version} slotIndex={slotIndex} slots={slots.Length}");
#endif
                continue;
            }

            CarrierSquadronSlotElement slot = slots[slotIndex];
            if (slot.templateIndex < 0 || slot.templateIndex >= templates.Length)
            {
#if UNITY_EDITOR
                if (debugEnabled)
                    Debug.LogWarning($"[CarrierRecall] Skip carrier squad because template index is invalid. carrier={carrier.Index}:{carrier.Version} squad={squadEntity.Index}:{squadEntity.Version} slot={slotIndex} templateIndex={slot.templateIndex} templates={templates.Length}");
#endif
                continue;
            }

            CarrierSquadronTemplateElement template = templates[slot.templateIndex];
            float2 carrierPos = em.GetComponentData<LocalTransform>(carrier).Position.xy;

            if (slot.state == CarrierSlotState.Launched)
            {
                squad.ValueRW.enduranceRemaining -= dt;

                // squadrons escort carrier by default
                // if member is in combat, move anchor to combat point so SquadDefaultsSystem don't pull it back
                if (TryGetCombatAnchor(em, members, out float2 combatAnchor))
                {
                    squad.ValueRW.anchorEntity = Entity.Null;
                    squad.ValueRW.anchorPosition = combatAnchor;
                    squad.ValueRW.defaultFireMode = FireMode.FireAtWill;
                    squad.ValueRW.defaultMoveMode = MoveMode.AttackMove;
                }
                else
                {
                    squad.ValueRW.anchorEntity = carrier;
                    squad.ValueRW.anchorPosition = carrierPos;
                    squad.ValueRW.defaultFireMode = FireMode.FireAtWill;
                    squad.ValueRW.defaultMoveMode = MoveMode.AttackMove;
                    squad.ValueRW.priorityTarget = Entity.Null;
                }
            }

            bool wasAlreadyReturning = slot.state == CarrierSlotState.Returning;
            bool enduranceExpired = squad.ValueRO.enduranceRemaining <= 0f;

            bool foundFarthestShip = TryGetFarthestAliveMemberDistance(
                em,
                members,
                carrierPos,
                out float farthestDistance,
                out Entity farthestShip);

            bool leashExceeded = foundFarthestShip &&
                template.leashDistance > 0f &&
                farthestDistance > template.leashDistance;

            float healthFraction = squad.ValueRO.maxMembers > 0
                ? (float)squad.ValueRO.aliveCount / squad.ValueRO.maxMembers
                : 0f;

            bool lowHealth = healthFraction <= template.recallAtHealthFraction;
            bool recallAll = hangar.stance == CarrierStance.RecallAll;

            int reasonFlags = RecallReasonUnknown;
            if (enduranceExpired)
                reasonFlags |= RecallReasonEndurance;
            if (leashExceeded)
                reasonFlags |= RecallReasonLeash;
            if (lowHealth)
                reasonFlags |= RecallReasonLowHealth;
            if (recallAll)
                reasonFlags |= RecallReasonRecallAll;

            bool needsRecall = wasAlreadyReturning || reasonFlags != RecallReasonUnknown;
            if (!needsRecall)
                continue;

            bool startsReturningNow = slot.state != CarrierSlotState.Returning;
#if UNITY_EDITOR
            if (debugEnabled && debugSettings.logRecallReasons && startsReturningNow)
            {
                Debug.Log($"[CarrierRecall] START RETURN carrier={carrier.Index}:{carrier.Version} squad={squadEntity.Index}:{squadEntity.Version} slot={slotIndex} template={slot.templateIndex} reasonFlags={reasonFlags} enduranceExpired={enduranceExpired} leashExceeded={leashExceeded} lowHealth={lowHealth} recallAll={recallAll} enduranceRemaining={squad.ValueRO.enduranceRemaining} healthFraction={healthFraction} alive={squad.ValueRO.aliveCount} max={squad.ValueRO.maxMembers} recallAtHealthFraction={template.recallAtHealthFraction} farthestShip={farthestShip.Index}:{farthestShip.Version} farthestDistance={farthestDistance} leashDistance={template.leashDistance} stance={(int)hangar.stance}");
            }
#endif

            // Returning is hard override, drones stop fight and fly back
            squad.ValueRW.priorityTarget = Entity.Null;
            squad.ValueRW.anchorEntity = carrier;
            squad.ValueRW.anchorPosition = carrierPos;
            squad.ValueRW.defaultFireMode = FireMode.HoldFire;
            squad.ValueRW.defaultMoveMode = MoveMode.HoldPosition;

            slot.state = CarrierSlotState.Returning;
            slots[slotIndex] = slot;

            ForceMembersToReturn(
                shipStateLookup,
                moverLookup,
                hintLookup,
                members,
                carrierPos);
        }

        ecb.Playback(em);
        ecb.Dispose();
    }

    private static CarrierDebugSettings GetDebugSettings(EntityManager em, Entity carrier)
    {
        if (carrier != Entity.Null && em.Exists(carrier) && em.HasComponent<CarrierDebugSettings>(carrier))
            return em.GetComponentData<CarrierDebugSettings>(carrier);

        return default;
    }

    private static bool TryGetCombatAnchor(
        EntityManager em,
        DynamicBuffer<SquadMember> members,
        out float2 combatAnchor)
    {
        combatAnchor = default;

        float2 sum = default;
        int count = 0;

        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null || !em.Exists(ship))
                continue;

            if (!em.HasComponent<ShipStateComponent>(ship))
                continue;

            ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(ship);
            if (shipState.currentState != ShipState.InCombat)
                continue;

            if (em.HasComponent<UnitMover>(ship))
            {
                UnitMover mover = em.GetComponentData<UnitMover>(ship);
                sum += mover.fightTarget;
                count++;
            }
            else if (em.HasComponent<LocalTransform>(ship))
            {
                sum += em.GetComponentData<LocalTransform>(ship).Position.xy;
                count++;
            }
        }

        if (count <= 0)
            return false;

        combatAnchor = sum / count;
        return true;
    }

    private static bool TryGetFarthestAliveMemberDistance(
        EntityManager em,
        DynamicBuffer<SquadMember> members,
        float2 center,
        out float farthestDistance,
        out Entity farthestShip)
    {
        farthestDistance = 0f;
        farthestShip = Entity.Null;

        float farthestDistanceSq = 0f;
        bool found = false;

        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null || !em.Exists(ship))
                continue;

            if (!em.HasComponent<LocalTransform>(ship))
                continue;

            float2 shipPos = em.GetComponentData<LocalTransform>(ship).Position.xy;
            float distanceSq = math.distancesq(shipPos, center);
            if (!found || distanceSq > farthestDistanceSq)
            {
                found = true;
                farthestDistanceSq = distanceSq;
                farthestShip = ship;
            }
        }

        if (!found)
            return false;

        farthestDistance = math.sqrt(farthestDistanceSq);
        return true;
    }

    private static void ForceMembersToReturn(
        ComponentLookup<ShipStateComponent> shipStateLookup,
        ComponentLookup<UnitMover> moverLookup,
        ComponentLookup<ShipPriorityHint> hintLookup,
        DynamicBuffer<SquadMember> members,
        float2 carrierPos)
    {
        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship == Entity.Null)
                continue;

            if (moverLookup.HasComponent(ship))
            {
                UnitMover mover = moverLookup[ship];
                mover.targetPos = carrierPos;
                mover.fightTarget = carrierPos;
                moverLookup[ship] = mover;
            }

            if (shipStateLookup.HasComponent(ship))
            {
                ShipStateComponent shipState = shipStateLookup[ship];

                shipState.mode = FireMode.HoldFire;
                shipState.moveMode = MoveMode.HoldPosition;
                shipState.forcedTarget = Entity.Null;

                if (shipState.currentState != ShipState.MovingToTarget)
                {
                    shipState.previousState = shipState.currentState;
                    shipState.currentState = ShipState.MovingToTarget;
                }

                shipStateLookup[ship] = shipState;
            }

            if (hintLookup.HasComponent(ship))
            {
                ShipPriorityHint hint = hintLookup[ship];
                hint.target = Entity.Null;
                hintLookup[ship] = hint;
            }
        }
    }

    private static void DestroyMembers(
        ref EntityCommandBuffer ecb,
        EntityManager em,
        DynamicBuffer<SquadMember> members)
    {
        for (int i = 0; i < members.Length; i++)
        {
            Entity ship = members[i].ship;
            if (ship != Entity.Null && em.Exists(ship))
                ecb.DestroyEntity(ship);
        }
    }
}
