using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Mission Script", fileName = "MissionScript")]
public class MissionScript : ScriptableObject
{
    [Header("Metadata")]
    public string missionLabel = "Untitled Mission";

    [TextArea(2, 6)]
    public string description;

    [Header("Mission Spawn Presets")]
    public MissionSpawnPreset[] spawnPresets;

    [Header("Spawn Presets To Run On Start")]
    public int[] initialSpawnPresetIndexes;

    [Header("Mission Events")]
    public MissionEvent[] events;
}

[Serializable]
public class MissionSpawnPreset
{
    public string label;
    public ArmyPlan plan;
    public int spawnWaypointId;
    public int targetWaypointId;
    public int assignGroupId = 0;
    public Tactics tactics = Tactics.Neutral;
    public Stance stance = Stance.HoldPosition;
}

[Serializable]
public class MissionEvent
{
    public string label = "Event";
    public bool fireOnce = true;
    public float cooldownSeconds = 0f;

    [Header("Trigger")]
    public MissionTriggerConfig trigger = MissionTriggerConfig.AfterDelay(2f);

    [Header("Actions")]
    public MissionActionConfig[] actions;
}

public enum MissionTriggerKind : byte
{
    None = 0,
    OnStart = 1,
    AfterDelay = 2,
    GroupReadinessBelow = 3,
    GroupDestroyed = 4,
    WaypointReached = 5,
}

public enum MissionActionKind : byte
{
    None = 0,
    SpawnStrikeGroup = 1,
    OrderGroup = 2,
    SetTactics = 3,
    DestroyGroup = 4,
    Log = 5,
    AttackFactionOnce = 6,
    HuntFaction = 7,
}

[Serializable]
public struct MissionTriggerConfig
{
    public MissionTriggerKind kind;

    [Header("Delay")]
    public float delaySeconds;

    [Header("Group")]
    public int groupId;
    public Faction faction;

    [Header("Readiness")]
    [Range(0f, 1f)]
    public float threshold;

    [Header("Waypoint")]
    public int waypointId;
    public float arrivalRadius;

    public static MissionTriggerConfig AfterDelay(float seconds)
    {
        return new MissionTriggerConfig
        {
            kind = MissionTriggerKind.AfterDelay,
            delaySeconds = seconds,
            threshold = 0.5f,
            arrivalRadius = 12f,
        };
    }

    public bool Evaluate(in MissionEvalContext ctx, ref MissionEventState state)
    {
        switch (kind)
        {
            case MissionTriggerKind.OnStart:
                return ctx.missionTime <= ctx.deltaTime + 0.0001f;

            case MissionTriggerKind.AfterDelay:
                return ctx.missionTime >= delaySeconds;

            case MissionTriggerKind.GroupReadinessBelow:
                if (!StrikeGroupLookup.TryGetDirectorData(ctx.em, groupId, faction, ctx.missionDirectorEntity, out StrikeGroupData readinessData))
                    return false;

                return readinessData.totalMemberCount > 0 && readinessData.readiness < threshold;

            case MissionTriggerKind.GroupDestroyed:
                if (StrikeGroupLookup.TryGetDirectorData(ctx.em, groupId, faction, ctx.missionDirectorEntity, out StrikeGroupData destroyedData))
                {
                    state.triggerHasEverExisted = true;
                    return destroyedData.totalMemberCount > 0 && destroyedData.aliveMemberCount == 0;
                }

                return state.triggerHasEverExisted;

            case MissionTriggerKind.WaypointReached:
                if (!WaypointLookup.TryGetData(ctx.em, waypointId, out Waypoint waypoint))
                    return false;

                if (!StrikeGroupLookup.TryGetDirectorData(ctx.em, groupId, faction, ctx.missionDirectorEntity, out StrikeGroupData waypointData))
                    return false;

                float radius = arrivalRadius > 0f ? arrivalRadius : 12f;
                float2 diff = waypointData.center - waypoint.position;
                return math.lengthsq(diff) <= radius * radius;

            case MissionTriggerKind.None:
            default:
                return false;
        }
    }
}

[Serializable]
public struct MissionActionConfig
{
    public MissionActionKind kind;

    [Header("Spawn")]
    public int spawnPresetIndex;

    [Header("Group")]
    public int groupId;
    public Faction faction;

    [Header("Faction Targeting")]
    public Faction targetFaction;

    [Header("Order")]
    public Stance stance;
    public int targetWaypointId;
    public float radius;

    [Header("Tactics")]
    public Tactics tactics;

    [Header("Destroy")]
    public DestroyGroupMode destroyMode;

    [Header("Log")]
    public string message;

    public static MissionActionConfig OrderGroup(int groupId, Faction faction, int waypointId)
    {
        return new MissionActionConfig
        {
            kind = MissionActionKind.OrderGroup,
            groupId = groupId,
            faction = faction,
            stance = Stance.AttackMove,
            targetWaypointId = waypointId,
            radius = 24f,
            tactics = Tactics.Neutral,
            destroyMode = DestroyGroupMode.DestroySquadsAndShips,
        };
    }

    public void EmitCommand(in MissionExecContext ctx)
    {
        switch (kind)
        {
            case MissionActionKind.SpawnStrikeGroup:
            {
                ctx.EmitCommand(new MissionSpawnGroupCommand
                {
                    directorEntity = ctx.missionDirectorEntity,
                    spawnPresetIndex = spawnPresetIndex,
                });
                break;
            }

            case MissionActionKind.OrderGroup:
            {
                ctx.EmitCommand(new MissionOrderGroupCommand
                {
                    directorEntity = ctx.missionDirectorEntity,
                    groupId = groupId,
                    faction = faction,
                    stance = stance,
                    targetWaypointId = targetWaypointId,
                    targetEntity = Entity.Null,
                    radius = radius > 0f ? radius : 24f,
                });
                break;
            }

            case MissionActionKind.SetTactics:
            {
                ctx.EmitCommand(new MissionSetTacticsCommand
                {
                    directorEntity = ctx.missionDirectorEntity,
                    groupId = groupId,
                    faction = faction,
                    tactics = tactics,
                });
                break;
            }

            case MissionActionKind.AttackFactionOnce:
            case MissionActionKind.HuntFaction:
            {
                ctx.EmitCommand(new MissionAttackFactionCommand
                {
                    directorEntity = ctx.missionDirectorEntity,
                    groupId = groupId,
                    faction = faction,
                    targetFaction = targetFaction,
                    stance = stance == Stance.Idle ? Stance.AttackMove : stance,
                    radius = radius > 0f ? radius : 24f,
                });
                break;
            }

            case MissionActionKind.DestroyGroup:
            {
                ctx.EmitCommand(new MissionDestroyGroupCommand
                {
                    directorEntity = ctx.missionDirectorEntity,
                    groupId = groupId,
                    faction = faction,
                    mode = destroyMode,
                });
                break;
            }

            case MissionActionKind.Log:
                Debug.Log($"[Mission t={ctx.missionTime:F1}s] {message}");
                break;

            case MissionActionKind.None:
            default:
                break;
        }
    }
}
