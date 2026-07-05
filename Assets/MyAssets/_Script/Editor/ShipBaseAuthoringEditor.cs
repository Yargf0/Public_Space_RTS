using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ShipBaseAuthoring))]
[CanEditMultipleObjects]
public class ShipBaseAuthoringEditor : Editor
{
    private static bool showMovement = true;
    private static bool showIdentity = true;
    private static bool showHealth = true;
    private static bool showBoid = false;
    private static bool showCombat = true;
    private static bool showPatterns = true;
    private static bool showVisibility = false;
    private static bool showCollision = false;
    private static bool showAgro = false;
    private static bool showAiDemoDebug = false;

    private SerializedProperty followDistance;
    private SerializedProperty maxSpeed;
    private SerializedProperty acceleration;
    private SerializedProperty rotationSpeed;

    private SerializedProperty Faction;
    private SerializedProperty ShipSize;
    private SerializedProperty ShipType;
    private SerializedProperty shipId;

    private SerializedProperty health;
    private SerializedProperty healthMax;
    private SerializedProperty destroyOnZeroHealth;

    private SerializedProperty separation;
    private SerializedProperty alignment;
    private SerializedProperty cohesion;
    private SerializedProperty flowFieldWeight;
    private SerializedProperty neighborRadius;

    private SerializedProperty initialFireMode;
    private SerializedProperty initialMoveMode;
    private SerializedProperty disableWeaponTargetDistribution;

    private SerializedProperty FightLogic;
    private SerializedProperty orbitDirection;
    private SerializedProperty idealDistance;

    private SerializedProperty TargetSizeMater;
    private SerializedProperty smallTarget;
    private SerializedProperty mediumTarget;
    private SerializedProperty bigTarget;

    private SerializedProperty smallRangeX;
    private SerializedProperty mediumRangeX;
    private SerializedProperty bigRangeX;

    private SerializedProperty tooCloseDist;
    private SerializedProperty tooFarDist;
    private SerializedProperty DriftSpeed;
    private SerializedProperty DriftPeriod;

    private SerializedProperty orbitJitter;
    private SerializedProperty JitterPeriod;
    private SerializedProperty SpeedJitter;

    private SerializedProperty attackRunFireRange;
    private SerializedProperty runFireTime;
    private SerializedProperty runBreakTime;
    private SerializedProperty runRepositionTime;
    private SerializedProperty runSpread;
    private SerializedProperty runRepositionDist;

    private SerializedProperty strafeLength;
    private SerializedProperty strafeMinRange;

    private SerializedProperty spottedTimer;
    private SerializedProperty useFogOfWar;
    private SerializedProperty addSelfSearchlight;
    private SerializedProperty searchlightRange;
    private SerializedProperty searchlightConeAngle;
    private SerializedProperty searchlightOpacity;
    private SerializedProperty searchlightKeepVisibleSeconds;
    private SerializedProperty searchlightScanInterval;
    private SerializedProperty drawSearchlightGizmo;
    private SerializedProperty searchlightGizmoColor;

    private SerializedProperty collisionRadius;
    private SerializedProperty drawAlways;
    private SerializedProperty drawOnlySelected;
    private SerializedProperty gizmoColor;

    private SerializedProperty agroDetectionTime;
    private SerializedProperty agroNeedDistance;
    private SerializedProperty agroDetectionRadius;

    private SerializedProperty drawFightPatternDebug;
    private SerializedProperty drawFightPatternHud;
    private SerializedProperty hudShowShipSize;
    private SerializedProperty hudShowShipState;
    private SerializedProperty hudShowMoveMode;
    private SerializedProperty hudShowFireMode;
    private SerializedProperty hudShowFightPattern;
    private SerializedProperty hudShowFightPhase;
    private SerializedProperty hudShowSquad;
    private SerializedProperty hudShowTarget;
    private SerializedProperty debugDrawSwarmSectors;
    private SerializedProperty debugDrawAllSwarmSectors;
    private SerializedProperty debugDrawSwarmSlotPoint;

    private void OnEnable()
    {
        followDistance = serializedObject.FindProperty("followDistance");
        maxSpeed = serializedObject.FindProperty("maxSpeed");
        acceleration = serializedObject.FindProperty("acceleration");
        rotationSpeed = serializedObject.FindProperty("rotationSpeed");

        Faction = serializedObject.FindProperty("Faction");
        ShipSize = serializedObject.FindProperty("ShipSize");
        ShipType = serializedObject.FindProperty("ShipType");
        shipId = serializedObject.FindProperty("shipId");

        health = serializedObject.FindProperty("health");
        healthMax = serializedObject.FindProperty("healthMax");
        destroyOnZeroHealth = serializedObject.FindProperty("destroyOnZeroHealth");

        separation = serializedObject.FindProperty("separation");
        alignment = serializedObject.FindProperty("alignment");
        cohesion = serializedObject.FindProperty("cohesion");
        flowFieldWeight = serializedObject.FindProperty("flowFieldWeight");
        neighborRadius = serializedObject.FindProperty("neighborRadius");

        initialFireMode = serializedObject.FindProperty("initialFireMode");
        initialMoveMode = serializedObject.FindProperty("initialMoveMode");
        disableWeaponTargetDistribution = serializedObject.FindProperty("disableWeaponTargetDistribution");

        FightLogic = serializedObject.FindProperty("FightLogicType");
        orbitDirection = serializedObject.FindProperty("orbitDirection");
        idealDistance = serializedObject.FindProperty("idealDistance");

        TargetSizeMater = serializedObject.FindProperty("TargetSizeMater");
        smallTarget = serializedObject.FindProperty("smallTarget");
        mediumTarget = serializedObject.FindProperty("mediumTarget");
        bigTarget = serializedObject.FindProperty("bigTarget");

        smallRangeX = serializedObject.FindProperty("smallRangeX");
        mediumRangeX = serializedObject.FindProperty("mediumRangeX");
        bigRangeX = serializedObject.FindProperty("bigRangeX");

        tooCloseDist = serializedObject.FindProperty("tooCloseDist");
        tooFarDist = serializedObject.FindProperty("tooFarDist");
        DriftSpeed = serializedObject.FindProperty("DriftSpeed");
        DriftPeriod = serializedObject.FindProperty("DriftPeriod");

        orbitJitter = serializedObject.FindProperty("orbitJitter");
        JitterPeriod = serializedObject.FindProperty("JitterPeriod");
        SpeedJitter = serializedObject.FindProperty("SpeedJitter");

        attackRunFireRange = serializedObject.FindProperty("attackRunFireRange");
        runFireTime = serializedObject.FindProperty("runFireTime");
        runBreakTime = serializedObject.FindProperty("runBreakTime");
        runRepositionTime = serializedObject.FindProperty("runRepositionTime");
        runSpread = serializedObject.FindProperty("runSpread");
        runRepositionDist = serializedObject.FindProperty("runRepositionDist");

        strafeLength = serializedObject.FindProperty("strafeLength");
        strafeMinRange = serializedObject.FindProperty("strafeMinRange");

        spottedTimer = serializedObject.FindProperty("spottedTimer");
        useFogOfWar = serializedObject.FindProperty("useFogOfWar");
        addSelfSearchlight = serializedObject.FindProperty("addSelfSearchlight");
        searchlightRange = serializedObject.FindProperty("searchlightRange");
        searchlightConeAngle = serializedObject.FindProperty("searchlightConeAngle");
        searchlightOpacity = serializedObject.FindProperty("searchlightOpacity");
        searchlightKeepVisibleSeconds = serializedObject.FindProperty("searchlightKeepVisibleSeconds");
        searchlightScanInterval = serializedObject.FindProperty("searchlightScanInterval");
        drawSearchlightGizmo = serializedObject.FindProperty("drawSearchlightGizmo");
        searchlightGizmoColor = serializedObject.FindProperty("searchlightGizmoColor");

        collisionRadius = serializedObject.FindProperty("collisionRadius");
        drawAlways = serializedObject.FindProperty("drawAlways");
        drawOnlySelected = serializedObject.FindProperty("drawOnlySelected");
        gizmoColor = serializedObject.FindProperty("gizmoColor");

        agroDetectionTime = serializedObject.FindProperty("agroDetectionTime");
        agroNeedDistance = serializedObject.FindProperty("agroNeedDistance");
        agroDetectionRadius = serializedObject.FindProperty("agroDetectionRadius");

        drawFightPatternDebug = serializedObject.FindProperty("drawFightPatternDebug");
        drawFightPatternHud = serializedObject.FindProperty("drawFightPatternHud");
        hudShowShipSize = serializedObject.FindProperty("hudShowShipSize");
        hudShowShipState = serializedObject.FindProperty("hudShowShipState");
        hudShowMoveMode = serializedObject.FindProperty("hudShowMoveMode");
        hudShowFireMode = serializedObject.FindProperty("hudShowFireMode");
        hudShowFightPattern = serializedObject.FindProperty("hudShowFightPattern");
        hudShowFightPhase = serializedObject.FindProperty("hudShowFightPhase");
        hudShowSquad = serializedObject.FindProperty("hudShowSquad");
        hudShowTarget = serializedObject.FindProperty("hudShowTarget");
        debugDrawSwarmSectors = serializedObject.FindProperty("debugDrawSwarmSectors");
        debugDrawAllSwarmSectors = serializedObject.FindProperty("debugDrawAllSwarmSectors");
        debugDrawSwarmSlotPoint = serializedObject.FindProperty("debugDrawSwarmSlotPoint");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawMovement();
        DrawIdentity();
        DrawHealth();
        DrawBoid();
        DrawCombat();
        DrawPatternSettings();
        DrawVisibility();
        DrawCollision();
        DrawAgro();
        DrawAiDemoDebug();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawMovement()
    {
        if (!Foldout(ref showMovement, "Movement")) return;

        Field(followDistance, "Follow Distance");
        Field(maxSpeed, "Max Speed");
        Field(acceleration, "Acceleration");
        Field(rotationSpeed, "Rotation Speed");

        EndSection();
    }

    private void DrawIdentity()
    {
        if (!Foldout(ref showIdentity, "Identity")) return;

        Field(Faction);
        Field(ShipSize);
        Field(ShipType);
        Field(shipId, "Ship Id");

        EndSection();
    }

    private void DrawHealth()
    {
        if (!Foldout(ref showHealth, "Health")) return;

        Field(health);
        Field(healthMax, "Health Max");
        Field(destroyOnZeroHealth);

        EndSection();
    }

    private void DrawBoid()
    {
        if (!Foldout(ref showBoid, "Boid")) return;

        Field(separation);
        Field(alignment);
        Field(cohesion);
        Field(flowFieldWeight);
        Field(neighborRadius);

        EndSection();
    }

    private void DrawCombat()
    {
        if (!Foldout(ref showCombat, "Combat")) return;

        EditorGUILayout.LabelField("State Defaults", EditorStyles.boldLabel);
        Field(initialFireMode, "Fire Mode");
        Field(initialMoveMode, "Move Mode");

        Space();

        EditorGUILayout.LabelField("Weapon Targeting", EditorStyles.boldLabel);
        Field(disableWeaponTargetDistribution, "Disable Target Distribution");
        if (disableWeaponTargetDistribution.boolValue)
        {
            EditorGUILayout.HelpBox("Weapons on this ship ignore target pressure. They may all focus one target.", MessageType.Info);
        }

        Space();

        EditorGUILayout.LabelField("Main Pattern", EditorStyles.boldLabel);
        Field(FightLogic, "Default Pattern");

        if (UsesOrbitDirection())
        {
            Field(orbitDirection, "Orbit Direction");
        }

        Field(idealDistance, "Base Distance");

        Space();

        Field(TargetSizeMater, "Use Target Size Patterns");

        if (TargetSizeMater.boolValue)
        {
            EditorGUI.indentLevel++;

            Field(smallTarget, "Small Target");
            Field(mediumTarget, "Medium Target");
            Field(bigTarget, "Big Target");

            Space();

            EditorGUILayout.LabelField("Distance Multipliers", EditorStyles.boldLabel);
            Field(smallRangeX, "Small x");
            Field(mediumRangeX, "Medium x");
            Field(bigRangeX, "Big x");

            EditorGUI.indentLevel--;
        }

        EndSection();
    }

    private void DrawPatternSettings()
    {
        if (!Foldout(ref showPatterns, "Pattern Settings")) return;

        bool anySpecificPatternVisible = false;

        if (UsesHoldDistance())
        {
            anySpecificPatternVisible = true;

            EditorGUILayout.LabelField("Hold Distance", EditorStyles.boldLabel);
            Field(tooCloseDist, "Too Close x");
            Field(tooFarDist, "Too Far x");
            Field(DriftSpeed, "Drift Speed");
            Field(DriftPeriod, "Drift Period");
            Space();
        }

        if (UsesCloseAndHold())
        {
            anySpecificPatternVisible = true;

            EditorGUILayout.LabelField("Close And Hold", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Uses Base Distance only: approaches if target is farther, stops if target is at or inside Base Distance. Does not retreat when target gets closer.", MessageType.Info);
            Space();
        }

        if (UsesOrbit())
        {
            anySpecificPatternVisible = true;

            EditorGUILayout.LabelField("Orbit", EditorStyles.boldLabel);
            Field(orbitJitter, "Radius Jitter");
            Field(JitterPeriod, "Jitter Period");
            Field(SpeedJitter, "Speed Jitter");
            Space();
        }

        if (UsesAttackRun())
        {
            anySpecificPatternVisible = true;

            EditorGUILayout.LabelField("Attack Run", EditorStyles.boldLabel);
            Field(attackRunFireRange, "Fire Range x");
            Field(runFireTime, "Fire Time");
            Field(runBreakTime, "Break Time");
            Field(runRepositionTime, "Reposition Time");
            Field(runSpread, "Spread Deg");
            Field(runRepositionDist, "Reposition Dist x");
            Space();
        }

        if (!anySpecificPatternVisible)
        {
            EditorGUILayout.HelpBox("Selected combat pattern has no extra tuning fields here.", MessageType.Info);
        }

        EndSection();
    }

    private void DrawVisibility()
    {
        if (!Foldout(ref showVisibility, "Visibility / Fog Of War")) return;

        Field(spottedTimer);
        Field(useFogOfWar, "Use Fog Of War");

        Space();
        EditorGUILayout.LabelField("Self Searchlight", EditorStyles.boldLabel);
        Field(addSelfSearchlight, "Add Self Searchlight");

        if (addSelfSearchlight.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Bakes gameplay Searchlight + SearchlightState onto the ship. The fog-cut visual is created automatically at runtime.", MessageType.Info);
            Field(searchlightRange, "Range");
            Field(searchlightConeAngle, "Cone Angle");
            Field(searchlightOpacity, "Opacity");
            Field(searchlightKeepVisibleSeconds, "Keep Visible Seconds");
            Field(searchlightScanInterval, "Scan Interval");
            Field(drawSearchlightGizmo, "Draw Searchlight Gizmo");
            if (drawSearchlightGizmo.boolValue)
            {
                Field(searchlightGizmoColor, "Gizmo Color");
            }

            ShipBaseAuthoring ship = target as ShipBaseAuthoring;
            if (ship != null)
            {
                EditorGUILayout.LabelField("Observer Faction", ship.Faction.ToString());
                EditorGUILayout.LabelField("Scans Faction", VisibilityUtility.Opposite(ship.Faction).ToString());
            }

            EditorGUI.indentLevel--;
        }

        EndSection();
    }

    private void DrawCollision()
    {
        if (!Foldout(ref showCollision, "Collision / Gizmos")) return;

        Field(collisionRadius);
        Field(drawAlways);
        Field(drawOnlySelected);

        if (drawAlways.boolValue || drawOnlySelected.boolValue)
        {
            Field(gizmoColor);
        }

        EndSection();
    }

    private void DrawAgro()
    {
        if (!Foldout(ref showAgro, "Agro")) return;

        Field(agroDetectionTime, "Detection Time");
        Field(agroNeedDistance, "Need Distance");

        if (agroNeedDistance.boolValue)
        {
            Field(agroDetectionRadius, "Detection Radius");
        }

        EndSection();
    }

    private void DrawAiDemoDebug()
    {
        if (!Foldout(ref showAiDemoDebug, "AI Demo Debug")) return;

        EditorGUILayout.HelpBox("These toggles bake FightPatternDebugMarker and are used by AiTest visualization systems.", MessageType.Info);
        Field(drawFightPatternDebug, "Draw Fight Pattern");
        Field(drawFightPatternHud, "Draw Fight HUD");

        if (drawFightPatternDebug.boolValue)
        {
            Space();
            EditorGUILayout.LabelField("Swarm Debug", EditorStyles.boldLabel);
            Field(debugDrawSwarmSectors, "Draw Swarm Sectors");

            if (debugDrawSwarmSectors.boolValue)
            {
                Field(debugDrawAllSwarmSectors, "Draw All Swarm Sectors");
                Field(debugDrawSwarmSlotPoint, "Draw Swarm Slot Point");
            }
        }

        if (drawFightPatternHud.boolValue)
        {
            Space();
            EditorGUILayout.LabelField("HUD Fields", EditorStyles.boldLabel);
            Field(hudShowShipSize, "Ship Size");
            Field(hudShowShipState, "Ship State");
            Field(hudShowMoveMode, "Move Mode");
            Field(hudShowFireMode, "Fire Mode");
            Field(hudShowFightPattern, "Fight Pattern");
            Field(hudShowFightPhase, "Fight Phase");
            Field(hudShowSquad, "Squad");
            Field(hudShowTarget, "Target");
        }

        EndSection();
    }

    private bool UsesHoldDistance()
    {
        return UsesPattern(FightLogicType.HoldDistance);
    }

    private bool UsesCloseAndHold()
    {
        return UsesPattern(FightLogicType.CloseAndHold);
    }

    private bool UsesOrbit()
    {
        return UsesPattern(FightLogicType.Orbit);
    }

    private bool UsesAttackRun()
    {
        return UsesPattern(FightLogicType.AttackRun);
    }

    private bool UsesOrbitDirection()
    {
        return UsesPattern(FightLogicType.Orbit)
            || UsesPattern(FightLogicType.Dogfight)
            || UsesPattern(FightLogicType.Swarm);
    }

    private bool UsesPattern(FightLogicType type)
    {
        if (GetEnum<FightLogicType>(FightLogic) == type) return true;

        if (!TargetSizeMater.boolValue) return false;

        return GetEnum<FightLogicType>(smallTarget) == type
            || GetEnum<FightLogicType>(mediumTarget) == type
            || GetEnum<FightLogicType>(bigTarget) == type;
    }

    private static T GetEnum<T>(SerializedProperty property) where T : Enum
    {
        Array values = Enum.GetValues(typeof(T));
        int index = Mathf.Clamp(property.enumValueIndex, 0, values.Length - 1);
        return (T)values.GetValue(index);
    }

    private static bool Foldout(ref bool value, string title)
    {
        Space();
        value = EditorGUILayout.Foldout(value, title, true, EditorStyles.foldoutHeader);

        if (!value) return false;

        EditorGUI.indentLevel++;
        return true;
    }

    private static void EndSection()
    {
        EditorGUI.indentLevel--;
    }

    private static void Space()
    {
        EditorGUILayout.Space(4f);
    }

    private static void Field(SerializedProperty property)
    {
        if (property == null) return;
        EditorGUILayout.PropertyField(property, true);
    }

    private static void Field(SerializedProperty property, string label)
    {
        if (property == null) return;
        EditorGUILayout.PropertyField(property, new GUIContent(label), true);
    }
}
