using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public enum WeaponRequestKind : byte
{
    Ballistic = 0,
    Rocket = 1,
    Hitscan = 2,
}

public enum WeaponFirePattern : byte
{
    Single = 0,
    SequentialHardpoints = 1,
    SimultaneousHardpoints = 2,
}

public enum WeaponPayloadKind : byte
{
    Direct = 0,
    Splash = 1,
    DirectPlusSplash = 2,
}

public enum WeaponStatusEffectKind : byte
{
    None = 0,
    Emp = 1,
}

public struct WeaponHardpoint : IBufferElementData
{
    public float3 LocalPos;
}

public struct WeaponFireRequest : IComponentData
{
    public Entity mountEntity;
    public Entity ownerEntity;
    public Entity ammoEntity;
    public Entity targetEntity;
    public Faction targetFaction;
    public Faction ownerFaction;
    public bool ownerUsesFogOfWar;
    public int profileIndex;
    public byte requestKind;
    public float3 spawnPosition;
    public float2 direction;
    public float2 ownerVelocity;
    public float2 targetPosition;
    public uint randomSeed;
}

public struct WeaponPayloadRuntime : IComponentData
{
    public byte payloadKind;
    public float damageAmount;
    public float splashRadius;
    public Faction ownerFaction;

    public byte statusEffectKind;
    public float statusDuration;
    public float moveSpeedMultiplier;
    public float accelerationMultiplier;
    public bool disableWeapons;
}

public struct EmpStatus : IComponentData, IEnableableComponent
{
    public float timer;
    public float moveSpeedMultiplier;
    public float accelerationMultiplier;
    public bool disableWeapons;
}

public struct WeaponProfileDatabase : IComponentData
{
    public BlobAssetReference<WeaponProfileDatabaseBlob> Value;
}

public struct WeaponProfileBlob
{
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

    public byte requestKind;
    public byte firePattern;
    public byte payloadKind;
    public byte statusEffectKind;
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

public struct WeaponProfileDatabaseBlob
{
    public BlobArray<WeaponProfileBlob> Profiles;
}
