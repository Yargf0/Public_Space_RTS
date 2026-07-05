using Unity.Entities;

// marker baked from ShipBaseAuthoring, flags pick what to draw for this ship
public struct FightPatternDebugMarker : IComponentData
{
    public byte DrawPattern;
    public byte DrawHud;

    public byte HudShowShipSize;
    public byte HudShowShipState;
    public byte HudShowMoveMode;
    public byte HudShowFireMode;
    public byte HudShowFightPattern;
    public byte HudShowFightPhase;
    public byte HudShowSquad;
    public byte HudShowTarget;

    public byte DrawSwarmSectors;
    public byte DrawAllSwarmSectors;
    public byte DrawSwarmSlotPoint;
}
