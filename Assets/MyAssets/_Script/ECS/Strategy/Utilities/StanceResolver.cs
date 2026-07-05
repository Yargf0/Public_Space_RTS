using Unity.Entities;
using Unity.Mathematics;

public struct StanceResolution
{
    public float2 anchorPos;
    public Entity anchorEntity;
    public Entity priorityTarget;
    public MoveMode moveMode;
}

public static class StanceResolver
{
    public static StanceResolution Resolve(Stance stance, Entity targetEntity, float2 anchorPos)
    {
        switch (stance)
        {
            case Stance.MoveTo:
                return new StanceResolution
                {
                    anchorPos = anchorPos,
                    anchorEntity = Entity.Null,
                    priorityTarget = Entity.Null,
                    moveMode = MoveMode.MoveAndEngage,
                };

            case Stance.AttackMove:
                return new StanceResolution
                {
                    anchorPos = anchorPos,
                    anchorEntity = Entity.Null,
                    priorityTarget = targetEntity,
                    moveMode = MoveMode.AttackMove,
                };

            case Stance.Guard:
                return new StanceResolution
                {
                    anchorPos = anchorPos,
                    anchorEntity = targetEntity,
                    priorityTarget = Entity.Null,
                    moveMode = MoveMode.MoveAndEngage,
                };

            case Stance.HoldPosition:
                return new StanceResolution
                {
                    anchorPos = anchorPos,
                    anchorEntity = Entity.Null,
                    priorityTarget = Entity.Null,
                    moveMode = MoveMode.HoldPosition,
                };

            case Stance.Dock:
                return new StanceResolution
                {
                    anchorPos = anchorPos,
                    anchorEntity = targetEntity,
                    priorityTarget = Entity.Null,
                    moveMode = MoveMode.MoveAndEngage,
                };

            case Stance.Idle:
            default:
                return new StanceResolution
                {
                    anchorPos = anchorPos,
                    anchorEntity = Entity.Null,
                    priorityTarget = Entity.Null,
                    moveMode = MoveMode.HoldPosition,
                };
        }
    }
}
