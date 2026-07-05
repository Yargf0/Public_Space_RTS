using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(RocketFireRequestExecutionSystem))]
[UpdateBefore(typeof(RocketMoverSystem))]
public partial class RocketTrailResetBeforeRocketMoverSystem : SystemBase
{
    private EntityQuery query;

    protected override void OnCreate()
    {
        query = GetEntityQuery(ComponentType.ReadWrite<RocketTrailResetRequest>());
    }

    protected override void OnUpdate()
    {
        RocketTrailResetShared.Process(EntityManager, query);
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(RocketMoverSystem))]
public partial class RocketTrailResetAfterRocketMoverSystem : SystemBase
{
    private EntityQuery query;

    protected override void OnCreate()
    {
        query = GetEntityQuery(ComponentType.ReadWrite<RocketTrailResetRequest>());
    }

    protected override void OnUpdate()
    {
        RocketTrailResetShared.Process(EntityManager, query);
    }
}

public static class RocketTrailResetShared
{
    public static void Process(EntityManager entityManager, EntityQuery query)
    {
        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
            {
                Entity entity = entities[entityIndex];
                RocketTrailResetRequest request = entityManager.GetComponentData<RocketTrailResetRequest>(entity);
                if (request.pending == 0)
                {
                    continue;
                }

                RocketTrailRendererReference reference = null;
                if (entityManager.HasComponent<RocketTrailRendererReference>(entity))
                {
                    reference = entityManager.GetComponentObject<RocketTrailRendererReference>(entity);
                }

                if (request.pending == 2)
                {
                    ProcessDelayedEnable(entityManager, entity, reference, ref request);
                    continue;
                }

                // first pass: disable trail emission before any transform work
                SetTrailState(reference, false);
                StopParticles(reference);
                ClearTrails(reference);

                if (request.showAfterClear == 0)
                {
                    SetVisualState(reference, false);
                }

                if (request.moveAfterClear != 0)
                {
                    // move only while visuals and trail are off
                    MoveEntity(entityManager, entity, request.movePosition);
                    ClearTrails(reference);
                }

                if (request.showAfterClear != 0)
                {
                    // spawn path: body can show now, trail waits one frame
                    SetVisualState(reference, true);
                }

                if (request.activateAfterClear != 0 && entityManager.HasComponent<RocketActive>(entity))
                {
                    entityManager.SetComponentEnabled<RocketActive>(entity, true);
                }

                if (request.emitAfterClear != 0)
                {
                    request.pending = 2;
                    request.moveAfterClear = 0;
                    request.movePosition = float3.zero;
                    request.showAfterClear = 0;
                    request.activateAfterClear = 0;
                    if (request.delayFramesBeforeEmit == 0)
                    {
                        request.delayFramesBeforeEmit = 1;
                    }
                    entityManager.SetComponentData(entity, request);
                    continue;
                }

                ClearRequest(entityManager, entity, ref request);
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    private static void ProcessDelayedEnable(
        EntityManager entityManager,
        Entity entity,
        RocketTrailRendererReference reference,
        ref RocketTrailResetRequest request)
    {
        if (request.delayFramesBeforeEmit > 0)
        {
            request.delayFramesBeforeEmit--;
            entityManager.SetComponentData(entity, request);
            return;
        }

        // clear once more after transform settled
        ClearTrails(reference);
        SetTrailState(reference, true);
        RestartParticles(reference);
        ClearRequest(entityManager, entity, ref request);
    }

    private static void MoveEntity(EntityManager entityManager, Entity entity, float3 position)
    {
        if (!entityManager.HasComponent<LocalTransform>(entity))
        {
            return;
        }

        LocalTransform transform = entityManager.GetComponentData<LocalTransform>(entity);
        transform.Position = position;
        entityManager.SetComponentData(entity, transform);
    }

    private static void ClearRequest(EntityManager entityManager, Entity entity, ref RocketTrailResetRequest request)
    {
        request.pending = 0;
        request.showAfterClear = 0;
        request.emitAfterClear = 0;
        request.activateAfterClear = 0;
        request.moveAfterClear = 0;
        request.movePosition = float3.zero;
        request.delayFramesBeforeEmit = 0;
        entityManager.SetComponentData(entity, request);
        if (entityManager.HasComponent<RocketTrailResetRequest>(entity))
        {
            entityManager.SetComponentEnabled<RocketTrailResetRequest>(entity, false);
        }
    }

    private static void SetVisualState(RocketTrailRendererReference reference, bool visible)
    {
        if (reference == null || reference.renderers == null)
        {
            return;
        }

        for (int i = 0; i < reference.renderers.Length; i++)
        {
            Renderer renderer = reference.renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = visible;
        }
    }

    private static void SetTrailState(RocketTrailRendererReference reference, bool emitting)
    {
        if (reference == null || reference.trails == null)
        {
            return;
        }

        for (int i = 0; i < reference.trails.Length; i++)
        {
            TrailRenderer trail = reference.trails[i];
            if (trail == null)
            {
                continue;
            }

            // keep TrailRenderer enabled, toggling it around teleport brings old positions back
            // emitting=false + Clear() is the safe state
            trail.enabled = true;
            trail.emitting = emitting;
            if (!emitting)
            {
                trail.Clear();
            }
        }
    }

    private static void ClearTrails(RocketTrailRendererReference reference)
    {
        if (reference == null || reference.trails == null)
        {
            return;
        }

        for (int i = 0; i < reference.trails.Length; i++)
        {
            TrailRenderer trail = reference.trails[i];
            if (trail == null)
            {
                continue;
            }

            trail.Clear();
        }
    }

    private static void StopParticles(RocketTrailRendererReference reference)
    {
        if (reference == null || reference.particles == null)
        {
            return;
        }

        for (int i = 0; i < reference.particles.Length; i++)
        {
            ParticleSystem particle = reference.particles[i];
            if (particle == null)
            {
                continue;
            }

            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Clear(true);
        }
    }

    private static void RestartParticles(RocketTrailRendererReference reference)
    {
        if (reference == null || reference.particles == null)
        {
            return;
        }

        for (int i = 0; i < reference.particles.Length; i++)
        {
            ParticleSystem particle = reference.particles[i];
            if (particle == null)
            {
                continue;
            }

            particle.Clear(true);
            particle.Play(true);
        }
    }
}
