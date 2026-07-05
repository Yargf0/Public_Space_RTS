using Unity.Entities;
using Unity.Mathematics;

public struct BulletActive : IComponentData, IEnableableComponent
{
}

public struct BulletPoolSettings : IComponentData
{
    public int prewarmPerPrefab;
    public int growChunk;
    public int hardCapPerPrefab;
    public bool allowRuntimeExpand;
    public bool logRuntimeExpand;

    public bool keepBootstrapEnabledForRuntimePrefabs;
    public float runtimeScanInterval;
    public int maxPoolsCreatedPerFrame;
    public int maxPrewarmPerFrame;

    public float3 inactivePosition;
}
