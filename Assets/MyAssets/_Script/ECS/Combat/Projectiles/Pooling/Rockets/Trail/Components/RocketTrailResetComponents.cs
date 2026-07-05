using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct RocketTrailResetRequest : IComponentData, IEnableableComponent
{
    // 0 = no work, 1 = first reset pass, 2 = delayed trail enable pass.
    public byte pending;

    // spawn path: show visuals right away, trail can wait
    public byte showAfterClear;
    public byte emitAfterClear;
    public byte activateAfterClear;

    // release path: clear trail first, then move to pool while hidden
    public byte moveAfterClear;
    public float3 movePosition;

    // TrailRenderer must not enable in same frame as ECS teleport
    // one frame delay, then Clear() again and enable emitting
    public byte delayFramesBeforeEmit;
}

public sealed class RocketTrailRendererReference : IComponentData
{
    public TrailRenderer[] trails;
    public Renderer[] renderers;
    public ParticleSystem[] particles;
}
