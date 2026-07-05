using UnityEngine;

public static class EmbeddedActionAuthoringUtility
{
    public static EmbeddedActionTargetFilter ResolveTargetFilter(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null
            ? slot.actionProfile.targetFilter
            : EmbeddedActionTargetFilter.Enemy;
    }

    public static EmbeddedActionDeliveryKind ResolveDeliveryKind(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null
            ? slot.actionProfile.deliveryKind
            : EmbeddedActionDeliveryKind.WeaponProfile;
    }

    public static EmbeddedActionEffectKind ResolveEffectKind(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null
            ? slot.actionProfile.effectKind
            : EmbeddedActionEffectKind.Damage;
    }

    public static EmbeddedWeaponSlotRole ResolveRuntimeRole(EmbeddedWeaponSlotAuthoring slot)
    {
        EmbeddedActionDeliveryKind deliveryKind = ResolveDeliveryKind(slot);
        EmbeddedActionEffectKind effectKind = ResolveEffectKind(slot);

        if (deliveryKind == EmbeddedActionDeliveryKind.WeaponProfile && effectKind == EmbeddedActionEffectKind.Damage)
        {
            return EmbeddedWeaponSlotRole.Damage;
        }

        // everything else is support, damage systems must ignore it
        return EmbeddedWeaponSlotRole.Support;
    }

    public static WeaponProfileSO ResolveWeaponProfile(EmbeddedWeaponSlotAuthoring slot)
    {
        if (slot == null)
        {
            return null;
        }

        return slot.actionProfile != null ? slot.actionProfile.weaponProfile : slot.profile;
    }

    public static GameObject ResolveDeliveryPrefab(EmbeddedWeaponSlotAuthoring slot)
    {
        if (slot == null)
        {
            return null;
        }

        return slot.actionProfile != null ? slot.actionProfile.deliveryPrefab : slot.ammoGameObject;
    }

    public static float ResolveRange(EmbeddedWeaponSlotAuthoring slot)
    {
        if (slot != null && slot.actionProfile != null)
        {
            return slot.actionProfile.range;
        }

        WeaponProfileSO profile = ResolveWeaponProfile(slot);
        return profile != null ? profile.attackDistance : 0f;
    }

    public static float ResolveValuePerSecond(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.valuePerSecond : 0f;
    }

    public static float ResolveTickInterval(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.tickInterval : 0.2f;
    }

    public static float ResolveSearchInterval(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.searchInterval : 0.2f;
    }

    public static float ResolveRotateSpeed(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.rotateSpeed : 0f;
    }

    public static float ResolveAimInterval(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.aimInterval : 1f / 30f;
    }

    public static bool ResolveRotate(EmbeddedWeaponSlotAuthoring slot)
    {
        if (slot != null && slot.actionProfile != null)
        {
            return slot.actionProfile.rotate;
        }

        WeaponProfileSO profile = ResolveWeaponProfile(slot);
        return profile != null && profile.rotate;
    }

    public static bool ResolveCanTargetSelf(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null && slot.actionProfile.canTargetSelf;
    }

    public static float ResolveBeamWidth(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.beamWidth : 0.35f;
    }

    public static float ResolveBeamVisualInterval(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.beamVisualInterval : 0.05f;
    }

    public static float ResolveMaxStoredValue(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.maxStoredValue : 0f;
    }

    public static float ResolveStatusDuration(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.statusDuration : 0f;
    }

    public static float ResolveMoveSpeedMultiplier(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.moveSpeedMultiplier : 1f;
    }

    public static float ResolveAccelerationMultiplier(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.accelerationMultiplier : 1f;
    }

    public static float ResolveEffectMultiplier(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null ? slot.actionProfile.effectMultiplier : 1f;
    }

    public static bool ResolveDisableWeapons(EmbeddedWeaponSlotAuthoring slot)
    {
        return slot != null && slot.actionProfile != null && slot.actionProfile.disableWeapons;
    }

    public static int ResolveMaxTargetsPerTick(EmbeddedWeaponSlotAuthoring slot)
    {
        // V6: full aura scan is forced, budget fields ignored
        return 0;
    }

    public static int ResolveMaxCellsPerTick(EmbeddedWeaponSlotAuthoring slot)
    {
        // V6: full aura scan is forced, budget fields ignored
        return 0;
    }
}
