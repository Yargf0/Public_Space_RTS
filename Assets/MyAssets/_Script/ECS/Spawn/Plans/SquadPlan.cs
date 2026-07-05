using UnityEngine;

// template of one squad, used by ArmyPlan and production catalog
[CreateAssetMenu(menuName = "Game/Squad Plan", fileName = "SquadPlan")]
public class SquadPlan : ScriptableObject
{
    [Header("Squad")]
    [Tooltip("Label for logs/debug/UI fallback.")]
    public string label;

    [Tooltip("Squad role. Defines default formation, spacing and combat behavior.")]
    public SquadRole role = SquadRole.Interceptor;

    [Header("Composition")]
    [Tooltip("Squad composition: one or several ship types.")]
    public SquadCompositionEntry[] composition;
}

[System.Serializable]
public class SquadCompositionEntry
{
    [Tooltip("String prefab id from LevelSpawnPrefabRegistry. Used by LevelSpawnApi.")]
    public string prefabId;

    [Tooltip("Direct prefab reference. Used by production catalog baking.")]
    public GameObject prefab;

    [Min(1)]
    [Tooltip("How many ships of this type are in the squad.")]
    public int count = 1;

    [Tooltip("Type index inside squad composition. For one type usually 0. For mixed squad: 0, 1, 2...")]
    public int memberPrefabIndex = 0;
}
