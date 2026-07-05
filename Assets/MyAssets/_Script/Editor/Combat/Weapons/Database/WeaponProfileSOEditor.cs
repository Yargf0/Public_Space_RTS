using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WeaponProfileSO))]
public class WeaponProfileSOEditor : Editor
{
    SerializedProperty profileId;

    SerializedProperty requestKind;
    SerializedProperty firePattern;
    SerializedProperty payloadKind;
    SerializedProperty statusEffectKind;

    SerializedProperty reloadTime;
    SerializedProperty attackDistance;
    SerializedProperty damageAmount;
    SerializedProperty projectileSpeed;
    SerializedProperty lifetime;
    SerializedProperty rotate;
    SerializedProperty limitRotation;
    SerializedProperty rotationLimitAngle;

    SerializedProperty allowedTargets;
    SerializedProperty priorityTargets;

    SerializedProperty burstInterval;
    SerializedProperty spreadAngle;
    SerializedProperty splashRadius;

    SerializedProperty statusDuration;
    SerializedProperty moveSpeedMultiplier;
    SerializedProperty accelerationMultiplier;
    SerializedProperty disableWeapons;

    SerializedProperty rocketAcceleration;
    SerializedProperty turnAfterLaunch;
    SerializedProperty rocketLaunchScatterAngle;
    SerializedProperty rocketLaunchScatterDuration;
    SerializedProperty rocketLaunchScatterDistance;
    SerializedProperty hitscanBeamWidth;

    SerializedProperty designPreviewHardpointCount;

    void OnEnable()
    {
        profileId = serializedObject.FindProperty("profileId");

        requestKind = serializedObject.FindProperty("requestKind");
        firePattern = serializedObject.FindProperty("firePattern");
        payloadKind = serializedObject.FindProperty("payloadKind");
        statusEffectKind = serializedObject.FindProperty("statusEffectKind");

        reloadTime = serializedObject.FindProperty("reloadTime");
        attackDistance = serializedObject.FindProperty("attackDistance");
        damageAmount = serializedObject.FindProperty("damageAmount");
        projectileSpeed = serializedObject.FindProperty("projectileSpeed");
        lifetime = serializedObject.FindProperty("lifetime");
        rotate = serializedObject.FindProperty("rotate");
        limitRotation = serializedObject.FindProperty("limitRotation");
        rotationLimitAngle = serializedObject.FindProperty("rotationLimitAngle");

        allowedTargets = serializedObject.FindProperty("allowedTargets");
        priorityTargets = serializedObject.FindProperty("priorityTargets");

        burstInterval = serializedObject.FindProperty("burstInterval");
        spreadAngle = serializedObject.FindProperty("spreadAngle");
        splashRadius = serializedObject.FindProperty("splashRadius");

        statusDuration = serializedObject.FindProperty("statusDuration");
        moveSpeedMultiplier = serializedObject.FindProperty("moveSpeedMultiplier");
        accelerationMultiplier = serializedObject.FindProperty("accelerationMultiplier");
        disableWeapons = serializedObject.FindProperty("disableWeapons");

        rocketAcceleration = serializedObject.FindProperty("rocketAcceleration");
        turnAfterLaunch = serializedObject.FindProperty("turnAfterLaunch");
        rocketLaunchScatterAngle = serializedObject.FindProperty("rocketLaunchScatterAngle");
        rocketLaunchScatterDuration = serializedObject.FindProperty("rocketLaunchScatterDuration");
        rocketLaunchScatterDistance = serializedObject.FindProperty("rocketLaunchScatterDistance");
        hitscanBeamWidth = serializedObject.FindProperty("hitscanBeamWidth");

        designPreviewHardpointCount = serializedObject.FindProperty("designPreviewHardpointCount");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawHeader("Identity");
        DrawProperty(profileId);

        DrawHeader("Core");
        DrawProperty(requestKind);
        DrawProperty(firePattern);
        DrawProperty(payloadKind);
        DrawProperty(statusEffectKind);
        DrawProperty(reloadTime);
        DrawProperty(attackDistance);
        DrawProperty(damageAmount);
        DrawProperty(projectileSpeed);
        DrawProperty(lifetime);
        DrawProperty(rotate);

        if (rotate != null && rotate.boolValue)
        {
            DrawProperty(limitRotation);

            if (limitRotation != null && limitRotation.boolValue)
            {
                DrawProperty(rotationLimitAngle);
                EditorGUILayout.HelpBox(
                    "Rotation Limit Angle is a half-angle. Example: 15 allows 15 degrees left and 15 degrees right from the turret default direction. The weapon will not fire until the aim direction is inside this sector.",
                    MessageType.Info);
            }
        }

        DrawHeader("Targeting");
        DrawProperty(allowedTargets);
        DrawProperty(priorityTargets);

        WeaponRequestKind currentRequestKind = requestKind != null
            ? (WeaponRequestKind)requestKind.enumValueIndex
            : WeaponRequestKind.Ballistic;

        WeaponFirePattern currentFirePattern = firePattern != null
            ? (WeaponFirePattern)firePattern.enumValueIndex
            : WeaponFirePattern.Single;

        WeaponPayloadKind currentPayloadKind = payloadKind != null
            ? (WeaponPayloadKind)payloadKind.enumValueIndex
            : WeaponPayloadKind.Direct;

        WeaponStatusEffectKind currentStatusEffectKind = statusEffectKind != null
            ? (WeaponStatusEffectKind)statusEffectKind.enumValueIndex
            : WeaponStatusEffectKind.None;

        DrawHeader("Special");

        if (currentFirePattern != WeaponFirePattern.Single)
        {
            DrawProperty(burstInterval);
        }

        DrawProperty(spreadAngle);

        if (currentPayloadKind == WeaponPayloadKind.Splash ||
            currentPayloadKind == WeaponPayloadKind.DirectPlusSplash)
        {
            DrawProperty(splashRadius);
        }

        if (currentStatusEffectKind != WeaponStatusEffectKind.None)
        {
            DrawHeader("Status Effect");
            DrawProperty(statusDuration);
            DrawProperty(moveSpeedMultiplier);
            DrawProperty(accelerationMultiplier);
            DrawProperty(disableWeapons);
        }

        DrawHeader("Delivery");

        if (currentRequestKind == WeaponRequestKind.Rocket)
        {
            DrawProperty(rocketAcceleration);
            DrawProperty(turnAfterLaunch);

            DrawHeader("Rocket Launch Scatter");
            DrawProperty(rocketLaunchScatterAngle);
            DrawProperty(rocketLaunchScatterDuration);
            DrawProperty(rocketLaunchScatterDistance);

            EditorGUILayout.HelpBox(
                "Launch Scatter is a start-only visual fan for missile volleys. It is separate from Spread Angle. After duration/distance ends, normal rocket homing continues.",
                MessageType.Info);
        }
        else if (currentRequestKind == WeaponRequestKind.Hitscan)
        {
            DrawProperty(hitscanBeamWidth);
        }

        DrawHeader("Design Preview");
        DrawProperty(designPreviewHardpointCount);
        DrawDpsPreview(currentFirePattern, currentPayloadKind);

        serializedObject.ApplyModifiedProperties();
    }

    void DrawHeader(string title)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void DrawProperty(SerializedProperty property, bool includeChildren = false)
    {
        if (property == null)
        {
            return;
        }

        EditorGUILayout.PropertyField(property, includeChildren);
    }

    void DrawDpsPreview(WeaponFirePattern currentFirePattern, WeaponPayloadKind currentPayloadKind)
    {
        float damage = damageAmount != null ? damageAmount.floatValue : 0f;
        float reload = reloadTime != null ? reloadTime.floatValue : 0f;
        float burst = burstInterval != null ? burstInterval.floatValue : 0f;
        int hardpoints = designPreviewHardpointCount != null
            ? Mathf.Max(1, designPreviewHardpointCount.intValue)
            : 1;

        float dps = CalculatePotentialDps(
            damage,
            reload,
            burst,
            hardpoints,
            currentFirePattern);

        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Potential DPS", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Single target DPS", dps.ToString("0.##"));

            EditorGUILayout.LabelField("Damage", damage.ToString("0.##"));
            EditorGUILayout.LabelField("Reload Time", reload.ToString("0.##"));
            EditorGUILayout.LabelField("Preview Hardpoints", hardpoints.ToString());

            if (currentFirePattern == WeaponFirePattern.SequentialHardpoints)
            {
                float cycleTime = Mathf.Max(0.01f, reload + burst * (hardpoints - 1));
                EditorGUILayout.LabelField("Full Burst Cycle Time", cycleTime.ToString("0.##"));
            }

            if (currentPayloadKind == WeaponPayloadKind.Splash)
            {
                EditorGUILayout.HelpBox(
                    "Splash DPS is shown as single-target theoretical DPS. Real effective DPS depends on how many targets are inside splash radius.",
                    MessageType.Info);
            }
            else if (currentPayloadKind == WeaponPayloadKind.DirectPlusSplash)
            {
                EditorGUILayout.HelpBox(
                    "DirectPlusSplash DPS shows direct-hit theoretical DPS only. Extra splash damage depends on target density.",
                    MessageType.Info);
            }
        }
    }

    float CalculatePotentialDps(
        float damage,
        float reload,
        float burst,
        int hardpoints,
        WeaponFirePattern firePattern)
    {
        if (damage <= 0f)
        {
            return 0f;
        }

        reload = Mathf.Max(0.01f, reload);
        burst = Mathf.Max(0f, burst);
        hardpoints = Mathf.Max(1, hardpoints);

        switch (firePattern)
        {
            case WeaponFirePattern.Single:
                return damage / reload;

            case WeaponFirePattern.SequentialHardpoints:
                {
                    float cycleTime = reload + burst * (hardpoints - 1);
                    return damage * hardpoints / Mathf.Max(0.01f, cycleTime);
                }

            case WeaponFirePattern.SimultaneousHardpoints:
                return damage * hardpoints / reload;

            default:
                return damage / reload;
        }
    }
}
