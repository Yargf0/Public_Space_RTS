using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// draws searchlight cones in game view
// enabled by AiDemoSearchlightDebugSettings

public struct AiDemoSearchlightDebugSettings : IComponentData
{
    public byte enabled;
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class AiDemoSearchlightConeDebugSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<AiDemoSearchlightDebugSettings>();
    }

    protected override void OnUpdate()
    {
        AiDemoSearchlightDebugSettings settings = SystemAPI.GetSingleton<AiDemoSearchlightDebugSettings>();
        if (settings.enabled == 0) return;
        if (!AiDemoLineRenderer.IsAvailable) return;

        Color friendlySensorColor = new Color(0.35f, 0.80f, 1.00f, 0.55f);
        Color enemySensorColor = new Color(1.00f, 0.45f, 0.30f, 0.55f);

        foreach (var (light, ltw) in
                 SystemAPI.Query<RefRO<Searchlight>, RefRO<LocalToWorld>>())
        {
            Searchlight sl = light.ValueRO;

            // sensor forward is local X (2D)
            Vector3 fwd = ltw.ValueRO.Right;

            // cone color by faction
            Color color = sl.observerFaction == Faction.Friendly
                ? friendlySensorColor
                : enemySensorColor;

            // full cone is drawn as ring
            if (sl.coneAngle >= 359.5f)
            {
                AiDemoLineRenderer.AddRing(ltw.ValueRO.Position, sl.range, color, 48);
            }
            else
            {
                float halfAngle = sl.coneAngle * 0.5f;
                AiDemoLineRenderer.AddCone(ltw.ValueRO.Position, fwd, sl.range, halfAngle, color, 24);
            }
        }
    }
}
