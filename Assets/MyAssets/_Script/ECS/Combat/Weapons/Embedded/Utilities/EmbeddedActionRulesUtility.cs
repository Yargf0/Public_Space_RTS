using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public static class EmbeddedActionRulesUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsOffensiveAction(EmbeddedActionTargetFilter targetFilter, EmbeddedActionEffectKind effectKind)
    {
        if (effectKind == EmbeddedActionEffectKind.Damage)
        {
            return true;
        }

        return targetFilter == EmbeddedActionTargetFilter.Enemy &&
               (effectKind == EmbeddedActionEffectKind.Emp || effectKind == EmbeddedActionEffectKind.Debuff);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFireAllowed(
        Entity owner,
        ref ComponentLookup<ShipStateComponent> shipStateLookupRef,
        ref ComponentLookup<ShipAgro> shipAgroLookupRef)
    {
        if (!shipStateLookupRef.TryGetComponent(owner, out ShipStateComponent shipState))
        {
            return true;
        }

        bool wasHit = shipAgroLookupRef.TryGetComponent(owner, out ShipAgro agro) && agro.wasHit;
        bool hasForcedTarget = shipState.forcedTarget != Entity.Null;

        return (hasForcedTarget && shipState.currentState == ShipState.InCombat)
            || shipState.mode == FireMode.FireAtWill
            || (shipState.mode == FireMode.ReturnFire && wasHit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSupportedAction(in EmbeddedActionSlot action)
    {
        EmbeddedActionDeliveryKind delivery = (EmbeddedActionDeliveryKind)action.deliveryKind;
        EmbeddedActionEffectKind effect = (EmbeddedActionEffectKind)action.effectKind;

        if (delivery == EmbeddedActionDeliveryKind.BeamOverTime || delivery == EmbeddedActionDeliveryKind.Aura)
        {
            return effect == EmbeddedActionEffectKind.Damage
                || effect == EmbeddedActionEffectKind.Repair
                || effect == EmbeddedActionEffectKind.ShieldRestore
                || effect == EmbeddedActionEffectKind.Emp
                || effect == EmbeddedActionEffectKind.Buff
                || effect == EmbeddedActionEffectKind.Debuff;
        }

        return false;
    }
}
