#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EmbeddedActionProfileSO))]
[CanEditMultipleObjects]
public sealed class EmbeddedActionProfileSOEditor : Editor
{
    private SerializedProperty targetFilter;
    private SerializedProperty range;
    private SerializedProperty searchInterval;
    private SerializedProperty canTargetSelf;

    private SerializedProperty rotate;
    private SerializedProperty rotateSpeed;
    private SerializedProperty aimInterval;

    private SerializedProperty deliveryKind;
    private SerializedProperty weaponProfile;
    private SerializedProperty deliveryPrefab;
    private SerializedProperty tickInterval;
    private SerializedProperty beamWidth;
    private SerializedProperty beamVisualInterval;

    private SerializedProperty effectKind;
    private SerializedProperty valuePerSecond;
    private SerializedProperty maxStoredValue;

    private SerializedProperty statusDuration;
    private SerializedProperty moveSpeedMultiplier;
    private SerializedProperty accelerationMultiplier;
    private SerializedProperty effectMultiplier;
    private SerializedProperty disableWeapons;
    private SerializedProperty maxTargetsPerTick;
    private SerializedProperty maxCellsPerTick;

    private void OnEnable()
    {
        targetFilter = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.targetFilter));
        range = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.range));
        searchInterval = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.searchInterval));
        canTargetSelf = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.canTargetSelf));

        rotate = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.rotate));
        rotateSpeed = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.rotateSpeed));
        aimInterval = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.aimInterval));

        deliveryKind = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.deliveryKind));
        weaponProfile = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.weaponProfile));
        deliveryPrefab = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.deliveryPrefab));
        tickInterval = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.tickInterval));
        beamWidth = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.beamWidth));
        beamVisualInterval = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.beamVisualInterval));

        effectKind = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.effectKind));
        valuePerSecond = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.valuePerSecond));
        maxStoredValue = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.maxStoredValue));

        statusDuration = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.statusDuration));
        moveSpeedMultiplier = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.moveSpeedMultiplier));
        accelerationMultiplier = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.accelerationMultiplier));
        effectMultiplier = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.effectMultiplier));
        disableWeapons = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.disableWeapons));
        maxTargetsPerTick = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.maxTargetsPerTick));
        maxCellsPerTick = serializedObject.FindProperty(nameof(EmbeddedActionProfileSO.maxCellsPerTick));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPresetButtons();
        EditorGUILayout.Space(6f);

        EmbeddedActionDeliveryKind delivery = GetDeliveryKind();
        EmbeddedActionEffectKind effect = GetEffectKind();
        EmbeddedActionTargetFilter filter = GetTargetFilter();

        DrawTargeting(filter);
        DrawAim(delivery);
        DrawDelivery(delivery);
        DrawEffect(effect);
        DrawPerformance(delivery);
        DrawValidationHints(delivery, effect, filter);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPresetButtons()
    {
        EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Repair Beam")) ApplyPreset(PresetKind.RepairBeam);
                if (GUILayout.Button("Shield Beam")) ApplyPreset(PresetKind.ShieldBeam);
                if (GUILayout.Button("EMP Beam")) ApplyPreset(PresetKind.EmpBeam);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Buff Beam")) ApplyPreset(PresetKind.BuffBeam);
                if (GUILayout.Button("Debuff Beam")) ApplyPreset(PresetKind.DebuffBeam);
                if (GUILayout.Button("Repair Aura")) ApplyPreset(PresetKind.RepairAura);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("EMP Aura")) ApplyPreset(PresetKind.EmpAura);
                GUILayout.FlexibleSpace();
            }
        }
    }

    private void DrawTargeting(EmbeddedActionTargetFilter filter)
    {
        EditorGUILayout.LabelField("Targeting", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(targetFilter);

            if (filter != EmbeddedActionTargetFilter.Self)
            {
                EditorGUILayout.PropertyField(range);
                EditorGUILayout.PropertyField(searchInterval);
            }
            else
            {
                EditorGUILayout.HelpBox("Self: the slot owner is always the target. Range/Search Interval are not used.", MessageType.Info);
            }

            if (filter == EmbeddedActionTargetFilter.AllyAny || filter == EmbeddedActionTargetFilter.AllyDamaged)
            {
                EditorGUILayout.PropertyField(canTargetSelf);
            }
        }
    }

    private void DrawAim(EmbeddedActionDeliveryKind delivery)
    {
        if (delivery == EmbeddedActionDeliveryKind.Aura || delivery == EmbeddedActionDeliveryKind.None)
        {
            return;
        }

        EditorGUILayout.LabelField("Aim", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(rotate);
            if (rotate.boolValue)
            {
                EditorGUILayout.PropertyField(rotateSpeed);
                EditorGUILayout.PropertyField(aimInterval);
            }
        }
    }

    private void DrawDelivery(EmbeddedActionDeliveryKind delivery)
    {
        EditorGUILayout.LabelField("Delivery", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(deliveryKind);

            switch (delivery)
            {
                case EmbeddedActionDeliveryKind.WeaponProfile:
                    EditorGUILayout.HelpBox("WeaponProfile is for normal damage weapons. Use BeamOverTime or Aura for Repair/Shield/EMP/Buff/Debuff.", MessageType.Info);
                    EditorGUILayout.PropertyField(weaponProfile);
                    EditorGUILayout.PropertyField(deliveryPrefab, new GUIContent("Ammo Prefab"));
                    break;

                case EmbeddedActionDeliveryKind.BeamOverTime:
                    EditorGUILayout.PropertyField(deliveryPrefab, new GUIContent("Beam Visual Prefab"));
                    EditorGUILayout.PropertyField(tickInterval);
                    EditorGUILayout.PropertyField(beamWidth);
                    EditorGUILayout.PropertyField(beamVisualInterval);
                    break;

                case EmbeddedActionDeliveryKind.Aura:
                    EditorGUILayout.PropertyField(deliveryPrefab, new GUIContent("Aura Visual Prefab"));
                    EditorGUILayout.PropertyField(tickInterval);
                    EditorGUILayout.PropertyField(beamWidth, new GUIContent("Aura Visual Width"));
                    EditorGUILayout.PropertyField(beamVisualInterval, new GUIContent("Aura Visual Interval"));
                    break;

                case EmbeddedActionDeliveryKind.None:
                    EditorGUILayout.HelpBox("Delivery=None is not used by the runtime. Choose BeamOverTime or Aura.", MessageType.Warning);
                    break;
            }
        }
    }

    private void DrawPerformance(EmbeddedActionDeliveryKind delivery)
    {
        if (delivery != EmbeddedActionDeliveryKind.Aura)
        {
            return;
        }

        EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.HelpBox("V6: Aura uses full honest scan. maxTargetsPerTick/maxCellsPerTick are kept only for data compatibility and are ignored by baker/runtime.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(maxTargetsPerTick);
                EditorGUILayout.PropertyField(maxCellsPerTick);
            }
        }
    }

    private void DrawEffect(EmbeddedActionEffectKind effect)
    {
        EditorGUILayout.LabelField("Effect", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(effectKind);

            if (effect == EmbeddedActionEffectKind.Damage ||
                effect == EmbeddedActionEffectKind.Repair ||
                effect == EmbeddedActionEffectKind.ShieldRestore)
            {
                EditorGUILayout.PropertyField(valuePerSecond);
            }

            if (effect == EmbeddedActionEffectKind.ShieldRestore)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Shield", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(maxStoredValue);
            }

            if (effect == EmbeddedActionEffectKind.Emp ||
                effect == EmbeddedActionEffectKind.Buff ||
                effect == EmbeddedActionEffectKind.Debuff)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Status Effects", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(statusDuration);
                EditorGUILayout.PropertyField(moveSpeedMultiplier);
                EditorGUILayout.PropertyField(accelerationMultiplier);

                if (effect == EmbeddedActionEffectKind.Buff || effect == EmbeddedActionEffectKind.Debuff)
                {
                    EditorGUILayout.PropertyField(effectMultiplier);
                }

                if (effect == EmbeddedActionEffectKind.Emp || effect == EmbeddedActionEffectKind.Debuff)
                {
                    EditorGUILayout.PropertyField(disableWeapons);
                }
            }
        }
    }

    private void DrawValidationHints(
        EmbeddedActionDeliveryKind delivery,
        EmbeddedActionEffectKind effect,
        EmbeddedActionTargetFilter filter)
    {
        if (delivery == EmbeddedActionDeliveryKind.WeaponProfile)
        {
            if (effect != EmbeddedActionEffectKind.Damage)
            {
                EditorGUILayout.HelpBox("WeaponProfile supports only Effect=Damage.", MessageType.Error);
            }

            if (filter != EmbeddedActionTargetFilter.Enemy)
            {
                EditorGUILayout.HelpBox("WeaponProfile supports only Target Filter=Enemy.", MessageType.Error);
            }

            return;
        }

        if (effect == EmbeddedActionEffectKind.Damage ||
            effect == EmbeddedActionEffectKind.Emp ||
            effect == EmbeddedActionEffectKind.Debuff)
        {
            if (filter != EmbeddedActionTargetFilter.Enemy)
            {
                EditorGUILayout.HelpBox($"{effect} must use Target Filter=Enemy.", MessageType.Error);
            }
        }
        else if (effect == EmbeddedActionEffectKind.Repair ||
                 effect == EmbeddedActionEffectKind.ShieldRestore ||
                 effect == EmbeddedActionEffectKind.Buff)
        {
            if (filter == EmbeddedActionTargetFilter.Enemy)
            {
                EditorGUILayout.HelpBox($"{effect} should not target Enemy. Choose AllyDamaged, AllyAny, or Self.", MessageType.Error);
            }
        }
    }

    private EmbeddedActionDeliveryKind GetDeliveryKind()
    {
        return (EmbeddedActionDeliveryKind)deliveryKind.enumValueIndex;
    }

    private EmbeddedActionEffectKind GetEffectKind()
    {
        return (EmbeddedActionEffectKind)effectKind.enumValueIndex;
    }

    private EmbeddedActionTargetFilter GetTargetFilter()
    {
        return (EmbeddedActionTargetFilter)targetFilter.enumValueIndex;
    }

    private enum PresetKind
    {
        RepairBeam,
        ShieldBeam,
        EmpBeam,
        BuffBeam,
        DebuffBeam,
        RepairAura,
        EmpAura,
    }

    private void ApplyPreset(PresetKind kind)
    {
        foreach (Object obj in targets)
        {
            EmbeddedActionProfileSO profile = obj as EmbeddedActionProfileSO;
            if (profile == null)
            {
                continue;
            }

            Undo.RecordObject(profile, "Apply Embedded Action Preset");
            ApplyPresetTo(profile, kind);
            EditorUtility.SetDirty(profile);
        }

        serializedObject.Update();
    }

    private static void ApplyPresetTo(EmbeddedActionProfileSO profile, PresetKind kind)
    {
        profile.weaponProfile = null;
        profile.deliveryPrefab = null;
        profile.rotateSpeed = 8f;
        profile.aimInterval = 1f / 30f;
        profile.searchInterval = 0.2f;
        profile.tickInterval = 0.2f;
        profile.beamWidth = 0.35f;
        profile.beamVisualInterval = 0.05f;
        profile.valuePerSecond = 0f;
        profile.maxStoredValue = 0f;
        profile.statusDuration = 0f;
        profile.moveSpeedMultiplier = 1f;
        profile.accelerationMultiplier = 1f;
        profile.effectMultiplier = 1f;
        profile.disableWeapons = false;
        profile.canTargetSelf = false;
        profile.maxTargetsPerTick = 0;
        profile.maxCellsPerTick = 0;

        switch (kind)
        {
            case PresetKind.RepairBeam:
                profile.targetFilter = EmbeddedActionTargetFilter.AllyDamaged;
                profile.range = 12f;
                profile.canTargetSelf = false;
                profile.rotate = true;
                profile.deliveryKind = EmbeddedActionDeliveryKind.BeamOverTime;
                profile.tickInterval = 0.2f;
                profile.beamWidth = 0.35f;
                profile.beamVisualInterval = 0.05f;
                profile.effectKind = EmbeddedActionEffectKind.Repair;
                profile.valuePerSecond = 20f;
                break;

            case PresetKind.ShieldBeam:
                profile.targetFilter = EmbeddedActionTargetFilter.AllyDamaged;
                profile.range = 10f;
                profile.canTargetSelf = true;
                profile.rotate = true;
                profile.deliveryKind = EmbeddedActionDeliveryKind.BeamOverTime;
                profile.tickInterval = 0.2f;
                profile.beamWidth = 0.45f;
                profile.beamVisualInterval = 0.05f;
                profile.effectKind = EmbeddedActionEffectKind.ShieldRestore;
                profile.valuePerSecond = 15f;
                profile.maxStoredValue = 100f;
                break;

            case PresetKind.EmpBeam:
                profile.targetFilter = EmbeddedActionTargetFilter.Enemy;
                profile.range = 14f;
                profile.rotate = true;
                profile.deliveryKind = EmbeddedActionDeliveryKind.BeamOverTime;
                profile.tickInterval = 0.25f;
                profile.beamWidth = 0.4f;
                profile.beamVisualInterval = 0.05f;
                profile.effectKind = EmbeddedActionEffectKind.Emp;
                profile.statusDuration = 1f;
                profile.moveSpeedMultiplier = 0.7f;
                profile.accelerationMultiplier = 0.7f;
                profile.disableWeapons = true;
                break;

            case PresetKind.BuffBeam:
                profile.targetFilter = EmbeddedActionTargetFilter.AllyAny;
                profile.range = 10f;
                profile.searchInterval = 0.3f;
                profile.canTargetSelf = true;
                profile.rotate = true;
                profile.deliveryKind = EmbeddedActionDeliveryKind.BeamOverTime;
                profile.tickInterval = 0.25f;
                profile.beamWidth = 0.35f;
                profile.beamVisualInterval = 0.05f;
                profile.effectKind = EmbeddedActionEffectKind.Buff;
                profile.statusDuration = 1.5f;
                profile.moveSpeedMultiplier = 1.15f;
                profile.accelerationMultiplier = 1.15f;
                profile.effectMultiplier = 1.25f;
                break;

            case PresetKind.DebuffBeam:
                profile.targetFilter = EmbeddedActionTargetFilter.Enemy;
                profile.range = 12f;
                profile.rotate = true;
                profile.deliveryKind = EmbeddedActionDeliveryKind.BeamOverTime;
                profile.tickInterval = 0.25f;
                profile.beamWidth = 0.35f;
                profile.beamVisualInterval = 0.05f;
                profile.effectKind = EmbeddedActionEffectKind.Debuff;
                profile.statusDuration = 1.5f;
                profile.moveSpeedMultiplier = 0.8f;
                profile.accelerationMultiplier = 0.8f;
                profile.effectMultiplier = 0.75f;
                profile.disableWeapons = false;
                break;

            case PresetKind.RepairAura:
                profile.targetFilter = EmbeddedActionTargetFilter.AllyDamaged;
                profile.range = 8f;
                profile.searchInterval = 0.3f;
                profile.canTargetSelf = true;
                profile.rotate = false;
                profile.deliveryKind = EmbeddedActionDeliveryKind.Aura;
                profile.tickInterval = 0.5f;
                profile.beamWidth = 0.5f;
                profile.beamVisualInterval = 0.2f;
                profile.effectKind = EmbeddedActionEffectKind.Repair;
                profile.valuePerSecond = 8f;
                break;

            case PresetKind.EmpAura:
                profile.targetFilter = EmbeddedActionTargetFilter.Enemy;
                profile.range = 7f;
                profile.searchInterval = 0.3f;
                profile.rotate = false;
                profile.deliveryKind = EmbeddedActionDeliveryKind.Aura;
                profile.tickInterval = 0.5f;
                profile.beamWidth = 0.5f;
                profile.beamVisualInterval = 0.2f;
                profile.effectKind = EmbeddedActionEffectKind.Emp;
                profile.statusDuration = 1f;
                profile.moveSpeedMultiplier = 0.75f;
                profile.accelerationMultiplier = 0.75f;
                profile.disableWeapons = true;
                break;
        }
    }
}
#endif
