using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class EmbeddedWeaponBatteryAuthoring : MonoBehaviour
{
    private void OnValidate()
    {
        int classicTurretCount = CountAuthoringByTypeName(transform, "TurretBaseAuthoring");
        int repairTurretCount = CountAuthoringByTypeName(transform, "RepairTurretAuthoring");

        if (classicTurretCount > 0 || repairTurretCount > 0)
        {
            Debug.LogError(
                $"Embedded weapon ship '{name}' still has classic turret authoring components: " +
                $"TurretBaseAuthoring={classicTurretCount}, RepairTurretAuthoring={repairTurretCount}. " +
                "Use EmbeddedWeaponSlotAuthoring only. Support slots must use EmbeddedActionProfileSO.",
                this);
        }
    }

    private static int CountAuthoringByTypeName(Transform root, string typeName)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        int count = 0;
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null && behaviour.GetType().Name == typeName)
            {
                count++;
            }
        }

        return count;
    }

    private class Baker : Baker<EmbeddedWeaponBatteryAuthoring>
    {
        public override void Bake(EmbeddedWeaponBatteryAuthoring authoring)
        {
            int classicTurretCount = CountAuthoringByTypeName(authoring.transform, "TurretBaseAuthoring");
            int repairTurretCount = CountAuthoringByTypeName(authoring.transform, "RepairTurretAuthoring");
            if (classicTurretCount > 0 || repairTurretCount > 0)
            {
                Debug.LogError(
                    $"[BakerEmbedded] '{authoring.name}': found classic turret authoring components together with EmbeddedWeaponBatteryAuthoring " +
                    $"(TurretBaseAuthoring={classicTurretCount}, RepairTurretAuthoring={repairTurretCount}). This is forbidden.",
                    authoring);
                return;
            }

            EmbeddedWeaponSlotAuthoring[] slotMbs = authoring.GetComponentsInChildren<EmbeddedWeaponSlotAuthoring>(true);
            if (slotMbs.Length == 0)
            {
                Debug.LogError(
                    $"[BakerEmbedded] '{authoring.name}': no EmbeddedWeaponSlotAuthoring children found. EmbeddedWeaponHost will not be baked without valid slots.",
                    authoring);
                return;
            }

            List<EmbeddedWeaponSlot> bakedSlots = new List<EmbeddedWeaponSlot>(slotMbs.Length);
            List<EmbeddedWeaponHardpoint> bakedHardpoints = new List<EmbeddedWeaponHardpoint>(slotMbs.Length);
            List<EmbeddedActionSlot> bakedActionSlots = new List<EmbeddedActionSlot>(slotMbs.Length);
            List<EmbeddedWeaponVisualSlot> bakedVisuals = new List<EmbeddedWeaponVisualSlot>(slotMbs.Length);
            List<Transform> muzzleSources = new List<Transform>(8);

            Quaternion rootRotationInv = Quaternion.Inverse(authoring.transform.rotation);

            for (int i = 0; i < slotMbs.Length; i++)
            {
                EmbeddedWeaponSlotAuthoring slotMb = slotMbs[i];
                if (!EmbeddedWeaponAuthoringValidationUtility.TryValidateSlot(slotMb, out string error))
                {
                    Debug.LogError($"[BakerEmbedded] Slot '{slotMb.name}': {error}", slotMb);
                    continue;
                }

                EmbeddedActionDeliveryKind deliveryKind = EmbeddedActionAuthoringUtility.ResolveDeliveryKind(slotMb);
                EmbeddedActionEffectKind effectKind = EmbeddedActionAuthoringUtility.ResolveEffectKind(slotMb);
                EmbeddedWeaponSlotRole resolvedRole = EmbeddedActionAuthoringUtility.ResolveRuntimeRole(slotMb);

                int profileId = -1;
                WeaponFirePattern firePattern = WeaponFirePattern.Single;
                Entity deliveryEntity = Entity.Null;
                WeaponProfileSO resolvedWeaponProfile = EmbeddedActionAuthoringUtility.ResolveWeaponProfile(slotMb);
                GameObject resolvedPrefab = EmbeddedActionAuthoringUtility.ResolveDeliveryPrefab(slotMb);

                if (deliveryKind == EmbeddedActionDeliveryKind.WeaponProfile && effectKind == EmbeddedActionEffectKind.Damage)
                {
#if UNITY_EDITOR
                    profileId = WeaponCatalogAsset.FindProfileIdInProject(resolvedWeaponProfile);
#endif
                    if (profileId < 0)
                    {
                        Debug.LogError($"[BakerEmbedded] Slot '{slotMb.name}': profile '{resolvedWeaponProfile.name}' was not found in any WeaponCatalogAsset.", slotMb);
                        continue;
                    }

                    firePattern = resolvedWeaponProfile.firePattern;
                    deliveryEntity = GetEntity(resolvedPrefab, TransformUsageFlags.Dynamic);
                    CollectDamageMuzzleSources(slotMb, firePattern, muzzleSources);
                }
                else
                {
                    if (resolvedPrefab != null)
                    {
                        deliveryEntity = GetEntity(resolvedPrefab, TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);
                    }

                    CollectSupportMuzzleSources(slotMb, muzzleSources);
                }

                if (muzzleSources.Count == 0)
                {
                    muzzleSources.Add(slotMb.transform);
                }

                // scaled prefabs: InverseTransformPoint divides by parent scale, don't use it
                // root scale is baked into offsets once
                Quaternion slotRotationInv = Quaternion.Inverse(slotMb.transform.rotation);
                Vector3 pivotLocalScaled = rootRotationInv * (slotMb.transform.position - authoring.transform.position);
                pivotLocalScaled.z = 0f; // 2D, marker Z ignored

                Vector3 slotForwardInRoot = rootRotationInv * (slotMb.transform.rotation * Vector3.up);
                float baseAngle = Mathf.Atan2(slotForwardInRoot.y, slotForwardInRoot.x) - Mathf.PI * 0.5f;

                int firstHardpointIndex = bakedHardpoints.Count;
                for (int hardpointIndex = 0; hardpointIndex < muzzleSources.Count; hardpointIndex++)
                {
                    Transform muzzleSource = muzzleSources[hardpointIndex];
                    Vector3 muzzleLocalToSlotScaled = slotRotationInv * (muzzleSource.position - slotMb.transform.position);
                    muzzleLocalToSlotScaled.z = 0f; // 2D, muzzle Z ignored

                    bakedHardpoints.Add(new EmbeddedWeaponHardpoint
                    {
                        muzzleLocalOffset = ToFloat3(muzzleLocalToSlotScaled),
                    });
                }

                float3 pivotLocal = ToFloat3(pivotLocalScaled);
                float3 firstMuzzleLocalToSlot = bakedHardpoints[firstHardpointIndex].muzzleLocalOffset;

                byte flags = 0;
                if (deliveryKind == EmbeddedActionDeliveryKind.WeaponProfile && resolvedWeaponProfile != null)
                {
                    if (resolvedWeaponProfile.rotate) { flags |= EmbeddedWeaponSlotFlags.Rotates; }
                    if (resolvedWeaponProfile.limitRotation) { flags |= EmbeddedWeaponSlotFlags.LimitRotation; }
                }
                else if (EmbeddedActionAuthoringUtility.ResolveRotate(slotMb))
                {
                    flags |= EmbeddedWeaponSlotFlags.Rotates;
                }

                Entity visualEntity = Entity.Null;
                byte visualFlags = 0;
                quaternion visualBaseLocalRotation = quaternion.identity;

                if (TryResolveVisualSlot(authoring, slotMb, out Transform visualRoot))
                {
                    visualEntity = GetEntity(visualRoot, TransformUsageFlags.Dynamic);
                    visualBaseLocalRotation = ToQuaternion(visualRoot.localRotation);

                    if (slotMb.rotateVisual)
                    {
                        visualFlags |= EmbeddedWeaponVisualSlotFlags.Rotate;
                    }
                }

                uint seed = math.hash(new int2(math.max(0, profileId), i)) | 1u;

                bakedSlots.Add(new EmbeddedWeaponSlot
                {
                    profileIndex = profileId,
                    ammoEntity = deliveryEntity,
                    pivotLocalPosition = pivotLocal,
                    muzzleLocalOffset = firstMuzzleLocalToSlot,
                    baseLocalAngle = baseAngle,
                    currentLocalAngle = baseAngle,
                    firstHardpointIndex = firstHardpointIndex,
                    hardpointCount = muzzleSources.Count,
                    nextHardpointIndex = 0,
                    cooldownTimer = 0f,
                    patternTimer = 0f,
                    targetEntity = Entity.Null,
                    targetPositionWorld = float2.zero,
                    findTargetTimer = 0f,
                    rngState = seed,
                    shotCounter = 0u,
                    flags = flags,
                    role = (byte)resolvedRole,
                });

                EmbeddedActionSlot actionSlot = BuildActionSlot(slotMb, deliveryEntity);
                bakedActionSlots.Add(actionSlot);

                bakedVisuals.Add(new EmbeddedWeaponVisualSlot
                {
                    visualEntity = visualEntity,
                    baseLocalRotation = visualBaseLocalRotation,
                    flags = visualFlags,
                });
            }

            if (bakedSlots.Count == 0)
            {
                Debug.LogError(
                    $"[BakerEmbedded] '{authoring.name}': all embedded slots failed validation. EmbeddedWeaponHost will not be baked.",
                    authoring);
                return;
            }

            Entity ship = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<EmbeddedWeaponHost>(ship);

            DynamicBuffer<EmbeddedWeaponSlot> slotBuffer = AddBuffer<EmbeddedWeaponSlot>(ship);
            for (int i = 0; i < bakedSlots.Count; i++)
            {
                slotBuffer.Add(bakedSlots[i]);
            }

            DynamicBuffer<EmbeddedWeaponHardpoint> hardpointBuffer = AddBuffer<EmbeddedWeaponHardpoint>(ship);
            for (int i = 0; i < bakedHardpoints.Count; i++)
            {
                hardpointBuffer.Add(bakedHardpoints[i]);
            }

            DynamicBuffer<EmbeddedActionSlot> actionBuffer = AddBuffer<EmbeddedActionSlot>(ship);
            for (int i = 0; i < bakedActionSlots.Count; i++)
            {
                actionBuffer.Add(bakedActionSlots[i]);
            }

            DynamicBuffer<EmbeddedWeaponVisualSlot> visualBuffer = AddBuffer<EmbeddedWeaponVisualSlot>(ship);
            for (int i = 0; i < bakedVisuals.Count; i++)
            {
                visualBuffer.Add(bakedVisuals[i]);
            }

            AddBuffer<EmbeddedActionVisualRuntime>(ship);
        }

        private EmbeddedActionSlot BuildActionSlot(EmbeddedWeaponSlotAuthoring slotMb, Entity deliveryEntity)
        {
            byte actionFlags = 0;
            if (EmbeddedActionAuthoringUtility.ResolveCanTargetSelf(slotMb))
            {
                actionFlags |= EmbeddedActionSlotFlags.CanTargetSelf;
            }

            if (EmbeddedActionAuthoringUtility.ResolveRotate(slotMb))
            {
                actionFlags |= EmbeddedActionSlotFlags.Rotate;
            }

            return new EmbeddedActionSlot
            {
                targetFilter = (byte)EmbeddedActionAuthoringUtility.ResolveTargetFilter(slotMb),
                deliveryKind = (byte)EmbeddedActionAuthoringUtility.ResolveDeliveryKind(slotMb),
                effectKind = (byte)EmbeddedActionAuthoringUtility.ResolveEffectKind(slotMb),
                flags = actionFlags,
                range = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveRange(slotMb)),
                valuePerSecond = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveValuePerSecond(slotMb)),
                tickInterval = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveTickInterval(slotMb)),
                timer = 0f,
                searchInterval = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveSearchInterval(slotMb)),
                searchTimer = 0f,
                rotateSpeed = math.max(0f, EmbeddedActionAuthoringUtility.ResolveRotateSpeed(slotMb)),
                aimInterval = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveAimInterval(slotMb)),
                aimTimer = 0f,
                visualPrefabEntity = deliveryEntity,
                visualWidth = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveBeamWidth(slotMb)),
                visualInterval = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveBeamVisualInterval(slotMb)),
                visualTimer = 0f,
                maxStoredValue = math.max(0f, EmbeddedActionAuthoringUtility.ResolveMaxStoredValue(slotMb)),
                statusDuration = math.max(0.01f, EmbeddedActionAuthoringUtility.ResolveStatusDuration(slotMb)),
                moveSpeedMultiplier = math.max(0f, EmbeddedActionAuthoringUtility.ResolveMoveSpeedMultiplier(slotMb)),
                accelerationMultiplier = math.max(0f, EmbeddedActionAuthoringUtility.ResolveAccelerationMultiplier(slotMb)),
                effectMultiplier = math.max(0f, EmbeddedActionAuthoringUtility.ResolveEffectMultiplier(slotMb)),
                disableWeapons = EmbeddedActionAuthoringUtility.ResolveDisableWeapons(slotMb) ? (byte)1 : (byte)0,
                maxTargetsPerTick = math.max(0, EmbeddedActionAuthoringUtility.ResolveMaxTargetsPerTick(slotMb)),
                maxCellsPerTick = math.max(0, EmbeddedActionAuthoringUtility.ResolveMaxCellsPerTick(slotMb)),
                scanCursor = 0,
            };
        }

        private static bool TryResolveVisualSlot(EmbeddedWeaponBatteryAuthoring battery, EmbeddedWeaponSlotAuthoring slotMb, out Transform visualRoot)
        {
            visualRoot = slotMb.visualRoot;
            if (visualRoot == null)
            {
                return false;
            }

            if (visualRoot == battery.transform)
            {
                Debug.LogError(
                    $"[BakerEmbedded] Slot '{slotMb.name}': visualRoot points to the ship root. This would rotate the whole ship. Leave Visual Root empty for built-in ship weapons, or assign a child turret/sprite object.",
                    slotMb);
                visualRoot = null;
                return false;
            }

            if (!visualRoot.IsChildOf(battery.transform))
            {
                Debug.LogError(
                    $"[BakerEmbedded] Slot '{slotMb.name}': visualRoot must be inside the same ship hierarchy.",
                    slotMb);
                visualRoot = null;
                return false;
            }

            return true;
        }

        private static void CollectDamageMuzzleSources(EmbeddedWeaponSlotAuthoring slotMb, WeaponFirePattern firePattern, List<Transform> result)
        {
            result.Clear();

            bool useAllMuzzles = firePattern == WeaponFirePattern.SequentialHardpoints
                              || firePattern == WeaponFirePattern.SimultaneousHardpoints;
            if (slotMb.muzzlePoints != null)
            {
                for (int i = 0; i < slotMb.muzzlePoints.Length; i++)
                {
                    Transform muzzle = slotMb.muzzlePoints[i];
                    if (muzzle == null)
                    {
                        continue;
                    }

                    result.Add(muzzle);
                    if (!useAllMuzzles)
                    {
                        return;
                    }
                }
            }

            if (result.Count == 0 && slotMb.muzzlePoint != null)
            {
                result.Add(slotMb.muzzlePoint);
            }

            if (result.Count == 0)
            {
                result.Add(slotMb.transform);
            }
        }

        private static void CollectSupportMuzzleSources(EmbeddedWeaponSlotAuthoring slotMb, List<Transform> result)
        {
            result.Clear();

            if (slotMb.muzzlePoints != null)
            {
                for (int i = 0; i < slotMb.muzzlePoints.Length; i++)
                {
                    Transform muzzle = slotMb.muzzlePoints[i];
                    if (muzzle != null)
                    {
                        result.Add(muzzle);
                    }
                }
            }

            if (result.Count == 0 && slotMb.muzzlePoint != null)
            {
                result.Add(slotMb.muzzlePoint);
            }

            if (result.Count == 0)
            {
                result.Add(slotMb.transform);
            }
        }

        private static float3 ToFloat3(Vector3 value)
        {
            // weapon logic is 2D, prefab Z is old layering hint, don't bake it
            return new float3(value.x, value.y, 0f);
        }

        private static quaternion ToQuaternion(Quaternion value)
        {
            return new quaternion(value.x, value.y, value.z, value.w);
        }
    }
}
