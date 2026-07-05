using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RocketPoolSettingsAuthoring : MonoBehaviour
{
    [Header("Rocket Pool")]
    [Min(0)] public int prewarmPerPrefab = 128;
    [Min(1)] public int growChunk = 32;
    [Min(1)] public int hardCapPerPrefab = 1024;
    public bool allowRuntimeExpand = true;
    public bool logRuntimeExpand = false;

    [Header("Bootstrap")]
    [Tooltip("Off = ProjectilePoolBootstrapSystem runs once and disables itself. Turn on only if rocket prefabs can appear after the first bootstrap pass.")]
    public bool keepBootstrapEnabledForRuntimePrefabs = false;
    [Min(0.05f)] public float runtimeScanInterval = 1f;
    [Min(1)] public int maxPoolsCreatedPerFrame = 2;
    [Tooltip("Limits how many pooled rocket entities are instantiated per frame during prewarm. Lower value = smaller bootstrap spikes, longer warmup.")]
    [Min(1)] public int maxPrewarmPerFrame = 256;

    [Tooltip("Fallback inactive position. Pooled rockets with trail reset are hidden instead of being teleported here on release.")]
    public Vector3 inactivePosition = new Vector3(999999f, 999999f, -9999f);

    private class Baker : Baker<RocketPoolSettingsAuthoring>
    {
        public override void Bake(RocketPoolSettingsAuthoring authoring)
        {
            int prewarm = math.max(0, authoring.prewarmPerPrefab);
            int hardCap = math.max(1, authoring.hardCapPerPrefab);
            hardCap = math.max(hardCap, prewarm);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new RocketPoolSettings
            {
                prewarmPerPrefab = prewarm,
                growChunk = math.max(1, authoring.growChunk),
                hardCapPerPrefab = hardCap,
                allowRuntimeExpand = authoring.allowRuntimeExpand,
                logRuntimeExpand = authoring.logRuntimeExpand,
                keepBootstrapEnabledForRuntimePrefabs = authoring.keepBootstrapEnabledForRuntimePrefabs,
                runtimeScanInterval = math.max(0.05f, authoring.runtimeScanInterval),
                maxPoolsCreatedPerFrame = math.max(1, authoring.maxPoolsCreatedPerFrame),
                maxPrewarmPerFrame = math.max(1, authoring.maxPrewarmPerFrame),
                inactivePosition = new float3(authoring.inactivePosition.x, authoring.inactivePosition.y, authoring.inactivePosition.z),
            });
        }
    }
}
