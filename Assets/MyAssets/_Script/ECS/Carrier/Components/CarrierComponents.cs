using Unity.Entities;

public struct CarrierTag : IComponentData { }

public struct CarrierHangarState : IComponentData
{
    public CarrierStance stance;

    public float launchInterval;
    public float launchTimer;

    public float launchDistance;
    public float recoveryRadius;

    public bool inCombat;
    public int activeSquadrons;

    public int rebuildingSlotIndex;

    public float combatExitDelay;
    public float combatExitTimer;
}

public struct CarrierDebugSettings : IComponentData
{
    public bool enableLogs;
    public bool logLaunch;
    public bool logRecallReasons;
    public bool logRecovery;
    public bool logSlotStateChanges;
}

public struct CarrierSquadronTemplateElement : IBufferElementData
{
    public Entity memberPrefab;
    public int memberPrefabIndex;

    public int membersPerSquadron;

    public FormationType formation;
    public float formationSpacing;

    public int launchPriority;

    public float endurance;
    public float serviceTime;
    public float rebuildTime;

    public float recallAtHealthFraction;
    public float leashDistance;

    public float targetSearchRange;
    public float targetSearchInterval;
    public byte allowedTargets;
    public byte priorityTargets;

    public float outOfCombatReturnDelay;
}

public struct CarrierSquadronSlotElement : IBufferElementData
{
    public int templateIndex;
    public Entity squadronEntity;
    public CarrierSlotState state;
    public float timer;
}
