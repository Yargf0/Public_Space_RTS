using Unity.Entities;
using Unity.Mathematics;

// Tag on weapon catalog entity.
public struct WeaponCatalogTag : IComponentData
{
}

// one element per WeaponProfileSO, index = profileId. lives only on catalog entity
[InternalBufferCapacity(16)]
public struct WeaponProfileBakeElement : IBufferElementData
{
    public byte requestKind;
    public byte firePattern;
    public byte payloadKind;
    public byte statusEffectKind;
    public float reloadTime;
    public float attackDistance;
    public float damageAmount;
    public float projectileSpeed;
    public float lifetime;
    public bool rotate;
    public bool limitRotation;
    public float rotationLimitAngle;
    public byte allowedTargets;
    public byte priorityTargets;
    public float burstInterval;
    public float spreadAngle;
    public float splashRadius;
    public float statusDuration;
    public float moveSpeedMultiplier;
    public float accelerationMultiplier;
    public bool disableWeapons;
    public bool turnAfterLaunch;
    public float rocketAcceleration;
    public float rocketLaunchScatterAngle;
    public float rocketLaunchScatterDuration;
    public float rocketLaunchScatterDistance;
    public float hitscanBeamWidth;
}

// Bake data for turret mount. Exists on turret entity before bootstrap.
public struct WeaponMountBakeData : IComponentData
{
    public Entity ammoEntity;
    public float baseLocalAngle;
}

public struct WeaponBulletSpawnPointBakeElement : IBufferElementData
{
    public float3 LocalPos;
}
