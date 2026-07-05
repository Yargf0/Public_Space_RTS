using System;
using Unity.Entities;
using UnityEngine;

[Serializable]
public abstract class MissionAction
{
    public abstract void EmitCommand(in MissionExecContext ctx);
}

public struct MissionExecContext
{
    public EntityManager em;
    public Entity missionDirectorEntity;
    public float missionTime;

    // not null when commands come from MissionDirectorRunner. commands go through queue,
    // no direct CreateEntity from MonoBehaviour.Update
    public MissionCommandExecutionSystem commandSystem;

    public void EmitCommand(in MissionSpawnGroupCommand command)
    {
        if (commandSystem != null)
        {
            commandSystem.Enqueue(command);
            return;
        }

        Entity cmd = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionSpawnGroupCommand>());
        em.SetComponentData(cmd, command);
    }

    public void EmitCommand(in MissionOrderGroupCommand command)
    {
        if (commandSystem != null)
        {
            commandSystem.Enqueue(command);
            return;
        }

        Entity cmd = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionOrderGroupCommand>());
        em.SetComponentData(cmd, command);
    }

    public void EmitCommand(in MissionSetTacticsCommand command)
    {
        if (commandSystem != null)
        {
            commandSystem.Enqueue(command);
            return;
        }

        Entity cmd = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionSetTacticsCommand>());
        em.SetComponentData(cmd, command);
    }

    public void EmitCommand(in MissionAttackFactionCommand command)
    {
        if (commandSystem != null)
        {
            commandSystem.Enqueue(command);
            return;
        }

        Entity cmd = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionAttackFactionCommand>());
        em.SetComponentData(cmd, command);
    }

    public void EmitCommand(in MissionDestroyGroupCommand command)
    {
        if (commandSystem != null)
        {
            commandSystem.Enqueue(command);
            return;
        }

        Entity cmd = em.CreateEntity(
            ComponentType.ReadWrite<MissionCommandTag>(),
            ComponentType.ReadWrite<MissionDestroyGroupCommand>());
        em.SetComponentData(cmd, command);
    }
}

[Serializable]
public class ActionSpawnStrikeGroup : MissionAction
{
    public int spawnPresetIndex;

    public override void EmitCommand(in MissionExecContext ctx)
    {
        ctx.EmitCommand(new MissionSpawnGroupCommand
        {
            directorEntity = ctx.missionDirectorEntity,
            spawnPresetIndex = spawnPresetIndex,
        });
    }
}

[Serializable]
public class ActionOrderGroup : MissionAction
{
    public int groupId;
    public Faction faction;
    public Stance stance = Stance.AttackMove;
    public int targetWaypointId;
    public float radius = 24f;

    public override void EmitCommand(in MissionExecContext ctx)
    {
        ctx.EmitCommand(new MissionOrderGroupCommand
        {
            directorEntity = ctx.missionDirectorEntity,
            groupId = groupId,
            faction = faction,
            stance = stance,
            targetWaypointId = targetWaypointId,
            targetEntity = Entity.Null,
            radius = radius,
        });
    }
}

[Serializable]
public class ActionSetTactics : MissionAction
{
    public int groupId;
    public Faction faction;
    public Tactics tactics = Tactics.Neutral;

    public override void EmitCommand(in MissionExecContext ctx)
    {
        ctx.EmitCommand(new MissionSetTacticsCommand
        {
            directorEntity = ctx.missionDirectorEntity,
            groupId = groupId,
            faction = faction,
            tactics = tactics,
        });
    }
}

[Serializable]
public class ActionDestroyGroup : MissionAction
{
    public int groupId;
    public Faction faction;
    public DestroyGroupMode mode = DestroyGroupMode.DestroySquadsAndShips;

    public override void EmitCommand(in MissionExecContext ctx)
    {
        ctx.EmitCommand(new MissionDestroyGroupCommand
        {
            directorEntity = ctx.missionDirectorEntity,
            groupId = groupId,
            faction = faction,
            mode = mode,
        });
    }
}

[Serializable]
public class ActionLog : MissionAction
{
    public string message = "Mission event";

    public override void EmitCommand(in MissionExecContext ctx)
    {
        Debug.Log($"[Mission t={ctx.missionTime:F1}s] {message}");
    }
}
