using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// draws pattern lines for ships with FightPatternDebugMarker
// needs AiDemoLineRenderer on camera
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class AiDemoFightPatternDebugSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<FightPatternDebugMarker>();
    }

    protected override void OnUpdate()
    {
        if (!AiDemoLineRenderer.IsAvailable) return;

        EntityManager em = EntityManager;
        float elapsed = (float)SystemAPI.Time.ElapsedTime;
        ComponentLookup<ShipSquadRef> shipSquadRefLookup = SystemAPI.GetComponentLookup<ShipSquadRef>(true);

        foreach (var (marker, fight, patternState, mover, agro, ltw, entity) in
                 SystemAPI.Query<
                         RefRO<FightPatternDebugMarker>,
                         RefRO<FightLogic>,
                         RefRO<FightPatternState>,
                         RefRO<UnitMover>,
                         RefRO<ShipAgro>,
                         RefRO<LocalToWorld>>()
                     .WithEntityAccess())
        {
            if (marker.ValueRO.DrawPattern == 0) continue;

            float3 shipPos3 = ltw.ValueRO.Position;
            float z = shipPos3.z;

            float2 shipPos = new float2(shipPos3.x, shipPos3.y);
            float2 fightTarget = mover.ValueRO.fightTarget;

            if (!IsFinite(fightTarget))
            {
                continue;
            }

            float2 targetPos = ResolveTargetPosition(em, agro.ValueRO, out bool hasTarget);
            FightLogicType pattern = ResolvePattern(fight.ValueRO, patternState.ValueRO);

            Color baseColor = PatternColor(pattern);
            Color fightTargetColor = new Color(0.25f, 1f, 0.30f, 0.95f);
            Color ringColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.30f);

            Vector3 shipV3 = ToV3(shipPos, z);
            Vector3 fightTargetV3 = ToV3(fightTarget, z);

            AiDemoLineRenderer.AddLine(shipV3, fightTargetV3, fightTargetColor);
            AiDemoLineRenderer.AddCross(fightTargetV3, 0.55f, fightTargetColor);

            if (hasTarget)
            {
                Vector3 targetV3 = ToV3(targetPos, z);
                AiDemoLineRenderer.AddLine(shipV3, targetV3, baseColor);

                DrawPatternShape(
                    pattern,
                    fight.ValueRO,
                    patternState.ValueRO,
                    marker.ValueRO,
                    entity,
                    targetPos,
                    fightTarget,
                    z,
                    elapsed,
                    shipSquadRefLookup,
                    ringColor);
            }
        }
    }

    private static FightLogicType ResolvePattern(FightLogic fight, FightPatternState state)
    {
        if ((byte)state.activePattern != byte.MaxValue)
        {
            return state.activePattern;
        }

        return fight.movementType;
    }

    private static float2 ResolveTargetPosition(EntityManager em, ShipAgro agro, out bool hasTarget)
    {
        hasTarget = false;

        if (agro.targetEntity != Entity.Null && em.Exists(agro.targetEntity) && em.HasComponent<LocalToWorld>(agro.targetEntity))
        {
            LocalToWorld targetLtw = em.GetComponentData<LocalToWorld>(agro.targetEntity);
            float3 p = targetLtw.Position;
            hasTarget = true;
            return new float2(p.x, p.y);
        }

        if (IsFinite(agro.targetPosition) && math.lengthsq(agro.targetPosition) > 0.0001f)
        {
            hasTarget = true;
            return agro.targetPosition;
        }

        return default;
    }

    private static void DrawPatternShape(
        FightLogicType pattern,
        FightLogic fight,
        FightPatternState state,
        FightPatternDebugMarker marker,
        Entity entity,
        float2 targetPos,
        float2 fightTarget,
        float z,
        float elapsed,
        ComponentLookup<ShipSquadRef> shipSquadRefLookup,
        Color ringColor)
    {
        float ideal = math.max(0f, fight.idealDistance);
        Vector3 targetV3 = ToV3(targetPos, z);

        switch (pattern)
        {
            case FightLogicType.HoldDistance:
            case FightLogicType.CloseAndHold:
            case FightLogicType.Orbit:
            case FightLogicType.Dogfight:
                if (ideal > 0.01f)
                {
                    AiDemoLineRenderer.AddRing(targetV3, ideal, ringColor, 64);
                }

                AiDemoLineRenderer.AddLine(targetV3, ToV3(fightTarget, z), ringColor);
                break;

            case FightLogicType.Swarm:
                if (ideal > 0.01f)
                {
                    AiDemoLineRenderer.AddRing(targetV3, ideal, ringColor, 64);
                    DrawSwarmSectors(marker, fight, state, entity, targetPos, fightTarget, z, ideal, elapsed, shipSquadRefLookup, ringColor);
                }

                AiDemoLineRenderer.AddLine(targetV3, ToV3(fightTarget, z), ringColor);
                break;

            case FightLogicType.AttackRun:
            case FightLogicType.InterceptorPass:
                if (ideal > 0.01f)
                {
                    AiDemoLineRenderer.AddRing(targetV3, ideal * math.max(0.01f, fight.attackRunFireRange), ringColor, 48);
                    AiDemoLineRenderer.AddRing(targetV3, ideal, new Color(ringColor.r, ringColor.g, ringColor.b, 0.18f), 64);
                }

                break;

            case FightLogicType.MissileAttackRun:
                if (ideal > 0.01f)
                {
                    AiDemoLineRenderer.AddRing(targetV3, ideal * math.max(0.01f, fight.missileLaunchDistance), ringColor, 48);
                    AiDemoLineRenderer.AddRing(targetV3, ideal * math.max(0.01f, fight.missileRetreatDistance), new Color(ringColor.r, ringColor.g, ringColor.b, 0.18f), 64);
                }

                break;

            case FightLogicType.Strafe:
                if (ideal > 0.01f)
                {
                    AiDemoLineRenderer.AddRing(targetV3, ideal, ringColor, 64);
                    AiDemoLineRenderer.AddRing(targetV3, ideal * math.max(0.01f, fight.strafeMinDistance), new Color(ringColor.r, ringColor.g, ringColor.b, 0.18f), 48);
                }

                AiDemoLineRenderer.AddLine(targetV3, ToV3(fightTarget, z), ringColor);
                break;
        }
    }

    private static void DrawSwarmSectors(
        FightPatternDebugMarker marker,
        FightLogic fight,
        FightPatternState state,
        Entity entity,
        float2 targetPos,
        float2 fightTarget,
        float z,
        float ideal,
        float elapsed,
        ComponentLookup<ShipSquadRef> shipSquadRefLookup,
        Color baseColor)
    {
        if (marker.DrawSwarmSectors == 0) return;

        int slotsPerCircle = math.max(2, fight.swarmSlotsPerCircle);
        int slot = ResolveSwarmSlot(entity, shipSquadRefLookup);
        int slotIndex = slot % slotsPerCircle;
        if (slotIndex < 0) { slotIndex += slotsPerCircle; }

        float sectorAngle = (math.PI * 2f) / slotsPerCircle;
        float baseAngle = slotIndex * sectorAngle;
        float globalRot = math.radians(fight.swarmRotationDegPerSec) * elapsed;

        if (fight.orbitDirection < 0f)
        {
            globalRot = -globalRot;
        }

        Vector3 center = ToV3(targetPos, z);
        Color faint = new Color(baseColor.r, baseColor.g, baseColor.b, 0.18f);
        Color strong = new Color(baseColor.r, baseColor.g, baseColor.b, 0.75f);

        if (marker.DrawAllSwarmSectors != 0)
        {
            float boundaryOffset = globalRot - sectorAngle * 0.5f;

            for (int i = 0; i < slotsPerCircle; i++)
            {
                float a = boundaryOffset + i * sectorAngle;
                float2 dir = new float2(math.cos(a), math.sin(a));
                AiDemoLineRenderer.AddLine(center, ToV3(targetPos + dir * ideal, z), faint);
            }
        }

        float midAngle = baseAngle + globalRot;
        float jitter = math.sin(elapsed * 0.6f + state.driftPhase) * 0.08f;
        float visualMidAngle = midAngle + jitter;
        float2 midDir = new float2(math.cos(visualMidAngle), math.sin(visualMidAngle));

        AiDemoLineRenderer.AddCone(
            center,
            ToV3(midDir, 0f),
            ideal,
            sectorAngle * 0.5f * 57.2957795f,
            strong,
            8);

        if (marker.DrawSwarmSlotPoint != 0)
        {
            Vector3 slotPoint = ToV3(fightTarget, z);
            AiDemoLineRenderer.AddLine(center, slotPoint, strong);
            AiDemoLineRenderer.AddCross(slotPoint, 0.85f, strong);
        }
    }

    private static int ResolveSwarmSlot(Entity entity, ComponentLookup<ShipSquadRef> shipSquadRefLookup)
    {
        if (shipSquadRefLookup.HasComponent(entity))
        {
            return shipSquadRefLookup[entity].formationSlotIndex;
        }

        return entity.Index;
    }

    private static Color PatternColor(FightLogicType pattern)
    {
        switch (pattern)
        {
            case FightLogicType.HoldDistance: return new Color(0.20f, 0.85f, 1.00f, 0.85f);
            case FightLogicType.CloseAndHold: return new Color(0.35f, 1.00f, 0.90f, 0.85f);
            case FightLogicType.Orbit: return new Color(0.45f, 0.75f, 1.00f, 0.85f);
            case FightLogicType.InterceptorPass: return new Color(1.00f, 0.70f, 0.25f, 0.85f);
            case FightLogicType.AttackRun: return new Color(1.00f, 0.35f, 0.20f, 0.85f);
            case FightLogicType.MissileAttackRun: return new Color(1.00f, 0.90f, 0.20f, 0.85f);
            case FightLogicType.Dogfight: return new Color(0.80f, 0.45f, 1.00f, 0.85f);
            case FightLogicType.Swarm: return new Color(0.25f, 1.00f, 0.55f, 0.85f);
            default: return Color.white;
        }
    }

    private static Vector3 ToV3(float2 p, float z)
    {
        return new Vector3(p.x, p.y, z);
    }

    private static bool IsFinite(float2 v)
    {
        return math.isfinite(v.x) && math.isfinite(v.y);
    }
}
