using System;
using Unity.Entities;
using UnityEngine;

public class CarrierHangarAuthoring : MonoBehaviour
{
    [Header("Carrier hangar")]
    public CarrierStance defaultStance = CarrierStance.AutoLaunch;
    public float launchInterval = 2f;
    public float launchDistance = 12f;
    public float recoveryRadius = 7f;

    [Header("Combat state")]
    public float combatExitDelay = 8f;

    [Header("Debug")]
    public bool enableLogs;
    public bool logLaunch = true;
    public bool logRecallReasons = true;
    public bool logRecovery = true;
    public bool logSlotStateChanges = true;

    [Header("Squadron loadout")]
    public CarrierSquadronTemplateAuthoring[] squadrons;

    [Serializable]
    public class CarrierSquadronTemplateAuthoring
    {
        [Header("Prefab / count")]
        public GameObject memberPrefab;
        public int memberPrefabIndex = 0;
        public int squadronsCount = 1;
        public int membersPerSquadron = 4;

        [Header("Formation")]
        public FormationType formation = FormationType.Wedge;
        public float formationSpacing = 4f;

        [Header("Behavior")]
        public int launchPriority = 0;
        public float endurance = 28f;
        public float serviceTime = 8f;
        public float rebuildTime = 18f;
        [Range(0.05f, 1f)] public float recallAtHealthFraction = 0.35f;
        public float leashDistance = 55f;

        [Header("Targeting")]
        public float targetSearchRange = 45f;
        public float targetSearchInterval = 0.35f;
        public ShipSize allowedTargets = ShipSize.Small | ShipSize.Medium | ShipSize.Big;
        public ShipSize priorityTargets = ShipSize.Small;

        [Header("Return")]
        public float outOfCombatReturnDelay = 4f;
    }

    class Baker : Baker<CarrierHangarAuthoring>
    {
        public override void Bake(CarrierHangarAuthoring authoring)
        {
            Entity carrierEntity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(carrierEntity, new CarrierTag());
            AddComponent(carrierEntity, new CarrierHangarState
            {
                stance = authoring.defaultStance,
                launchInterval = Mathf.Max(0.05f, authoring.launchInterval),
                launchTimer = 0f,
                launchDistance = Mathf.Max(0f, authoring.launchDistance),
                recoveryRadius = Mathf.Max(0.5f, authoring.recoveryRadius),
                inCombat = false,
                activeSquadrons = 0,
                rebuildingSlotIndex = -1,

                combatExitDelay = Mathf.Max(0f, authoring.combatExitDelay),
                combatExitTimer = 0f,
            });
            AddComponent(carrierEntity, new CarrierDebugSettings
            {
                enableLogs = authoring.enableLogs,
                logLaunch = authoring.logLaunch,
                logRecallReasons = authoring.logRecallReasons,
                logRecovery = authoring.logRecovery,
                logSlotStateChanges = authoring.logSlotStateChanges,
            });

            DynamicBuffer<CarrierSquadronTemplateElement> templates =
                AddBuffer<CarrierSquadronTemplateElement>(carrierEntity);

            DynamicBuffer<CarrierSquadronSlotElement> slots =
                AddBuffer<CarrierSquadronSlotElement>(carrierEntity);

            if (authoring.squadrons == null)
            {
                return;
            }

            for (int i = 0; i < authoring.squadrons.Length; i++)
            {
                CarrierSquadronTemplateAuthoring src = authoring.squadrons[i];
                if (src == null)
                {
                    continue;
                }

                Entity prefabEntity = src.memberPrefab != null
                    ? GetEntity(src.memberPrefab, TransformUsageFlags.Dynamic)
                    : Entity.Null;

                int templateIndex = templates.Length;

                templates.Add(new CarrierSquadronTemplateElement
                {
                    memberPrefab = prefabEntity,
                    memberPrefabIndex = src.memberPrefabIndex,
                    membersPerSquadron = Mathf.Max(1, src.membersPerSquadron),
                    formation = src.formation,
                    formationSpacing = Mathf.Max(0.1f, src.formationSpacing),
                    launchPriority = src.launchPriority,
                    endurance = Mathf.Max(1f, src.endurance),
                    serviceTime = Mathf.Max(0f, src.serviceTime),
                    rebuildTime = Mathf.Max(0f, src.rebuildTime),
                    recallAtHealthFraction = Mathf.Clamp01(src.recallAtHealthFraction),
                    leashDistance = Mathf.Max(1f, src.leashDistance),

                    targetSearchRange = Mathf.Max(1f, src.targetSearchRange),
                    targetSearchInterval = Mathf.Max(0.05f, src.targetSearchInterval),
                    allowedTargets = (byte)src.allowedTargets,
                    priorityTargets = (byte)src.priorityTargets,

                    outOfCombatReturnDelay = Mathf.Max(0f, src.outOfCombatReturnDelay),
                });

                int slotsCount = Mathf.Max(0, src.squadronsCount);
                for (int s = 0; s < slotsCount; s++)
                {
                    slots.Add(new CarrierSquadronSlotElement
                    {
                        templateIndex = templateIndex,
                        squadronEntity = Entity.Null,
                        state = prefabEntity == Entity.Null
                            ? CarrierSlotState.Disabled
                            : CarrierSlotState.Ready,
                        timer = 0f,
                    });
                }
            }
        }
    }
}
