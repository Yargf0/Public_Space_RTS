using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// shared consumers for Buff/Debuff/EMP statuses so systems don't duplicate this math
[BurstCompile]
public static class EmbeddedActionStatusUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AreWeaponsDisabled(
        Entity entity,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup)
    {
        if (entity == Entity.Null)
        {
            return false;
        }

        if (empStatusLookup.HasComponent(entity) &&
            empStatusLookup.IsComponentEnabled(entity) &&
            empStatusLookup[entity].disableWeapons)
        {
            return true;
        }

        return debuffStatusLookup.HasComponent(entity) &&
               debuffStatusLookup.IsComponentEnabled(entity) &&
               debuffStatusLookup[entity].disableWeapons;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetMoveSpeedMultiplier(
        Entity entity,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup,
        ref ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup)
    {
        if (entity == Entity.Null)
        {
            return 1f;
        }

        float multiplier = 1f;

        if (empStatusLookup.HasComponent(entity) && empStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(empStatusLookup[entity].moveSpeedMultiplier);
        }

        if (buffStatusLookup.HasComponent(entity) && buffStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(buffStatusLookup[entity].moveSpeedMultiplier);
        }

        if (debuffStatusLookup.HasComponent(entity) && debuffStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(debuffStatusLookup[entity].moveSpeedMultiplier);
        }

        return math.clamp(multiplier, 0f, 10f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetAccelerationMultiplier(
        Entity entity,
        ref ComponentLookup<EmpStatus> empStatusLookup,
        ref ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup,
        ref ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup)
    {
        if (entity == Entity.Null)
        {
            return 1f;
        }

        float multiplier = 1f;

        if (empStatusLookup.HasComponent(entity) && empStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(empStatusLookup[entity].accelerationMultiplier);
        }

        if (buffStatusLookup.HasComponent(entity) && buffStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(buffStatusLookup[entity].accelerationMultiplier);
        }

        if (debuffStatusLookup.HasComponent(entity) && debuffStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(debuffStatusLookup[entity].accelerationMultiplier);
        }

        return math.clamp(multiplier, 0f, 10f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetOutgoingEffectMultiplier(
        Entity entity,
        ref ComponentLookup<EmbeddedActionBuffStatus> buffStatusLookup,
        ref ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup)
    {
        if (entity == Entity.Null)
        {
            return 1f;
        }

        float multiplier = 1f;

        if (buffStatusLookup.HasComponent(entity) && buffStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(buffStatusLookup[entity].effectMultiplier);
        }

        if (debuffStatusLookup.HasComponent(entity) && debuffStatusLookup.IsComponentEnabled(entity))
        {
            multiplier *= NormalizeMultiplier(debuffStatusLookup[entity].effectMultiplier);
        }

        return math.clamp(multiplier, 0f, 10f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetIncomingDamageMultiplier(
        Entity target,
        ref ComponentLookup<EmbeddedActionDebuffStatus> debuffStatusLookup)
    {
        if (target == Entity.Null || !debuffStatusLookup.HasComponent(target) || !debuffStatusLookup.IsComponentEnabled(target))
        {
            return 1f;
        }

        float debuffEffect = NormalizeMultiplier(debuffStatusLookup[target].effectMultiplier);

        // debuff multiplier below 1 = weaker armor = more incoming damage
        // example: 0.75 => x1.333
        if (debuffEffect < 0.999f)
        {
            return math.clamp(1f / debuffEffect, 1f, 10f);
        }

        // if designer sets >1 on purpose, use it as direct multiplier
        return math.clamp(debuffEffect, 1f, 10f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NormalizeMultiplier(float value)
    {
        return value <= 0f ? 1f : value;
    }
}
