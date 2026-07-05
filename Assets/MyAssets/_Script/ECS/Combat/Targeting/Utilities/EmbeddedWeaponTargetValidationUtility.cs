using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class EmbeddedWeaponTargetValidationUtility
{
    public static bool IsTargetValidForSlot(
        Entity target,
        float2 muzzlePosition,
        Faction ownerFaction,
        Faction targetFaction,
        bool ownerUsesFogOfWar,
        in WeaponProfileBlob profile,
        ref ComponentLookup<LocalTransform> localTransformLookup,
        ref ComponentLookup<Unit> unitLookup,
        ref ComponentLookup<Visibility> visibilityLookup,
        ref ComponentLookup<UnitCollisionRadius> collisionRadiusLookup,
        ref ComponentLookup<Health> healthLookup,
        out float2 targetPosition,
        out float edgeDistance,
        out byte shipSize)
    {
        targetPosition = float2.zero;
        edgeDistance = float.MaxValue;
        shipSize = 0;

        if (target == Entity.Null || !localTransformLookup.HasComponent(target) || !unitLookup.TryGetComponent(target, out Unit targetUnit))
        {
            return false;
        }

        if (targetUnit.faction != targetFaction)
        {
            return false;
        }

        shipSize = targetUnit.shipSize;
        if ((profile.allowedTargets & shipSize) == 0)
        {
            return false;
        }

        // note: FindTargetSystem has no DeadTag/Destroying checks, Health is the only dead-state here
        if (healthLookup.TryGetComponent(target, out Health health) && health.healthAmount <= 0f)
        {
            return false;
        }

        if (ownerUsesFogOfWar && !VisibilityUtility.IsVisibleToFaction(target, ownerFaction, ref visibilityLookup))
        {
            return false;
        }

        LocalTransform targetTransform = localTransformLookup[target];
        targetPosition = targetTransform.Position.xy;

        float2 radius = float2.zero;
        if (collisionRadiusLookup.TryGetComponent(target, out UnitCollisionRadius collisionRadius))
        {
            radius = collisionRadius.collisionRadius;
        }

        edgeDistance = math.distance(targetPosition, muzzlePosition) - math.max(radius.x, radius.y);
        return edgeDistance <= profile.attackDistance;
    }

    public static bool IsGridCandidateValidForSlot(
        Grid candidate,
        float2 muzzlePosition,
        Faction ownerFaction,
        Faction targetFaction,
        bool ownerUsesFogOfWar,
        in WeaponProfileBlob profile,
        ref ComponentLookup<LocalTransform> localTransformLookup,
        ref ComponentLookup<Unit> unitLookup,
        ref ComponentLookup<Visibility> visibilityLookup,
        ref ComponentLookup<Health> healthLookup,
        out float edgeDistance,
        out float2 targetPosition)
    {
        edgeDistance = float.MaxValue;
        targetPosition = float2.zero;

        if (candidate.Entity == Entity.Null ||
            !localTransformLookup.HasComponent(candidate.Entity) ||
            !unitLookup.TryGetComponent(candidate.Entity, out Unit targetUnit))
        {
            return false;
        }

        byte shipSize = targetUnit.shipSize;
        if ((profile.allowedTargets & shipSize) == 0)
        {
            return false;
        }

        // classic grid path gets faction safety from GetEntityMap
        // embedded also checks Unit.faction because forced targets don't come from map
        if (targetUnit.faction != targetFaction)
        {
            return false;
        }

        // same note: Health is the only dead-state component, keep this extra check
        if (healthLookup.TryGetComponent(candidate.Entity, out Health health) && health.healthAmount <= 0f)
        {
            return false;
        }

        if (ownerUsesFogOfWar && !VisibilityUtility.IsVisibleToFaction(candidate.Entity, ownerFaction, ref visibilityLookup))
        {
            return false;
        }

        targetPosition = localTransformLookup[candidate.Entity].Position.xy;
        edgeDistance = math.distance(targetPosition, muzzlePosition) - math.max(candidate.CollisionRadius.x, candidate.CollisionRadius.y);
        return edgeDistance <= profile.attackDistance;
    }
}
