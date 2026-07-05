using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(BulletMoverSystem))]
[UpdateAfter(typeof(RocketMoverSystem))]
public partial struct ProjectilePoolDiagnosticsLogSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ProjectilePool>();
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach ((RefRO<ProjectilePool> pool, RefRW<ProjectilePoolLogState> logState)
            in SystemAPI.Query<RefRO<ProjectilePool>, RefRW<ProjectilePoolLogState>>())
        {
            string label = pool.ValueRO.kind == ProjectilePoolKind.Rocket ? "RocketPool" : "BulletPool";

            if (pool.ValueRO.runtimeExpandCount != logState.ValueRO.lastRuntimeExpandCount)
            {
                if (pool.ValueRO.logRuntimeExpand)
                {
                    int delta = pool.ValueRO.runtimeExpandCount - logState.ValueRO.lastRuntimeExpandCount;
                    Debug.Log($"[{label}] Expanded prefab={pool.ValueRO.prefabEntity.Index}:{pool.ValueRO.prefabEntity.Version} delta={delta} totalSpawned={pool.ValueRO.totalSpawned} active={pool.ValueRO.activeCount} peak={pool.ValueRO.peakActiveCount}");
                }

                logState.ValueRW.lastRuntimeExpandCount = pool.ValueRO.runtimeExpandCount;
            }

            if (pool.ValueRO.droppedShotCount != logState.ValueRO.lastDroppedShotCount)
            {
                int delta = pool.ValueRO.droppedShotCount - logState.ValueRO.lastDroppedShotCount;
                Debug.LogWarning($"[{label}] Dropped shots prefab={pool.ValueRO.prefabEntity.Index}:{pool.ValueRO.prefabEntity.Version} delta={delta} totalDropped={pool.ValueRO.droppedShotCount} hardCap={pool.ValueRO.hardCap}");
                logState.ValueRW.lastDroppedShotCount = pool.ValueRO.droppedShotCount;
            }

            if (pool.ValueRO.duplicateFreeEntryCount != logState.ValueRO.lastDuplicateFreeEntryCount)
            {
                int delta = pool.ValueRO.duplicateFreeEntryCount - logState.ValueRO.lastDuplicateFreeEntryCount;
                Debug.LogWarning($"[{label}] Skipped invalid free entries prefab={pool.ValueRO.prefabEntity.Index}:{pool.ValueRO.prefabEntity.Version} delta={delta} total={pool.ValueRO.duplicateFreeEntryCount}");
                logState.ValueRW.lastDuplicateFreeEntryCount = pool.ValueRO.duplicateFreeEntryCount;
            }

            if (pool.ValueRO.doubleReleaseCount != logState.ValueRO.lastDoubleReleaseCount)
            {
                int delta = pool.ValueRO.doubleReleaseCount - logState.ValueRO.lastDoubleReleaseCount;
                Debug.LogWarning($"[{label}] Double release ignored prefab={pool.ValueRO.prefabEntity.Index}:{pool.ValueRO.prefabEntity.Version} delta={delta} total={pool.ValueRO.doubleReleaseCount}");
                logState.ValueRW.lastDoubleReleaseCount = pool.ValueRO.doubleReleaseCount;
            }
        }
    }
}
