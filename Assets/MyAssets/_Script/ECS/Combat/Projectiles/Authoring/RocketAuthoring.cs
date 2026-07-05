using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RocketAuthoring : MonoBehaviour
{
    public float maxSpeed;
    public float acceleration;
    public float rotationSpeed;

    class Baker : Baker<RocketAuthoring>
    {
        public override void Bake(RocketAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RocketActive>(entity);

            AddComponent(entity, new Rocket
            {
                currentSpeed = 0f,
                maxSpeed = authoring.maxSpeed,
                acceleration = authoring.acceleration,
                rotationSpeed = authoring.rotationSpeed,
                timer = 0f,
                ownerFaction = Faction.Friendly,
                useFogOfWar = false,
                phase = (byte)RocketFlightPhase.Locked,
                lkpFreeFlightTimer = 0f,
            });

            AddComponent(entity, new LastKnownTarget
            {
                target = Entity.Null,
                lastKnownPosition = default,
                searchTimer = 0f,
            });
            SetComponentEnabled<LastKnownTarget>(entity, false);

            // archetype must be final before firing
            // pooled hot path does SetComponent only
            AddComponent(entity, new WeaponPayloadRuntime());

            // baked so prewarm can use SetComponentData
            AddComponent(entity, new ProjectilePoolMember
            {
                poolEntity = Entity.Null,
                prefabEntity = Entity.Null,
                kind = ProjectilePoolKind.Rocket,
                inPool = 0,
            });

            // reset request is enableable so trail reset system don't scan all rockets every frame
            AddComponent(entity, new RocketTrailResetRequest
            {
                pending = 0,
                showAfterClear = 0,
                emitAfterClear = 0,
                activateAfterClear = 0,
                moveAfterClear = 0,
                movePosition = float3.zero,
                delayFramesBeforeEmit = 0,
            });
            SetComponentEnabled<RocketTrailResetRequest>(entity, false);

            TrailRenderer[] trails = authoring.GetComponentsInChildren<TrailRenderer>(includeInactive: true);
            Renderer[] allRenderers = authoring.GetComponentsInChildren<Renderer>(includeInactive: true);
            Renderer[] renderers = FilterNonTrailRenderers(allRenderers);
            ParticleSystem[] particles = authoring.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            if ((trails != null && trails.Length > 0) ||
                (renderers != null && renderers.Length > 0) ||
                (particles != null && particles.Length > 0))
            {
                AddComponentObject(entity, new RocketTrailRendererReference
                {
                    trails = trails,
                    renderers = renderers,
                    particles = particles,
                });
            }
        }
    }

    private static Renderer[] FilterNonTrailRenderers(Renderer[] allRenderers)
    {
        if (allRenderers == null || allRenderers.Length == 0)
        {
            return allRenderers;
        }

        int count = 0;
        for (int i = 0; i < allRenderers.Length; i++)
        {
            if (allRenderers[i] != null && !(allRenderers[i] is TrailRenderer))
            {
                count++;
            }
        }

        Renderer[] result = new Renderer[count];
        int writeIndex = 0;
        for (int i = 0; i < allRenderers.Length; i++)
        {
            if (allRenderers[i] != null && !(allRenderers[i] is TrailRenderer))
            {
                result[writeIndex++] = allRenderers[i];
            }
        }

        return result;
    }
}

public enum RocketFlightPhase : byte
{
    Locked = 0,
    Lkp = 1,
    FreeDelay = 2,
    Free = 3,
}

public struct RocketActive : IComponentData, IEnableableComponent
{
}

public struct Rocket : IComponentData
{
    public float maxSpeed;
    public float currentSpeed;
    public float acceleration;
    public float rotationSpeed;
    public float timer;

    public Faction ownerFaction;
    public bool useFogOfWar;
    public byte phase;
    public float lkpFreeFlightTimer;
}

// start phase for missile volley. rocket flies on scatterDirection, then normal homing
public struct RocketLaunchScatter : IComponentData
{
    public float timer;
    public float distanceRemaining;
    public float2 scatterDirection;
}
