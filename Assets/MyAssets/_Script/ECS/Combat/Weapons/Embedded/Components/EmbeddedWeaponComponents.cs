using Unity.Entities;
using Unity.Mathematics;

public struct EmbeddedWeaponHost : IComponentData
{
}

public struct EmbeddedWeaponFireRuntime : IComponentData
{
    public float nextReadyFireTime;
    public byte hasDamageSlots;
}

public struct EmbeddedWeaponPressureRebuildRequest : IComponentData
{
}

public static class EmbeddedWeaponFireRuntimeUtility
{
    public const float NoReadyFireTime = 3.40282347e38f;

    public static EmbeddedWeaponFireRuntime Build(DynamicBuffer<EmbeddedWeaponSlot> slots)
    {
        EmbeddedWeaponFireRuntime runtime = new EmbeddedWeaponFireRuntime
        {
            nextReadyFireTime = NoReadyFireTime,
            hasDamageSlots = 0,
        };

        for (int i = 0; i < slots.Length; i++)
        {
            EmbeddedWeaponSlot slot = slots[i];
            if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
            {
                continue;
            }

            runtime.hasDamageSlots = 1;
            if (slot.targetEntity == Entity.Null)
            {
                continue;
            }

            float readyTime = math.max(slot.cooldownTimer, slot.patternTimer);
            runtime.nextReadyFireTime = math.min(runtime.nextReadyFireTime, readyTime);
        }

        return runtime;
    }
}

// Damage = projectile/hitscan pipeline. Support/Repair = action pipeline (repair, shield, EMP, buffs).
// Repair stays as alias for old prefabs.
public enum EmbeddedWeaponSlotRole : byte
{
    Damage = 0,
    Support = 1,
    Repair = Support,
}

// Flexible embedded action layer:
// Targeting -> Aim -> Delivery -> Effect.
public enum EmbeddedActionTargetFilter : byte
{
    Enemy = 0,
    AllyDamaged = 1,
    Self = 2,
    AllyAny = 3,
}

public enum EmbeddedActionDeliveryKind : byte
{
    WeaponProfile = 0, // Existing Ballistic/Rocket/Hitscan through WeaponProfileSO.
    BeamOverTime = 1, // Tick-based beam: damage, repair, EMP, buff, debuff.
    Aura = 2,         // Tick-based area effect around slot/ship.
    None = 3,
}

public enum EmbeddedActionEffectKind : byte
{
    Damage = 0,
    Repair = 1,
    ShieldRestore = 2,
    Emp = 3,
    Buff = 4,
    Debuff = 5,
}

[InternalBufferCapacity(4)]
public struct EmbeddedWeaponSlot : IBufferElementData
{
    public int profileIndex;
    public Entity ammoEntity;

    public float3 pivotLocalPosition;
    public float3 muzzleLocalOffset;
    public float baseLocalAngle;
    public float currentLocalAngle;

    public int firstHardpointIndex;
    public int hardpointCount;
    public int nextHardpointIndex;

    public float cooldownTimer;
    public float patternTimer;

    public Entity targetEntity;
    public float2 targetPositionWorld;

    public float findTargetTimer;

    public uint rngState;
    public uint shotCounter;

    public byte flags;
    public byte role;
}

[InternalBufferCapacity(4)]
public struct EmbeddedWeaponHardpoint : IBufferElementData
{
    public float3 muzzleLocalOffset;
}

[InternalBufferCapacity(4)]
public struct EmbeddedActionSlot : IBufferElementData
{
    public byte targetFilter;
    public byte deliveryKind;
    public byte effectKind;
    public byte flags;

    public float range;
    public float valuePerSecond;
    public float tickInterval;
    public float timer;

    public float searchInterval;
    public float searchTimer;

    public float rotateSpeed;

    // support beam aim throttle. aimTimer is absolute nextAimTime
    public float aimInterval;
    public float aimTimer;

    public Entity visualPrefabEntity;
    public float visualWidth;
    public float visualInterval;
    public float visualTimer;

    // extra params for ShieldRestore / EMP / Buff / Debuff
    public float maxStoredValue;
    public float statusDuration;
    public float moveSpeedMultiplier;
    public float accelerationMultiplier;
    public float effectMultiplier;
    public byte disableWeapons;

    // aura budget. 0 or negative = safe defaults
    public int maxTargetsPerTick;
    public int maxCellsPerTick;
    public int scanCursor;
}


public struct EmbeddedActionHostRuntime : IComponentData
{
    public float nextTargetWorkTime;
    public float nextBeamWorkTime;
    public float nextAuraWorkTime;
    public float nextVisualWorkTime;
    public byte hasBeamActions;
    public byte hasAuraActions;
}


public struct EmbeddedActionEffectFlushSingleton : IComponentData
{
}

[InternalBufferCapacity(64)]
public struct EmbeddedActionPendingEffect : IBufferElementData
{
    public Entity target;
    public EmbeddedActionEffectAccumulator effect;
}

[InternalBufferCapacity(32)]
public struct EmbeddedActionPendingEmpStatus : IBufferElementData
{
    public Entity target;
    public EmpStatus status;
}

[InternalBufferCapacity(32)]
public struct EmbeddedActionPendingBuffStatus : IBufferElementData
{
    public Entity target;
    public EmbeddedActionBuffStatus status;
}

[InternalBufferCapacity(32)]
public struct EmbeddedActionPendingDebuffStatus : IBufferElementData
{
    public Entity target;
    public EmbeddedActionDebuffStatus status;
}

public struct EmbeddedActionVisualInitialized : IComponentData
{
}

public struct EmbeddedActionRuntimeInitialized : IComponentData
{
}

public struct EmbeddedActionRuntimeDeadState : IComponentData
{
    public byte wasDead;
}

public struct EmbeddedActionVisualOwner : IComponentData
{
    public Entity owner;
    public int slotIndex;
}

public enum EmbeddedActionVisualKind : byte
{
    None = 0,
    Beam = 1,
    Aura = 2,
}

public static class EmbeddedActionVisualRuntimeFlags
{
    public const byte Visible = 1 << 0;
    public const byte Dirty = 1 << 1;
}

[InternalBufferCapacity(4)]
public struct EmbeddedActionVisualRuntime : IBufferElementData
{
    public Entity visualEntity;
    public float visibleUntil;
    public float2 startWorld;
    public float2 endWorld;
    public float range;
    public float width;
    public byte kind;
    public byte flags;
}

// runtime status from Buff effect. enableable to avoid Add/Remove churn
public struct EmbeddedActionBuffStatus : IComponentData, IEnableableComponent
{
    public float timer;
    public float effectMultiplier;
    public float moveSpeedMultiplier;
    public float accelerationMultiplier;
}

// runtime status from Debuff. can also refresh EmpStatus
public struct EmbeddedActionDebuffStatus : IComponentData, IEnableableComponent
{
    public float timer;
    public float effectMultiplier;
    public float moveSpeedMultiplier;
    public float accelerationMultiplier;
    public bool disableWeapons;
}

[InternalBufferCapacity(4)]
public struct EmbeddedWeaponVisualSlot : IBufferElementData
{
    public Entity visualEntity;
    public quaternion baseLocalRotation;
    public byte flags;
}

public static class EmbeddedWeaponSlotFlags
{
    public const byte Rotates = 1 << 0;
    public const byte LimitRotation = 1 << 1;
}

public static class EmbeddedActionSlotFlags
{
    public const byte CanTargetSelf = 1 << 0;
    public const byte Rotate = 1 << 1;
}

public static class EmbeddedWeaponVisualSlotFlags
{
    public const byte Rotate = 1 << 0;
}
