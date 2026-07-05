using UnityEngine;
using UnityEngine.Serialization;

// source of truth for weapon settings. id = index in catalog, no manual profileId
[CreateAssetMenu(fileName = "WeaponProfile", menuName = "Weapon/Weapon Profile")]
public class WeaponProfileSO : ScriptableObject
{
    [Header("Core")]
    public WeaponRequestKind requestKind = WeaponRequestKind.Ballistic;
    public WeaponFirePattern firePattern = WeaponFirePattern.Single;
    public WeaponPayloadKind payloadKind = WeaponPayloadKind.Direct;
    public WeaponStatusEffectKind statusEffectKind = WeaponStatusEffectKind.None;

    public float reloadTime = 1f;
    public float attackDistance = 10f;
    public float damageAmount = 1f;
    public float projectileSpeed = 10f;
    public float lifetime = 3f;
    public bool rotate = true;

    [Header("Rotation Sector")]
    [Tooltip("If enabled, this rotating weapon can turn only within +/- Rotation Limit Angle around its default local forward direction. 180 means unrestricted rotation.")]
    public bool limitRotation = false;

    [Range(0f, 180f)]
    [Tooltip("Half-angle of the allowed rotation sector in degrees. Example: 15 means the weapon can rotate 15 degrees left and 15 degrees right from its default direction.")]
    public float rotationLimitAngle = 45f;

    [Header("Targeting")]
    public ShipSize allowedTargets = ShipSize.Small | ShipSize.Medium | ShipSize.Big | ShipSize.RocketSmall | ShipSize.RocketBig;
    public ShipSize priorityTargets = ShipSize.Small;

    [Header("Pattern / Special")]
    public float burstInterval = 0f;

    [FormerlySerializedAs("hitscanSpreadAngle")]
    public float spreadAngle = 0f;

    [FormerlySerializedAs("rocketSplashRadius")]
    public float splashRadius = 0f;

    [Header("Status Effect")]
    public float statusDuration = 0f;
    public float moveSpeedMultiplier = 1f;
    public float accelerationMultiplier = 1f;
    public bool disableWeapons = false;

    [Header("Rocket / Torpedo")]
    public float rocketAcceleration = 0f;

    [Tooltip("If false, projectile flies straight after launch and no longer turns to target. Disable for torpedoes.")]
    public bool turnAfterLaunch = true;

    [Header("Rocket Launch Scatter")]
    [Tooltip("Start-only rocket fan angle in degrees. This is not weapon accuracy spread. Use large values for missile volley visuals.")]
    public float rocketLaunchScatterAngle = 0f;

    [Tooltip("How long the rocket keeps the start scatter direction before normal homing starts.")]
    public float rocketLaunchScatterDuration = 0f;

    [Tooltip("Optional max distance for the start scatter phase. If <= 0, only duration is used.")]
    public float rocketLaunchScatterDistance = 0f;

    public float hitscanBeamWidth = 1f;
}
