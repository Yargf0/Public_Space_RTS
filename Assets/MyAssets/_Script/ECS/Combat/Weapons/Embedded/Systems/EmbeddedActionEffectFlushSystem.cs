using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// applies all Beam/Aura effects once per frame
// beam and aura systems only append, this one writes Health and statuses
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedAuraActionSystem))]
[UpdateBefore(typeof(EmbeddedActionVisualSystem))]
public partial struct EmbeddedActionEffectFlushSystem : ISystem
{
    private ComponentLookup<Health> healthLookup;
    private ComponentLookup<ShipAgro> shipAgroLookup;
    private ComponentLookup<EmpStatus> empStatusLookup;
    private ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup;
    private ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup;
    private ComponentLookup<Unit> unitLookup;

    private NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator> pendingEffects;
    private NativeParallelHashMap<Entity, EmpStatus> pendingEmp;
    private NativeParallelHashMap<Entity, EmbeddedActionBuffStatus> pendingBuff;
    private NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus> pendingDebuff;

    public void OnCreate(ref SystemState state)
    {
        Entity singleton = state.EntityManager.CreateEntity(typeof(EmbeddedActionEffectFlushSingleton));
        state.EntityManager.AddBuffer<EmbeddedActionPendingEffect>(singleton);
        state.EntityManager.AddBuffer<EmbeddedActionPendingEmpStatus>(singleton);
        state.EntityManager.AddBuffer<EmbeddedActionPendingBuffStatus>(singleton);
        state.EntityManager.AddBuffer<EmbeddedActionPendingDebuffStatus>(singleton);

        healthLookup = state.GetComponentLookup<Health>(false);
        shipAgroLookup = state.GetComponentLookup<ShipAgro>(false);
        empStatusLookup = state.GetComponentLookup<EmpStatus>(false);
        buffStatusLookup = state.GetComponentLookup<EmbeddedActionBuffStatus>(false);
        debuffStatusLookup = state.GetComponentLookup<EmbeddedActionDebuffStatus>(false);
        unitLookup = state.GetComponentLookup<Unit>(true);

        pendingEffects = new NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator>(128, Allocator.Persistent);
        pendingEmp = new NativeParallelHashMap<Entity, EmpStatus>(64, Allocator.Persistent);
        pendingBuff = new NativeParallelHashMap<Entity, EmbeddedActionBuffStatus>(64, Allocator.Persistent);
        pendingDebuff = new NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus>(64, Allocator.Persistent);

        state.RequireForUpdate<EmbeddedActionEffectFlushSingleton>();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (pendingEffects.IsCreated) pendingEffects.Dispose();
        if (pendingEmp.IsCreated) pendingEmp.Dispose();
        if (pendingBuff.IsCreated) pendingBuff.Dispose();
        if (pendingDebuff.IsCreated) pendingDebuff.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        DynamicBuffer<EmbeddedActionPendingEffect> effectBuffer = SystemAPI.GetSingletonBuffer<EmbeddedActionPendingEffect>();
        DynamicBuffer<EmbeddedActionPendingEmpStatus> empBuffer = SystemAPI.GetSingletonBuffer<EmbeddedActionPendingEmpStatus>();
        DynamicBuffer<EmbeddedActionPendingBuffStatus> buffBuffer = SystemAPI.GetSingletonBuffer<EmbeddedActionPendingBuffStatus>();
        DynamicBuffer<EmbeddedActionPendingDebuffStatus> debuffBuffer = SystemAPI.GetSingletonBuffer<EmbeddedActionPendingDebuffStatus>();

        if (effectBuffer.Length == 0 && empBuffer.Length == 0 && buffBuffer.Length == 0 && debuffBuffer.Length == 0)
        {
            return;
        }

        EnsureCapacity(effectBuffer.Length, empBuffer.Length, buffBuffer.Length, debuffBuffer.Length);

        pendingEffects.Clear();
        pendingEmp.Clear();
        pendingBuff.Clear();
        pendingDebuff.Clear();

        for (int i = 0; i < effectBuffer.Length; i++)
        {
            EmbeddedActionPendingEffect item = effectBuffer[i];
            EmbeddedActionRuntimeUtility.AddEffect(ref pendingEffects, item.target, item.effect);
        }

        for (int i = 0; i < empBuffer.Length; i++)
        {
            EmbeddedActionPendingEmpStatus item = empBuffer[i];
            EmbeddedActionStatusMergeUtility.MergePendingEmp(ref pendingEmp, item.target, item.status);
        }

        for (int i = 0; i < buffBuffer.Length; i++)
        {
            EmbeddedActionPendingBuffStatus item = buffBuffer[i];
            EmbeddedActionStatusMergeUtility.MergePendingBuff(ref pendingBuff, item.target, item.status);
        }

        for (int i = 0; i < debuffBuffer.Length; i++)
        {
            EmbeddedActionPendingDebuffStatus item = debuffBuffer[i];
            EmbeddedActionStatusMergeUtility.MergePendingDebuff(ref pendingDebuff, item.target, item.status);
        }

        effectBuffer.Clear();
        empBuffer.Clear();
        buffBuffer.Clear();
        debuffBuffer.Clear();

        healthLookup.Update(ref state);
        shipAgroLookup.Update(ref state);
        empStatusLookup.Update(ref state);
        buffStatusLookup.Update(ref state);
        debuffStatusLookup.Update(ref state);
        unitLookup.Update(ref state);

        EmbeddedActionRuntimeUtility.FlushEffects(ref pendingEffects, ref healthLookup, ref shipAgroLookup);

        EntityCommandBuffer statusFallbackEcb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        bool statusFallbackChanged = EmbeddedActionRuntimeUtility.FlushStatuses(
            ref pendingEmp,
            ref pendingBuff,
            ref pendingDebuff,
            ref empStatusLookup,
            ref buffStatusLookup,
            ref debuffStatusLookup,
            ref unitLookup,
            ref healthLookup,
            ref statusFallbackEcb);

        if (statusFallbackChanged)
        {
            statusFallbackEcb.Playback(state.EntityManager);
        }
    }

    private void EnsureCapacity(int effects, int emp, int buff, int debuff)
    {
        int effectCapacity = math.max(128, effects + 16);
        if (pendingEffects.Capacity < effectCapacity)
        {
            pendingEffects.Dispose();
            pendingEffects = new NativeParallelHashMap<Entity, EmbeddedActionEffectAccumulator>(effectCapacity, Allocator.Persistent);
        }

        int empCapacity = math.max(64, emp + 16);
        if (pendingEmp.Capacity < empCapacity)
        {
            pendingEmp.Dispose();
            pendingEmp = new NativeParallelHashMap<Entity, EmpStatus>(empCapacity, Allocator.Persistent);
        }

        int buffCapacity = math.max(64, buff + 16);
        if (pendingBuff.Capacity < buffCapacity)
        {
            pendingBuff.Dispose();
            pendingBuff = new NativeParallelHashMap<Entity, EmbeddedActionBuffStatus>(buffCapacity, Allocator.Persistent);
        }

        int debuffCapacity = math.max(64, debuff + 16);
        if (pendingDebuff.Capacity < debuffCapacity)
        {
            pendingDebuff.Dispose();
            pendingDebuff = new NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus>(debuffCapacity, Allocator.Persistent);
        }
    }
}
