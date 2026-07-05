using Unity.Entities;
using UnityEngine;

// one entry point for weapon catalog, put on one GameObject in SubScene
// baker makes tag + buffer, build system turns it into blob
public class GlobalWeaponCatalogAuthoring : MonoBehaviour
{
    public WeaponCatalogAsset catalog;

    private class Baker : Baker<GlobalWeaponCatalogAuthoring>
    {
        public override void Bake(GlobalWeaponCatalogAuthoring authoring)
        {
            // rebake when catalog changes
            DependsOn(authoring.catalog);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new WeaponCatalogTag());

            DynamicBuffer<WeaponProfileBakeElement> buffer = AddBuffer<WeaponProfileBakeElement>(entity);

            if (authoring.catalog == null || authoring.catalog.profiles == null || authoring.catalog.profiles.Length == 0)
            {
                Debug.LogWarning($"GlobalWeaponCatalogAuthoring on '{authoring.name}' has no catalog or profiles array is empty.", authoring);
                return;
            }

            buffer.EnsureCapacity(authoring.catalog.profiles.Length);

            for (int i = 0; i < authoring.catalog.profiles.Length; i++)
            {
                WeaponProfileSO p = authoring.catalog.profiles[i];
                if (p == null)
                {
                    // empty slot: still add default element so indices stay stable
                    buffer.Add(default);
                    continue;
                }

                // Direct with splashRadius > 0 becomes Splash
                byte resolvedPayloadKind = (byte)p.payloadKind;
                if (p.payloadKind == WeaponPayloadKind.Direct && p.splashRadius > 0.001f)
                    resolvedPayloadKind = (byte)WeaponPayloadKind.Splash;

                buffer.Add(new WeaponProfileBakeElement
                {
                    requestKind = (byte)p.requestKind,
                    firePattern = (byte)p.firePattern,
                    payloadKind = resolvedPayloadKind,
                    statusEffectKind = (byte)p.statusEffectKind,
                    reloadTime = p.reloadTime,
                    attackDistance = p.attackDistance,
                    damageAmount = p.damageAmount,
                    projectileSpeed = p.projectileSpeed,
                    lifetime = p.lifetime,
                    rotate = p.rotate,
                    limitRotation = p.limitRotation,
                    rotationLimitAngle = p.rotationLimitAngle,
                    allowedTargets = (byte)p.allowedTargets,
                    priorityTargets = (byte)p.priorityTargets,
                    burstInterval = p.burstInterval,
                    spreadAngle = p.spreadAngle,
                    splashRadius = p.splashRadius,
                    statusDuration = p.statusDuration,
                    moveSpeedMultiplier = p.moveSpeedMultiplier,
                    accelerationMultiplier = p.accelerationMultiplier,
                    disableWeapons = p.disableWeapons,
                    rocketAcceleration = p.rocketAcceleration,
                    turnAfterLaunch = p.turnAfterLaunch,
                    rocketLaunchScatterAngle = p.rocketLaunchScatterAngle,
                    rocketLaunchScatterDuration = p.rocketLaunchScatterDuration,
                    rocketLaunchScatterDistance = p.rocketLaunchScatterDistance,
                    hitscanBeamWidth = p.hitscanBeamWidth,
                });
            }
        }
    }
}
