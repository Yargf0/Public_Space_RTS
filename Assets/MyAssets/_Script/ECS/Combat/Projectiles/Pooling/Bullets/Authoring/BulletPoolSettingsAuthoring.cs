using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class BulletPoolSettingsAuthoring : MonoBehaviour
{
    [Header("Bullet Pool")]
    [Min(0)] public int prewarmPerPrefab = 512;
    [Min(1)] public int growChunk = 64;
    [Min(1)] public int hardCapPerPrefab = 4096;
    public bool allowRuntimeExpand = true;
    public bool logRuntimeExpand = false;

    [Header("Bootstrap")]
    [Tooltip("Off = ProjectilePoolBootstrapSystem discovers currently known bullet prefabs, finishes prewarm, then disables itself. Turn on only if bullet prefabs can appear after the first bootstrap pass.")]
    public bool keepBootstrapEnabledForRuntimePrefabs = false;
    [Min(0.05f)] public float runtimeScanInterval = 1f;
    [Min(1)] public int maxPoolsCreatedPerFrame = 2;
    [Tooltip("Limits how many pooled bullet entities are instantiated per frame during prewarm. Lower value = smaller bootstrap spikes, longer warmup.")]
    [Min(1)] public int maxPrewarmPerFrame = 256;

    [Tooltip("Inactive pooled bullets are moved here so renderers are culled without adding/removing Disabled.")]
    public Vector3 inactivePosition = new Vector3(999999f, 999999f, -9999f);

    private class Baker : Baker<BulletPoolSettingsAuthoring>
    {
        public override void Bake(BulletPoolSettingsAuthoring authoring)
        {
            int prewarm = math.max(0, authoring.prewarmPerPrefab);
            int hardCap = math.max(1, authoring.hardCapPerPrefab);
            hardCap = math.max(hardCap, prewarm);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new BulletPoolSettings
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
