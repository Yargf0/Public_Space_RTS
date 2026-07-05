using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public static class VisibilityUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Faction Opposite(Faction faction)
    {
        return faction == Faction.Friendly ? Faction.Enemy : Faction.Friendly;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVisibleToFaction(
        Entity target,
        Faction observerFaction,
        ref ComponentLookup<Visibility> visibilityLookup)
    {
        if (target == Entity.Null || !visibilityLookup.HasComponent(target) || !visibilityLookup.IsComponentEnabled(target))
        {
            return false;
        }

        Visibility visibility = visibilityLookup[target];

        return observerFaction == Faction.Friendly
            ? visibility.visibleToFriendlyTimer > 0f
            : visibility.visibleToEnemyTimer > 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RefreshVisibilityForFaction(
        Entity target,
        Faction observerFaction,
        float duration,
        ref ComponentLookup<Visibility> visibilityLookup)
    {
        if (target == Entity.Null || duration <= 0f || !visibilityLookup.HasComponent(target))
        {
            return;
        }

        Visibility visibility = visibilityLookup[target];

        if (observerFaction == Faction.Friendly)
        {
            visibility.visibleToFriendlyTimer = math.max(visibility.visibleToFriendlyTimer, duration);
        }
        else
        {
            visibility.visibleToEnemyTimer = math.max(visibility.visibleToEnemyTimer, duration);
        }

        visibilityLookup[target] = visibility;
        visibilityLookup.SetComponentEnabled(target, true);
    }
}
