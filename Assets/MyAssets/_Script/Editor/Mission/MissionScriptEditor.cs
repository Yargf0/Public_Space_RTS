using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MissionScript), true)]
public sealed class MissionScriptEditor : Editor
{
    private SerializedProperty missionLabelProp;
    private SerializedProperty descriptionProp;
    private SerializedProperty spawnPresetsProp;
    private SerializedProperty initialSpawnPresetIndexesProp;
    private SerializedProperty eventsProp;

    private void OnEnable()
    {
        missionLabelProp = serializedObject.FindProperty("missionLabel");
        descriptionProp = serializedObject.FindProperty("description");
        spawnPresetsProp = serializedObject.FindProperty("spawnPresets");
        initialSpawnPresetIndexesProp = serializedObject.FindProperty("initialSpawnPresetIndexes");
        eventsProp = serializedObject.FindProperty("events");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(missionLabelProp);
        EditorGUILayout.PropertyField(descriptionProp);
        EditorGUILayout.Space(8f);

        EditorGUILayout.PropertyField(spawnPresetsProp, true);
        EditorGUILayout.PropertyField(initialSpawnPresetIndexesProp, true);
        EditorGUILayout.Space(10f);

        EditorGUILayout.LabelField("Mission Events", EditorStyles.boldLabel);
        DrawEvents();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawEvents()
    {
        if (eventsProp == null)
        {
            EditorGUILayout.HelpBox("Property 'events' not found.", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Events", GUILayout.Width(60f));
        int newSize = Mathf.Max(0, EditorGUILayout.IntField(eventsProp.arraySize, GUILayout.Width(60f)));
        if (newSize != eventsProp.arraySize)
            eventsProp.arraySize = newSize;

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ Event", GUILayout.Width(90f)))
            AddEvent();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);

        for (int i = 0; i < eventsProp.arraySize; i++)
        {
            SerializedProperty eventProp = eventsProp.GetArrayElementAtIndex(i);
            SerializedProperty labelProp = eventProp.FindPropertyRelative("label");
            string foldoutLabel = string.IsNullOrEmpty(labelProp.stringValue) ? $"Event {i}" : $"Event {i}: {labelProp.stringValue}";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            eventProp.isExpanded = EditorGUILayout.Foldout(eventProp.isExpanded, foldoutLabel, true);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(70f)))
            {
                eventsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (eventProp.isExpanded)
            {
                EditorGUI.indentLevel++;
                DrawEventBody(eventProp);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }
    }

    private void DrawEventBody(SerializedProperty eventProp)
    {
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("label"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("fireOnce"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("cooldownSeconds"));

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Trigger", EditorStyles.boldLabel);
        SerializedProperty triggerProp = eventProp.FindPropertyRelative("trigger");
        DrawTrigger(triggerProp);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        DrawActions(eventProp.FindPropertyRelative("actions"));
    }

    private void DrawTrigger(SerializedProperty triggerProp)
    {
        if (triggerProp == null)
        {
            EditorGUILayout.HelpBox("Trigger property not found.", MessageType.Error);
            return;
        }

        SerializedProperty kind = triggerProp.FindPropertyRelative("kind");
        EditorGUILayout.PropertyField(kind);

        MissionTriggerKind k = (MissionTriggerKind)kind.enumValueIndex;
        switch (k)
        {
            case MissionTriggerKind.AfterDelay:
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("delaySeconds"));
                break;

            case MissionTriggerKind.GroupReadinessBelow:
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("groupId"));
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("faction"));
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("threshold"));
                break;

            case MissionTriggerKind.GroupDestroyed:
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("groupId"));
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("faction"));
                break;

            case MissionTriggerKind.WaypointReached:
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("waypointId"));
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("groupId"));
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("faction"));
                EditorGUILayout.PropertyField(triggerProp.FindPropertyRelative("arrivalRadius"));
                break;
        }
    }

    private void DrawActions(SerializedProperty actionsProp)
    {
        if (actionsProp == null)
        {
            EditorGUILayout.HelpBox("Actions property not found.", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(45f));
        int newSize = Mathf.Max(0, EditorGUILayout.IntField(actionsProp.arraySize, GUILayout.Width(60f)));
        if (newSize != actionsProp.arraySize)
            actionsProp.arraySize = newSize;
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3f);

        // buttons in rows so Inspector don't look cramped
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Order Group", GUILayout.Height(22f)))
            AddAction(actionsProp, MissionActionKind.OrderGroup);
        if (GUILayout.Button("+ Attack Faction Once", GUILayout.Height(22f)))
            AddAction(actionsProp, MissionActionKind.AttackFactionOnce);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Hunt Faction", GUILayout.Height(22f)))
            AddAction(actionsProp, MissionActionKind.HuntFaction);
        if (GUILayout.Button("+ Spawn Group", GUILayout.Height(22f)))
            AddAction(actionsProp, MissionActionKind.SpawnStrikeGroup);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Set Tactics", GUILayout.Height(22f)))
            AddAction(actionsProp, MissionActionKind.SetTactics);
        if (GUILayout.Button("+ Destroy Group", GUILayout.Height(22f)))
            AddAction(actionsProp, MissionActionKind.DestroyGroup);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Log", GUILayout.Height(22f)))
            AddAction(actionsProp, MissionActionKind.Log);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5f);

        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            SerializedProperty actionProp = actionsProp.GetArrayElementAtIndex(i);
            SerializedProperty kind = actionProp.FindPropertyRelative("kind");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Action {i}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(80f), GUILayout.Height(21f)))
            {
                actionsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(kind);
            DrawActionFields(actionProp, (MissionActionKind)kind.enumValueIndex);
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }
    }

    private void DrawActionFields(SerializedProperty actionProp, MissionActionKind kind)
    {
        switch (kind)
        {
            case MissionActionKind.SpawnStrikeGroup:
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("spawnPresetIndex"));
                break;

            case MissionActionKind.OrderGroup:
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("groupId"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("faction"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("stance"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("targetWaypointId"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("radius"));
                break;

            case MissionActionKind.SetTactics:
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("groupId"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("faction"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("tactics"));
                break;

            case MissionActionKind.AttackFactionOnce:
                DrawAttackFactionFields(actionProp, false);
                break;

            case MissionActionKind.HuntFaction:
                DrawAttackFactionFields(actionProp, true);
                break;

            case MissionActionKind.DestroyGroup:
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("groupId"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("faction"));
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("destroyMode"));
                break;

            case MissionActionKind.Log:
                EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("message"));
                break;
        }
    }


    private static void DrawAttackFactionFields(SerializedProperty actionProp, bool hunt)
    {
        EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("groupId"), new GUIContent("Attacker Group Id"));
        EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("faction"), new GUIContent("Attacker Faction"));
        EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("targetFaction"), new GUIContent("Target Faction"));
        EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("stance"));
        EditorGUILayout.PropertyField(actionProp.FindPropertyRelative("radius"));

        if (hunt)
        {
            EditorGUILayout.HelpBox("Hunt repeats only when the event repeats: set Fire Once = false and Cooldown Seconds = 1..3. The action refreshes target position every time the event fires.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Attack Faction Once finds the current center of Target Faction once and orders the group there.", MessageType.Info);
        }
    }

    private void AddEvent()
    {
        int index = eventsProp.arraySize;
        eventsProp.InsertArrayElementAtIndex(index);
        SerializedProperty eventProp = eventsProp.GetArrayElementAtIndex(index);
        eventProp.isExpanded = true;

        eventProp.FindPropertyRelative("label").stringValue = $"Event {index}";
        eventProp.FindPropertyRelative("fireOnce").boolValue = true;
        eventProp.FindPropertyRelative("cooldownSeconds").floatValue = 0f;

        SerializedProperty triggerProp = eventProp.FindPropertyRelative("trigger");
        triggerProp.FindPropertyRelative("kind").enumValueIndex = (int)MissionTriggerKind.AfterDelay;
        triggerProp.FindPropertyRelative("delaySeconds").floatValue = 2f;
        triggerProp.FindPropertyRelative("threshold").floatValue = 0.5f;
        triggerProp.FindPropertyRelative("arrivalRadius").floatValue = 12f;

        eventProp.FindPropertyRelative("actions").arraySize = 0;
    }

    private static void AddAction(SerializedProperty actionsProp, MissionActionKind kind)
    {
        int index = actionsProp.arraySize;
        actionsProp.InsertArrayElementAtIndex(index);
        SerializedProperty actionProp = actionsProp.GetArrayElementAtIndex(index);
        actionProp.FindPropertyRelative("kind").enumValueIndex = (int)kind;
        actionProp.FindPropertyRelative("stance").enumValueIndex = (int)Stance.AttackMove;
        actionProp.FindPropertyRelative("radius").floatValue = 24f;
        SerializedProperty targetFaction = actionProp.FindPropertyRelative("targetFaction");
        if (targetFaction != null)
            targetFaction.enumValueIndex = (int)Faction.Friendly;
        actionProp.FindPropertyRelative("tactics").enumValueIndex = (int)Tactics.Neutral;
        actionProp.FindPropertyRelative("destroyMode").enumValueIndex = (int)DestroyGroupMode.DestroySquadsAndShips;
        actionProp.FindPropertyRelative("message").stringValue = "Mission event";
    }
}
