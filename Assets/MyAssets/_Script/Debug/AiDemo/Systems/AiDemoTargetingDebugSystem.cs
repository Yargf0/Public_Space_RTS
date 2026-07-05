using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// draws target links and combat range rings
// enabled by AiDemoTargetingDebugSettings


[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class AiDemoTargetingDebugSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<AiDemoTargetingDebugSettings>();
    }

    protected override void OnUpdate()
    {
        AiDemoTargetingDebugSettings settings = SystemAPI.GetSingleton<AiDemoTargetingDebugSettings>();
        if (settings.enabled == 0) return;
        if (!AiDemoLineRenderer.IsAvailable) return;

        EntityManager em = EntityManager;

        // faction colors
        Color friendlyColor = new Color(0.30f, 0.75f, 1.00f, 0.85f);
        Color enemyColor = new Color(1.00f, 0.40f, 0.30f, 0.85f);
        Color idealColor = new Color(1.00f, 1.00f, 1.00f, 0.25f);

        // shooter -> target lines
        if (settings.drawTargetLines != 0)
        {
            foreach (var (target, ltw, unit) in
                     SystemAPI.Query<RefRO<Target>, RefRO<LocalToWorld>, RefRO<Unit>>())
            {
                Entity tgtEntity = target.ValueRO.targetEntity;
                if (tgtEntity == Entity.Null) continue;
                if (!em.Exists(tgtEntity)) continue;
                if (!em.HasComponent<LocalToWorld>(tgtEntity)) continue;

                LocalToWorld tgtLtw = em.GetComponentData<LocalToWorld>(tgtEntity);

                Color c = unit.ValueRO.faction == Faction.Friendly ? friendlyColor : enemyColor;

                AiDemoLineRenderer.AddLine(ltw.ValueRO.Position, tgtLtw.Position, c);
                AiDemoLineRenderer.AddCross(tgtLtw.Position, 0.6f, c);
            }
        }

        // ideal combat distance rings
        if (settings.drawIdealDistance != 0)
        {
            foreach (var (fight, ltw) in
                     SystemAPI.Query<RefRO<FightLogic>, RefRO<LocalToWorld>>())
            {
                float r = fight.ValueRO.idealDistance;
                if (r <= 0.01f) continue;
                AiDemoLineRenderer.AddRing(ltw.ValueRO.Position, r, idealColor, 48);
            }
        }
    }
}
