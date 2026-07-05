using Unity.Entities;

// shared move/combat stance rules for state changes, input and UI
public static class MoveModeCombatRules
{
    public static MoveMode GetNextMoveMode(MoveMode currentMode)
    {
        return currentMode switch
        {
            MoveMode.HoldPosition => MoveMode.MoveAndEngage,
            MoveMode.MoveAndEngage => MoveMode.AttackMove,
            MoveMode.AttackMove => MoveMode.HoldPosition,
            _ => MoveMode.MoveAndEngage,
        };
    }

    public static bool CanAutoEnterCombat(
        MoveMode moveMode,
        ShipState currentState,
        bool autoEngageAllowed,
        Entity forcedTarget,
        bool interruptMoveAndEngage = false)
    {
        if (forcedTarget != Entity.Null)
            return true;

        if (!autoEngageAllowed)
            return false;

        return moveMode switch
        {
            // HoldPosition: can shoot, but no auto combat movement
            MoveMode.HoldPosition => false,

            // MoveAndEngage: finish movement first, then combat movement
            MoveMode.MoveAndEngage =>
                currentState != ShipState.Following &&
                (interruptMoveAndEngage || currentState != ShipState.MovingToTarget),

            // AttackMove can enter combat on the way
            MoveMode.AttackMove =>
                currentState != ShipState.Following,

            _ => false,
        };
    }

    public static bool ShouldForceFormationMovement(MoveMode moveMode, ShipState currentState, bool reachedDesiredPosition)
    {
        // outside combat squad keeps members in formation slots
        if (currentState != ShipState.InCombat)
            return true;

        return moveMode switch
        {
            // hard hold: movement stays clamped to formation
            MoveMode.HoldPosition => true,

            // soft engage: formation controls ship before combat,
            // but don't pull back ship that is already InCombat
            MoveMode.MoveAndEngage => false,

            // attack move: after combat starts, don't pull member back until it ends
            MoveMode.AttackMove => false,

            _ => true,
        };
    }

    public static bool IsAggressiveMoveMode(MoveMode moveMode)
    {
        return moveMode == MoveMode.MoveAndEngage || moveMode == MoveMode.AttackMove;
    }
}
