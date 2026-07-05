using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public abstract class MissionTrigger
{
    public abstract bool Evaluate(in MissionEvalContext ctx);
    public virtual void Reset(in MissionEvalContext ctx) { }
}

public struct MissionEvalContext
{
    public EntityManager em;
    public float missionTime;
    public float deltaTime;
    public Entity missionDirectorEntity;
}

[Serializable]
public class TriggerOnStart : MissionTrigger
{
    public override bool Evaluate(in MissionEvalContext ctx)
    {
        return ctx.missionTime <= ctx.deltaTime + 0.0001f;
    }
}

[Serializable]
public class TriggerAfterDelay : MissionTrigger
{
    public float delaySeconds = 5f;

    public override bool Evaluate(in MissionEvalContext ctx)
    {
        return ctx.missionTime >= delaySeconds;
    }
}

[Serializable]
public class TriggerGroupReadinessBelow : MissionTrigger
{
    public int groupId;
    public Faction faction;

    [Range(0f, 1f)]
    public float threshold = 0.5f;

    public override bool Evaluate(in MissionEvalContext ctx)
    {
        if (!StrikeGroupLookup.TryGetDirectorData(ctx.em, groupId, faction, ctx.missionDirectorEntity, out StrikeGroupData data))
            return false;

        return data.totalMemberCount > 0 && data.readiness < threshold;
    }
}

[Serializable]
public class TriggerGroupDestroyed : MissionTrigger
{
    public int groupId;
    public Faction faction;

    [NonSerialized]
    private bool hasEverExisted;

    public override bool Evaluate(in MissionEvalContext ctx)
    {
        if (StrikeGroupLookup.TryGetDirectorData(ctx.em, groupId, faction, ctx.missionDirectorEntity, out StrikeGroupData data))
        {
            hasEverExisted = true;
            return data.totalMemberCount > 0 && data.aliveMemberCount == 0;
        }

        return hasEverExisted;
    }

    public override void Reset(in MissionEvalContext ctx)
    {
        hasEverExisted = false;
    }
}

[Serializable]
public class TriggerWaypointReached : MissionTrigger
{
    public int waypointId;
    public int groupId;
    public Faction faction;
    public float arrivalRadius = 12f;

    public override bool Evaluate(in MissionEvalContext ctx)
    {
        if (!WaypointLookup.TryGetData(ctx.em, waypointId, out Waypoint waypoint))
            return false;

        if (!StrikeGroupLookup.TryGetDirectorData(ctx.em, groupId, faction, ctx.missionDirectorEntity, out StrikeGroupData data))
            return false;

        float2 diff = data.center - waypoint.position;
        return math.lengthsq(diff) <= arrivalRadius * arrivalRadius;
    }
}
