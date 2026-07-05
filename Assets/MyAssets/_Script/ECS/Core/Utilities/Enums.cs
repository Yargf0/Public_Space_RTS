using System;
using UnityEngine;

public enum Faction
{
    Friendly,
    Enemy
}

[System.Flags]
public enum ShipSize : byte
{
    Small = 1,
    Medium = 2,
    Big = 4,
    RocketSmall = 8,
    RocketBig = 16,
}

public enum PathfindingSizeClass : byte
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

public enum ShipState : byte
{
    Idle,           // No order, ship is afk.
    MovingToTarget, // Ship moving to some point on map.
    InCombat,       // Ship in fight.
    GuardPosition,  // Ship is guarding some point in map.
    ReturnToGroup,  // Flying back to the squad.
    Following,      // Following a ship.
}

// Fight patterns. Don't reorder, prefabs store the value by index. Add new ones to the end.
public enum FightLogicType : byte
{
    // Keep preferred range: come closer if far, back off if too close.
    HoldDistance,

    // Fly circles around the target.
    Orbit,

    // Predict where target goes, attack on the pass, then fly away and repeat.
    InterceptorPass,

    // Approach, fire window, fly through, turn around, repeat.
    AttackRun,

    // Fly to launch range, launch, go away to reload, come back.
    MissileAttackRun,

    // Try to stay behind the target.
    Dogfight,

    // Group attack from different sides, based on formationSlotIndex.
    Swarm,

    // Straight side passes with turns, doesn't turn into orbit.
    Strafe,

    // Come to preferred range and stay, don't back away.
    CloseAndHold,
}

public enum ShipType
{
    Interceptor,
    Fighter,
    Corvet,
    Lincore,
    Carrier
}

public enum FireMode
{
    [InspectorName("Fire At Will")] FireAtWill,
    [InspectorName("Return Fire")] ReturnFire,
    [InspectorName("Hold Fire")] HoldFire,
}

public enum MoveMode : byte
{
    [InspectorName("Hold Position")] HoldPosition,
    [InspectorName("Move And Engage")] MoveAndEngage,
    [InspectorName("Attack Move")] AttackMove,
}

// Formation shapes for squads.
public enum FormationType : byte
{
    None,       // No formation, everyone flies as he wants.
    Wedge,      // V shape, leader in front.
    Line,       // Line, leader in center.
    Ring,       // Ring around the leader.
    Column,     // One after another behind the leader.
}

// Commands for the per-ship queue.
public enum CommandType : byte
{
    MoveTo,         // Move to point.
    AttackMove,     // A + click, move and attack on the way.
    AttackTarget,   // Attack one entity.
    Follow,         // Follow friendly ship.
    Stop,           // S, stop and clear orders.
}

// Carrier stuff.

public enum CarrierStance : byte
{
    AutoLaunch, // Squadrons launch when ready.
    HoldDeck,   // Squadrons wait on deck until player releases them.
    RecallAll,  // Everyone comes back to the carrier.
}

public enum CarrierSlotState : byte
{
    Ready,            // On deck, ready to launch.
    Launched,         // Out and flying.
    Returning,        // Flying back to carrier.
    Servicing,        // Repair / rearm on deck.
    QueuedForRebuild, // Lost, waits in rebuild queue.
    Rebuilding,       // New squadron is being built.
    Disabled,         // Slot not working (damage / no resources).
}

public enum RallyPointMode : byte
{
    None,
    FollowPoint,
    FollowEntity,
}

public enum ProducerEventType : byte
{
    QueueAdded,
    QueueRejected,
    Started,
    Completed,
}

public enum ProducerRejectReason : byte
{
    None,
    ShipNotFound,
    ShipNotAllowed,
    QueueFull,
    NotEnoughResources,
    ResourceStateNotFound,
    ProducerDisabled,
    ProducerDead,
    InvalidPrefab,
}

public enum AsteroidResourceType : byte
{
    Metal,
    Crystal,
}

public enum Tactics : byte
{
    [InspectorName("Evasive")]
    Evasive = 0,

    [InspectorName("Neutral")]
    Neutral = 1,

    [InspectorName("Aggressive")]
    Aggressive = 2,
}

public enum Stance : byte
{
    [InspectorName("Idle")]
    Idle = 0,

    [InspectorName("Move To")]
    MoveTo = 1,

    [InspectorName("Attack Move")]
    AttackMove = 2,

    [InspectorName("Guard")]
    Guard = 3,

    [InspectorName("Hold Position")]
    HoldPosition = 4,

    [InspectorName("Dock")]
    Dock = 5,
}
