public static class GameConstants
{
    public const float SmallGridCellSize = 6f;
    public const float BigGridCellSize = 30f;
    public const float ReachDistanceSq = 0.2f;
    public const int UnitsLayer = 6;


    public const float WorldPlaneZ = 0f;

    public const float ShipSmallZ = -5f;
    public const float ShipMediumZ = -3f;
    public const float ShipBigZ = -1f;

    public const float ShipDefaultZ = ShipSmallZ;
    public const float SquadAnchorZ = ShipDefaultZ;

    // Local Z offsets from the parent.
    public const float WeaponVisualLocalZFromParent = -1f;
    public const float EmbeddedWeaponVisualLocalZ = WeaponVisualLocalZFromParent;
    public const float ClassicWeaponMountLocalZ = WeaponVisualLocalZFromParent;
    public const float RepairTurretLocalZ = WeaponVisualLocalZFromParent;

    public const float SelectionLocalZOffset = -1f;

    public const float BuildingZ = 1f;
    public const float CommandLineZ = 5f;
    public const float ProjectileZ = -7f;
    public const float EffectsZ = -8f;
    public const float BuildPreviewZ = -10f;

    public const float ZLayerEpsilon = 0.0001f;

    // old names so old code compiles, new code should use GetShipZ()
    public const float SmallShipZ = ShipSmallZ;
    public const float MediumShipZ = ShipMediumZ;
    public const float BigShipZ = ShipBigZ;
    public const float ShipZ = ShipDefaultZ;
    public const float TurretParentLocalZ = WeaponVisualLocalZFromParent;

    public const float ReturnFireMemoryDuration = 3f;

    public const float DefaultFollowDistance = 18f;

    public const float LkpSearchDuration = 5f;
    public const float RocketLkpFreeFlightDuration = 0.5f;
    public const float RocketLkpArrivalDistance = 3f;
    public const float HitVisibilityRefreshDuration = 1.5f;

    public static float GetShipZ(byte shipSize)
    {
        ShipSize size = (ShipSize)shipSize;
        return GetShipZ(size);
    }

    public static float GetShipZ(ShipSize shipSize)
    {
        if ((shipSize & (ShipSize.Big | ShipSize.RocketBig)) != 0)
            return ShipBigZ;

        if ((shipSize & ShipSize.Medium) != 0)
            return ShipMediumZ;

        return ShipSmallZ;
    }
}
