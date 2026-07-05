using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedFindTargetSystem))]
[UpdateAfter(typeof(SearchlightSpotSystem))]
[UpdateAfter(typeof(MoveVelocitySystem))]
[UpdateAfter(typeof(RotateToMovementSystem))]
[UpdateBefore(typeof(EmbeddedWeaponFireRequestBuildSystem))]
public partial struct EmbeddedWeaponAimSystem : ISystem
{
    // 0.002 rad ~ 0.11 deg, not visible but way less buffer writes
    private const float AngleWriteEpsilon = 0.002f;
    private const float AimTicksPerSecond = 60f;
    private const uint AimStaggerBuckets = 2u;

    private ComponentLookup<Velocity> velocityLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private BufferLookup<EmbeddedWeaponSlot> slotsLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        velocityLookup = state.GetComponentLookup<Velocity>(true);
        localTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        slotsLookup = state.GetBufferLookup<EmbeddedWeaponSlot>(false);
        state.RequireForUpdate<WeaponProfileDatabase>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<WeaponProfileDatabase>(out WeaponProfileDatabase database))
        {
            return;
        }

        velocityLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        slotsLookup.Update(ref state);

        ref WeaponProfileDatabaseBlob root = ref database.Value.Value;
        uint aimTick = (uint)math.floor((float)SystemAPI.Time.ElapsedTime * AimTicksPerSecond);

        foreach ((RefRO<LocalTransform> shipLocal, Entity shipEntity)
            in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<EmbeddedWeaponHost>()
                .WithAll<EmbeddedWeaponSlot>()
                .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedWeaponSlot> slots = slotsLookup[shipEntity];
            LocalTransform shipTransform = shipLocal.ValueRO;
            quaternion shipRot = shipTransform.Rotation;
            quaternion shipRotInv = math.inverse(shipRot);
            float2 ownerVelocity = float2.zero;
            if (velocityLookup.TryGetComponent(shipEntity, out Velocity ownerVelocityComponent))
            {
                ownerVelocity = ownerVelocityComponent.velocity;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                EmbeddedWeaponSlot slot = slots[i];

                if ((EmbeddedWeaponSlotRole)slot.role != EmbeddedWeaponSlotRole.Damage)
                {
                    continue;
                }

                if ((uint)slot.profileIndex >= (uint)root.Profiles.Length)
                {
                    WriteBaseAngleIfChanged(slots, i, ref slot);
                    continue;
                }

                ref WeaponProfileBlob profile = ref root.Profiles[slot.profileIndex];
                WeaponRequestKind requestKind = (WeaponRequestKind)profile.requestKind;
                if (!IsSupportedEmbeddedRequestKind(requestKind) ||
                    !IsSupportedEmbeddedFirePattern((WeaponFirePattern)profile.firePattern) ||
                    !profile.rotate ||
                    (slot.flags & EmbeddedWeaponSlotFlags.Rotates) == 0 ||
                    slot.targetEntity == Entity.Null ||
                    !localTransformLookup.TryGetComponent(slot.targetEntity, out LocalTransform targetTransform))
                {
                    WriteBaseAngleIfChanged(slots, i, ref slot);
                    continue;
                }

                // Slots update in turns, not all in one frame.
                if (!ShouldUpdateAimThisTick(shipEntity, i, aimTick))
                {
                    continue;
                }

                quaternion currentSlotWorldRot = math.mul(shipRot, quaternion.RotateZ(slot.currentLocalAngle));
                float2 fallbackForward = math.normalizesafe(
                    math.mul(currentSlotWorldRot, new float3(0f, 1f, 0f)).xy,
                    new float2(0f, 1f));

                float3 pivotWorld = shipTransform.Position + math.rotate(shipRot, slot.pivotLocalPosition);
                float2 targetVelocity = float2.zero;
                if (velocityLookup.TryGetComponent(slot.targetEntity, out Velocity targetVelocityComponent))
                {
                    targetVelocity = targetVelocityComponent.velocity;
                }

                float3 targetWorld = targetTransform.Position;
                targetWorld.z = pivotWorld.z;

                float3 aimWorldPosition = requestKind == WeaponRequestKind.Hitscan
                    ? targetWorld
                    : WeaponAimUtility.ResolveProjectileAimPoint(
                        pivotWorld,
                        targetWorld,
                        ownerVelocity,
                        targetVelocity,
                        profile.projectileSpeed,
                        fallbackForward);

                float3 toAimWorld = aimWorldPosition - pivotWorld;
                toAimWorld.z = 0f;
                if (math.lengthsq(toAimWorld) < 0.0001f)
                {
                    continue;
                }

                float3 aimLocalDirection = math.rotate(shipRotInv, math.normalizesafe(toAimWorld, new float3(0f, 1f, 0f)));
                aimLocalDirection.z = 0f;
                if (math.lengthsq(aimLocalDirection) < 0.0001f)
                {
                    continue;
                }

                aimLocalDirection = math.normalizesafe(aimLocalDirection, new float3(0f, 1f, 0f));
                float desiredLocalAngle = math.atan2(aimLocalDirection.y, aimLocalDirection.x) - math.PI / 2f;

                if (CombatUtility.HasLimitedWeaponRotation(in profile))
                {
                    float maxDelta = math.radians(math.clamp(profile.rotationLimitAngle, 0f, 180f));
                    desiredLocalAngle = CombatUtility.ClampAngleAroundRad(desiredLocalAngle, slot.baseLocalAngle, maxDelta);
                }

                if (math.abs(CombatUtility.NormalizeAngleRad(desiredLocalAngle - slot.currentLocalAngle)) <= AngleWriteEpsilon)
                {
                    continue;
                }

                slot.currentLocalAngle = desiredLocalAngle;
                slots[i] = slot;
            }
        }
    }

    private static bool ShouldUpdateAimThisTick(Entity shipEntity, int slotIndex, uint aimTick)
    {
        uint hash = math.hash(new uint3(
            (uint)shipEntity.Index,
            (uint)shipEntity.Version,
            (uint)(slotIndex + 3571)));

        return ((aimTick + hash) % AimStaggerBuckets) == 0u;
    }

    private static void WriteBaseAngleIfChanged(DynamicBuffer<EmbeddedWeaponSlot> slots, int index, ref EmbeddedWeaponSlot slot)
    {
        if (math.abs(CombatUtility.NormalizeAngleRad(slot.baseLocalAngle - slot.currentLocalAngle)) <= AngleWriteEpsilon)
        {
            return;
        }

        slot.currentLocalAngle = slot.baseLocalAngle;
        slots[index] = slot;
    }

    private static bool IsSupportedEmbeddedRequestKind(WeaponRequestKind requestKind)
    {
        return requestKind == WeaponRequestKind.Ballistic
            || requestKind == WeaponRequestKind.Rocket
            || requestKind == WeaponRequestKind.Hitscan;
    }

    private static bool IsSupportedEmbeddedFirePattern(WeaponFirePattern firePattern)
    {
        return firePattern == WeaponFirePattern.Single
            || firePattern == WeaponFirePattern.SequentialHardpoints
            || firePattern == WeaponFirePattern.SimultaneousHardpoints;
    }
}
