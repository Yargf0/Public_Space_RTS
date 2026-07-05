using UnityEditor;

[CustomEditor(typeof(FactionMaterialVariantAuthoring))]
[CanEditMultipleObjects]
public class FactionMaterialVariantAuthoringEditor : Editor
{
    private SerializedProperty friendlyMaterial;
    private SerializedProperty enemyMaterial;

    private void OnEnable()
    {
        friendlyMaterial = serializedObject.FindProperty(nameof(FactionMaterialVariantAuthoring.friendlyMaterial));
        enemyMaterial = serializedObject.FindProperty(nameof(FactionMaterialVariantAuthoring.enemyMaterial));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(friendlyMaterial);
        EditorGUILayout.PropertyField(enemyMaterial);
        serializedObject.ApplyModifiedProperties();
    }
}
