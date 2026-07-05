using UnityEngine;

// flexible embedded slot profile: Targeting -> Aim -> Delivery -> Effect
[CreateAssetMenu(fileName = "EmbeddedActionProfile", menuName = "Weapon/Embedded Action Profile")]
public class EmbeddedActionProfileSO : ScriptableObject
{
    [Header("Targeting")]
    public EmbeddedActionTargetFilter targetFilter = EmbeddedActionTargetFilter.Enemy;
    public float range = 10f;
    public float searchInterval = 0.2f;
    public bool canTargetSelf = false;

    [Header("Aim")]
    public bool rotate = true;
    public float rotateSpeed = 8f;

    [Tooltip("For rotating BeamOverTime support slots: how often aim is updated. 0.033 = 30 Hz. Bigger = cheaper, less smooth.")]
    public float aimInterval = 1f / 30f;

    [Header("Delivery")]
    public EmbeddedActionDeliveryKind deliveryKind = EmbeddedActionDeliveryKind.WeaponProfile;

    [Tooltip("Used by Delivery = WeaponProfile. This keeps existing ballistic/rocket/hitscan data in WeaponProfileSO.")]
    public WeaponProfileSO weaponProfile;

    [Tooltip("Ammo/projectile/beam/aura prefab used by this action profile.")]
    public GameObject deliveryPrefab;

    [Tooltip("Used by BeamOverTime and Aura tick-based delivery types.")]
    public float tickInterval = 0.2f;

    [Tooltip("Used by BeamOverTime visuals. Also used as aura visual thickness.")]
    public float beamWidth = 0.35f;

    [Tooltip("Used by BeamOverTime/Aura visuals. How often a short beam/pulse visual is spawned while effect is active.")]
    public float beamVisualInterval = 0.05f;

    [Header("Effect")]
    public EmbeddedActionEffectKind effectKind = EmbeddedActionEffectKind.Damage;

    [Tooltip("Damage/repair/shield amount per second for tick-based effects. Status-only effects ignore this value. For WeaponProfile damage, WeaponProfileSO.damageAmount is still used.")]
    public float valuePerSecond = 10f;

    [Header("Shield")]
    [Tooltip("For Effect=ShieldRestore: max temporary shield stored as health above HealthMax. Existing damage code consumes it automatically because it subtracts Health.")]
    public float maxStoredValue = 100f;

    [Header("Status Effects")]
    [Tooltip("For EMP/Buff/Debuff: how long the status remains after each tick refresh.")]
    public float statusDuration = 1f;

    [Tooltip("For EMP/Debuff/Buff status data. 1 = unchanged, 0.5 = slow by 50%, 1.25 = boost by 25%.")]
    public float moveSpeedMultiplier = 1f;

    [Tooltip("For EMP/Debuff/Buff status data. 1 = unchanged.")]
    public float accelerationMultiplier = 1f;

    [Tooltip("For Buff/Debuff status data. Consumer systems may read this as generic strength/damage multiplier.")]
    public float effectMultiplier = 1f;

    [Tooltip("For EMP/Debuff: disables weapon fire while status is active when target has/receives EmpStatus.")]
    public bool disableWeapons = false;

    [Header("Performance")]
    [Tooltip("V6: ignored. Aura always processes all valid targets every tick for honest gameplay/visual consistency.")]
    public int maxTargetsPerTick = 0;

    [Tooltip("V6: ignored. Aura always scans the whole radius every tick for honest gameplay/visual consistency.")]
    public int maxCellsPerTick = 0;
}
