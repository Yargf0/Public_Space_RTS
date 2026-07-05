using UnityEngine;

public static class EmbeddedWeaponAuthoringValidationUtility
{
    public static bool TryValidateSlot(EmbeddedWeaponSlotAuthoring slot, out string error)
    {
        error = string.Empty;

        if (slot == null)
        {
            error = "slot authoring is null.";
            return false;
        }

        if (!TryValidateSlotModel(slot, out error))
        {
            return false;
        }

        if (HasZeroAxisScale(slot.transform.lossyScale))
        {
            error = "slot transform has zero axis in lossyScale. Scaled prefabs are allowed, but zero scale cannot be baked safely.";
            return false;
        }

        if (slot.muzzlePoint != null && HasZeroAxisScale(slot.muzzlePoint.lossyScale))
        {
            error = "muzzlePoint has zero axis in lossyScale. Scaled prefabs are allowed, but zero scale cannot be baked safely.";
            return false;
        }

        if (slot.muzzlePoints != null)
        {
            for (int i = 0; i < slot.muzzlePoints.Length; i++)
            {
                Transform muzzle = slot.muzzlePoints[i];
                if (muzzle != null && HasZeroAxisScale(muzzle.lossyScale))
                {
                    error = $"muzzlePoints[{i}] has zero axis in lossyScale. Scaled prefabs are allowed, but zero scale cannot be baked safely.";
                    return false;
                }
            }
        }

        if (slot.visualRoot != null && HasZeroAxisScale(slot.visualRoot.lossyScale))
        {
            error = "visualRoot has zero axis in lossyScale. Scaled prefabs are allowed, but zero scale cannot be baked safely.";
            return false;
        }

        if (slot.visualRoot != null && !slot.visualRoot.IsChildOf(slot.transform.root))
        {
            error = "visualRoot must be inside the same prefab/scene hierarchy as the embedded weapon slot.";
            return false;
        }

        if (HasForbiddenEntityGeneratingComponentInHierarchy(slot, slot.visualRoot, out string componentName))
        {
            error = slot.visualRoot == null
                ? $"marker hierarchy contains '{componentName}', which can bake an entity/Parent. Leave Visual Root empty only for weapons drawn directly into the ship sprite/mesh. For a rotating turret/sprite, move the renderer under a child object and assign it to Visual Root."
                : $"marker hierarchy contains forbidden '{componentName}' outside the assigned Visual Root. Only visual renderers under Visual Root are allowed.";
            return false;
        }

        return true;
    }

    private static bool TryValidateSlotModel(EmbeddedWeaponSlotAuthoring slot, out string error)
    {
        error = string.Empty;

        EmbeddedActionDeliveryKind deliveryKind = EmbeddedActionAuthoringUtility.ResolveDeliveryKind(slot);
        EmbeddedActionEffectKind effectKind = EmbeddedActionAuthoringUtility.ResolveEffectKind(slot);
        EmbeddedActionTargetFilter targetFilter = EmbeddedActionAuthoringUtility.ResolveTargetFilter(slot);

        if (deliveryKind == EmbeddedActionDeliveryKind.WeaponProfile)
        {
            if (effectKind != EmbeddedActionEffectKind.Damage)
            {
                error = $"Delivery=WeaponProfile currently supports Effect=Damage only, got Effect={effectKind}. Use BeamOverTime or Aura for support/status effects.";
                return false;
            }

            if (targetFilter != EmbeddedActionTargetFilter.Enemy)
            {
                error = $"Delivery=WeaponProfile currently supports TargetFilter=Enemy only, got TargetFilter={targetFilter}.";
                return false;
            }

            WeaponProfileSO profile = EmbeddedActionAuthoringUtility.ResolveWeaponProfile(slot);
            if (profile == null)
            {
                error = slot.actionProfile != null
                    ? $"actionProfile '{slot.actionProfile.name}' uses Delivery=WeaponProfile but has no Weapon Profile assigned."
                    : "profile is null.";
                return false;
            }

            GameObject deliveryPrefab = EmbeddedActionAuthoringUtility.ResolveDeliveryPrefab(slot);
            if (deliveryPrefab == null)
            {
                error = slot.actionProfile != null
                    ? $"actionProfile '{slot.actionProfile.name}' has no Delivery Prefab and slot Ammo Game Object is empty."
                    : "ammoGameObject is null.";
                return false;
            }

            if (!IsSupportedEmbeddedFirePattern(profile.firePattern))
            {
                error = $"profile '{profile.name}' uses firePattern={profile.firePattern}. Embedded supports Single, SequentialHardpoints and SimultaneousHardpoints only.";
                return false;
            }

            if (!IsSupportedEmbeddedRequestKind(profile.requestKind))
            {
                error = $"profile '{profile.name}' uses requestKind={profile.requestKind}. Embedded supports Ballistic, Rocket/Torpedo and Hitscan.";
                return false;
            }

            return true;
        }

        if (deliveryKind == EmbeddedActionDeliveryKind.BeamOverTime || deliveryKind == EmbeddedActionDeliveryKind.Aura)
        {
            if (!IsImplementedTickEffect(effectKind))
            {
                error = $"Delivery={deliveryKind} does not support Effect={effectKind}.";
                return false;
            }

            if (!IsSupportedTargetFilterForEffect(targetFilter, effectKind))
            {
                error = $"Effect={effectKind} does not support TargetFilter={targetFilter}.";
                return false;
            }

            if (!ValidateCommonActionNumbers(slot, effectKind, out error))
            {
                return false;
            }

            return true;
        }

        error = $"deliveryKind={deliveryKind} is not implemented yet. The data model supports it, but the runtime system is not added.";
        return false;
    }

    public static bool IsSupportedEmbeddedFirePattern(WeaponFirePattern firePattern)
    {
        return firePattern == WeaponFirePattern.Single
            || firePattern == WeaponFirePattern.SequentialHardpoints
            || firePattern == WeaponFirePattern.SimultaneousHardpoints;
    }

    private static bool IsSupportedEmbeddedRequestKind(WeaponRequestKind requestKind)
    {
        return requestKind == WeaponRequestKind.Ballistic
            || requestKind == WeaponRequestKind.Rocket
            || requestKind == WeaponRequestKind.Hitscan;
    }

    private static bool IsImplementedTickEffect(EmbeddedActionEffectKind effectKind)
    {
        return effectKind == EmbeddedActionEffectKind.Damage
            || effectKind == EmbeddedActionEffectKind.Repair
            || effectKind == EmbeddedActionEffectKind.ShieldRestore
            || effectKind == EmbeddedActionEffectKind.Emp
            || effectKind == EmbeddedActionEffectKind.Buff
            || effectKind == EmbeddedActionEffectKind.Debuff;
    }

    private static bool IsSupportedTargetFilterForEffect(EmbeddedActionTargetFilter targetFilter, EmbeddedActionEffectKind effectKind)
    {
        switch (effectKind)
        {
            case EmbeddedActionEffectKind.Damage:
            case EmbeddedActionEffectKind.Emp:
            case EmbeddedActionEffectKind.Debuff:
                return targetFilter == EmbeddedActionTargetFilter.Enemy;

            case EmbeddedActionEffectKind.Repair:
            case EmbeddedActionEffectKind.ShieldRestore:
            case EmbeddedActionEffectKind.Buff:
                return targetFilter == EmbeddedActionTargetFilter.AllyDamaged
                    || targetFilter == EmbeddedActionTargetFilter.AllyAny
                    || targetFilter == EmbeddedActionTargetFilter.Self;

            default:
                return false;
        }
    }

    private static bool ValidateCommonActionNumbers(EmbeddedWeaponSlotAuthoring slot, EmbeddedActionEffectKind effectKind, out string error)
    {
        error = string.Empty;

        if (EmbeddedActionAuthoringUtility.ResolveRange(slot) <= 0f)
        {
            error = "action range must be > 0.";
            return false;
        }

        if (EffectUsesValuePerSecond(effectKind) && EmbeddedActionAuthoringUtility.ResolveValuePerSecond(slot) <= 0f)
        {
            error = "valuePerSecond must be > 0 for Damage/Repair/ShieldRestore action profiles.";
            return false;
        }

        if (EmbeddedActionAuthoringUtility.ResolveTickInterval(slot) <= 0f)
        {
            error = "tickInterval must be > 0.";
            return false;
        }

        if (EmbeddedActionAuthoringUtility.ResolveSearchInterval(slot) <= 0f)
        {
            error = "searchInterval must be > 0.";
            return false;
        }

        if (EmbeddedActionAuthoringUtility.ResolveRotateSpeed(slot) < 0f)
        {
            error = "rotateSpeed must be >= 0.";
            return false;
        }

        if (EmbeddedActionAuthoringUtility.ResolveBeamWidth(slot) <= 0f)
        {
            error = "beamWidth must be > 0.";
            return false;
        }

        if (EmbeddedActionAuthoringUtility.ResolveBeamVisualInterval(slot) <= 0f)
        {
            error = "beamVisualInterval must be > 0.";
            return false;
        }

        if (effectKind == EmbeddedActionEffectKind.ShieldRestore && EmbeddedActionAuthoringUtility.ResolveMaxStoredValue(slot) <= 0f)
        {
            error = "maxStoredValue must be > 0 for ShieldRestore.";
            return false;
        }

        if ((effectKind == EmbeddedActionEffectKind.Emp || effectKind == EmbeddedActionEffectKind.Buff || effectKind == EmbeddedActionEffectKind.Debuff) &&
            EmbeddedActionAuthoringUtility.ResolveStatusDuration(slot) <= 0f)
        {
            error = "statusDuration must be > 0 for EMP/Buff/Debuff.";
            return false;
        }

        return true;
    }

    private static bool EffectUsesValuePerSecond(EmbeddedActionEffectKind effectKind)
    {
        return effectKind == EmbeddedActionEffectKind.Damage
            || effectKind == EmbeddedActionEffectKind.Repair
            || effectKind == EmbeddedActionEffectKind.ShieldRestore;
    }

    private static bool HasForbiddenEntityGeneratingComponentInHierarchy(EmbeddedWeaponSlotAuthoring slot, Transform allowedVisualRoot, out string componentName)
    {
        componentName = string.Empty;

        Renderer[] renderers = slot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || IsInsideAllowedVisualRoot(renderer.transform, allowedVisualRoot))
            {
                continue;
            }

            componentName = renderer.GetType().Name;
            return true;
        }

        ParticleSystem[] particleSystems = slot.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null || IsInsideAllowedVisualRoot(particleSystem.transform, allowedVisualRoot))
            {
                continue;
            }

            componentName = "ParticleSystem";
            return true;
        }

        if (HasAnyForbiddenComponent(slot.GetComponentsInChildren<Collider>(true), out componentName) ||
            HasAnyForbiddenComponent(slot.GetComponentsInChildren<Collider2D>(true), out componentName) ||
            HasAnyForbiddenComponent(slot.GetComponentsInChildren<Rigidbody>(true), out componentName) ||
            HasAnyForbiddenComponent(slot.GetComponentsInChildren<Rigidbody2D>(true), out componentName) ||
            HasAnyForbiddenComponent(slot.GetComponentsInChildren<LODGroup>(true), out componentName) ||
            HasAnyForbiddenComponent(slot.GetComponentsInChildren<Light>(true), out componentName))
        {
            return true;
        }

        MonoBehaviour[] behaviours = slot.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == slot)
            {
                continue;
            }

            // VisualRoot can have small data-only markers
            // without this check baker rejects whole slot and gun don't fire
            if (IsInsideAllowedVisualRoot(behaviour.transform, allowedVisualRoot))
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (!typeName.EndsWith("Authoring"))
            {
                continue;
            }

            componentName = typeName;
            return true;
        }

        return false;
    }

    private static bool HasAnyForbiddenComponent<T>(T[] components, out string componentName)
        where T : Component
    {
        componentName = string.Empty;

        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component == null)
            {
                continue;
            }

            componentName = component.GetType().Name;
            return true;
        }

        return false;
    }

    private static bool IsInsideAllowedVisualRoot(Transform transform, Transform allowedVisualRoot)
    {
        return allowedVisualRoot != null && transform != null && transform.IsChildOf(allowedVisualRoot);
    }

    private static bool HasZeroAxisScale(Vector3 scale)
    {
        const float Epsilon = 0.0001f;
        return Mathf.Abs(scale.x) < Epsilon
            || Mathf.Abs(scale.y) < Epsilon
            || Mathf.Abs(scale.z) < Epsilon;
    }
}
