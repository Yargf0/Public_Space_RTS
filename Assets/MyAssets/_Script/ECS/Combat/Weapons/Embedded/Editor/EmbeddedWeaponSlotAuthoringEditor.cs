#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EmbeddedWeaponSlotAuthoring))]
[CanEditMultipleObjects]
public sealed class EmbeddedWeaponSlotAuthoringEditor : Editor
{
    private SerializedProperty actionProfile;
    private SerializedProperty profile;
    private SerializedProperty ammoGameObject;
    private SerializedProperty muzzlePoint;
    private SerializedProperty muzzlePoints;
    private SerializedProperty visualRoot;
    private SerializedProperty rotateVisual;

    private void OnEnable()
    {
        actionProfile = serializedObject.FindProperty(nameof(EmbeddedWeaponSlotAuthoring.actionProfile));
        profile = serializedObject.FindProperty(nameof(EmbeddedWeaponSlotAuthoring.profile));
        ammoGameObject = serializedObject.FindProperty(nameof(EmbeddedWeaponSlotAuthoring.ammoGameObject));
        muzzlePoint = serializedObject.FindProperty(nameof(EmbeddedWeaponSlotAuthoring.muzzlePoint));
        muzzlePoints = serializedObject.FindProperty(nameof(EmbeddedWeaponSlotAuthoring.muzzlePoints));
        visualRoot = serializedObject.FindProperty(nameof(EmbeddedWeaponSlotAuthoring.visualRoot));
        rotateVisual = serializedObject.FindProperty(nameof(EmbeddedWeaponSlotAuthoring.rotateVisual));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Mode", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(actionProfile);

            bool hasActionProfile = actionProfile.objectReferenceValue != null;
            if (hasActionProfile)
            {
                DrawActionModeHelp();
            }
            else
            {
                DrawDamageWeaponFields();
            }
        }

        DrawMuzzleAndVisualFields();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawActionModeHelp()
    {
        EmbeddedActionProfileSO action = actionProfile.objectReferenceValue as EmbeddedActionProfileSO;
        if (action == null)
        {
            return;
        }

        EditorGUILayout.HelpBox(
            $"Action slot: {action.effectKind} / {action.deliveryKind} / {action.targetFilter}\n" +
            $"Range: {action.range:0.##}. Profile/Ammo are hidden because only normal damage weapons use them.",
            MessageType.Info);

        if (profile.objectReferenceValue != null || ammoGameObject.objectReferenceValue != null)
        {
            EditorGUILayout.HelpBox("This action slot still has Profile/Ammo assigned. They are ignored and can be cleared.", MessageType.Warning);
            if (GUILayout.Button("Clear ignored Profile/Ammo"))
            {
                profile.objectReferenceValue = null;
                ammoGameObject.objectReferenceValue = null;
            }
        }
    }

    private void DrawDamageWeaponFields()
    {
        EditorGUILayout.HelpBox("Damage weapon: Action Profile is empty. Normal Profile + Ammo Game Object are used.", MessageType.Info);
        EditorGUILayout.PropertyField(profile);
        EditorGUILayout.PropertyField(ammoGameObject);
    }

    private void DrawMuzzleAndVisualFields()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Muzzle / Beam Spawn", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(muzzlePoint);
            EditorGUILayout.PropertyField(muzzlePoints, true);
        }

        EditorGUILayout.LabelField("Optional Visual", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(visualRoot);
            if (visualRoot.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(rotateVisual);
            }
        }
    }
}
#endif
