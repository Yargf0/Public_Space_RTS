using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public static class EmbeddedActionStatusMergeUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmpStatus BuildEmpStatus(in EmbeddedActionSlot action)
    {
        return new EmpStatus
        {
            timer = math.max(0.01f, action.statusDuration),
            moveSpeedMultiplier = action.moveSpeedMultiplier <= 0f ? 1f : action.moveSpeedMultiplier,
            accelerationMultiplier = action.accelerationMultiplier <= 0f ? 1f : action.accelerationMultiplier,
            disableWeapons = action.disableWeapons != 0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmbeddedActionBuffStatus BuildBuffStatus(in EmbeddedActionSlot action)
    {
        return new EmbeddedActionBuffStatus
        {
            timer = math.max(0.01f, action.statusDuration),
            effectMultiplier = action.effectMultiplier <= 0f ? 1f : action.effectMultiplier,
            moveSpeedMultiplier = action.moveSpeedMultiplier <= 0f ? 1f : action.moveSpeedMultiplier,
            accelerationMultiplier = action.accelerationMultiplier <= 0f ? 1f : action.accelerationMultiplier,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmbeddedActionDebuffStatus BuildDebuffStatus(in EmbeddedActionSlot action)
    {
        return new EmbeddedActionDebuffStatus
        {
            timer = math.max(0.01f, action.statusDuration),
            effectMultiplier = action.effectMultiplier <= 0f ? 1f : action.effectMultiplier,
            moveSpeedMultiplier = action.moveSpeedMultiplier <= 0f ? 1f : action.moveSpeedMultiplier,
            accelerationMultiplier = action.accelerationMultiplier <= 0f ? 1f : action.accelerationMultiplier,
            disableWeapons = action.disableWeapons != 0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmpStatus MergeEmp(EmpStatus existing, EmpStatus incoming)
    {
        existing.timer = math.max(math.max(0f, existing.timer), incoming.timer);
        existing.moveSpeedMultiplier = math.min(existing.moveSpeedMultiplier <= 0f ? 1f : existing.moveSpeedMultiplier, incoming.moveSpeedMultiplier <= 0f ? 1f : incoming.moveSpeedMultiplier);
        existing.accelerationMultiplier = math.min(existing.accelerationMultiplier <= 0f ? 1f : existing.accelerationMultiplier, incoming.accelerationMultiplier <= 0f ? 1f : incoming.accelerationMultiplier);
        existing.disableWeapons = existing.disableWeapons || incoming.disableWeapons;
        return existing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmbeddedActionBuffStatus MergeBuff(EmbeddedActionBuffStatus existing, EmbeddedActionBuffStatus incoming)
    {
        existing.timer = math.max(math.max(0f, existing.timer), incoming.timer);
        existing.effectMultiplier = math.max(existing.effectMultiplier <= 0f ? 1f : existing.effectMultiplier, incoming.effectMultiplier <= 0f ? 1f : incoming.effectMultiplier);
        existing.moveSpeedMultiplier = math.max(existing.moveSpeedMultiplier <= 0f ? 1f : existing.moveSpeedMultiplier, incoming.moveSpeedMultiplier <= 0f ? 1f : incoming.moveSpeedMultiplier);
        existing.accelerationMultiplier = math.max(existing.accelerationMultiplier <= 0f ? 1f : existing.accelerationMultiplier, incoming.accelerationMultiplier <= 0f ? 1f : incoming.accelerationMultiplier);
        return existing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmbeddedActionDebuffStatus MergeDebuff(EmbeddedActionDebuffStatus existing, EmbeddedActionDebuffStatus incoming)
    {
        existing.timer = math.max(math.max(0f, existing.timer), incoming.timer);
        existing.effectMultiplier = math.min(existing.effectMultiplier <= 0f ? 1f : existing.effectMultiplier, incoming.effectMultiplier <= 0f ? 1f : incoming.effectMultiplier);
        existing.moveSpeedMultiplier = math.min(existing.moveSpeedMultiplier <= 0f ? 1f : existing.moveSpeedMultiplier, incoming.moveSpeedMultiplier <= 0f ? 1f : incoming.moveSpeedMultiplier);
        existing.accelerationMultiplier = math.min(existing.accelerationMultiplier <= 0f ? 1f : existing.accelerationMultiplier, incoming.accelerationMultiplier <= 0f ? 1f : incoming.accelerationMultiplier);
        existing.disableWeapons = existing.disableWeapons || incoming.disableWeapons;
        return existing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MergePendingEmp(ref NativeParallelHashMap<Entity, EmpStatus> pending, Entity target, EmpStatus status)
    {
        if (pending.TryGetValue(target, out EmpStatus existing))
        {
            pending[target] = MergeEmp(existing, status);
            return;
        }

        pending.TryAdd(target, status);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MergePendingBuff(ref NativeParallelHashMap<Entity, EmbeddedActionBuffStatus> pending, Entity target, EmbeddedActionBuffStatus status)
    {
        if (pending.TryGetValue(target, out EmbeddedActionBuffStatus existing))
        {
            pending[target] = MergeBuff(existing, status);
            return;
        }

        pending.TryAdd(target, status);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MergePendingDebuff(ref NativeParallelHashMap<Entity, EmbeddedActionDebuffStatus> pending, Entity target, EmbeddedActionDebuffStatus status)
    {
        if (pending.TryGetValue(target, out EmbeddedActionDebuffStatus existing))
        {
            pending[target] = MergeDebuff(existing, status);
            return;
        }

        pending.TryAdd(target, status);
    }
}
