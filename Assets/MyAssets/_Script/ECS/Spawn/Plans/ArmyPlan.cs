using UnityEngine;

// spawn preset for StrikeGroup fleet. name stays ArmyPlan so old .asset refs work
[CreateAssetMenu(menuName = "Game/Army Plan", fileName = "ArmyPlan")]
public class ArmyPlan : ScriptableObject
{
    [Header("Spawn preset")]
    [Tooltip("Faction of all squads in this preset.")]
    public Faction faction = Faction.Enemy;

    [Header("Default StrikeGroup behavior")]
    public Tactics defaultTactics = Tactics.Neutral;
    public Stance defaultStance = Stance.HoldPosition;

    [Header("Initial ship state")]
    public ShipState initialState = ShipState.MovingToTarget;
    public MoveMode initialMoveMode = MoveMode.MoveAndEngage;
    public FireMode initialFireMode = FireMode.FireAtWill;

    [Header("Composition")]
    [Tooltip("Squads in this spawn preset. Each squad has its own offset from the spawn center.")]
    public ArmyPlanSquadEntry[] squads;
}

[System.Serializable]
public class ArmyPlanSquadEntry
{
    public string label;
    public Vector2 spawnOffset;
    public SquadPlan squadPlan;
}
