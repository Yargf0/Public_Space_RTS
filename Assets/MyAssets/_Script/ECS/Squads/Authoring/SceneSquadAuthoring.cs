using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SceneSquadAuthoring : MonoBehaviour
{
    [Serializable]
    public class SquadMemberEntry
    {
        [Header("Prefab")]
        [Tooltip("DOTS ship prefab used as a squad member.")]
        public GameObject prefab;

        [Tooltip("Number of ships of this type to create.")]
        public int count = 1;

        [Tooltip("Type index inside the squad composition. For mixed squads: 0, 1, 2...")]
        public int memberPrefabIndex = 0;
    }

    [Header("Squad")]
    public Faction faction = Faction.Enemy;
    public SquadRole role = SquadRole.Interceptor;

    [Header("Formation")]
    public FormationType formation = FormationType.Wedge;
    public float spacing = 4f;

    [Header("Default Orders")]
    public FireMode defaultFireMode = FireMode.FireAtWill;
    public MoveMode defaultMoveMode = MoveMode.MoveAndEngage;

    [Header("Origin")]
    [Tooltip("Use ArmyPlan for normal scene squads. Carrier squads should be created by the carrier.")]
    public SquadOrigin origin = SquadOrigin.ArmyPlan;

    [Header("Composition")]
    public SquadMemberEntry[] composition;

    private class Baker : Baker<SceneSquadAuthoring>
    {
        public override void Bake(SceneSquadAuthoring authoring)
        {
            if (authoring.composition == null || authoring.composition.Length == 0)
            {
                Debug.LogWarning($"SceneSquadAuthoring on '{authoring.name}' has empty composition.", authoring);
                return;
            }

            int totalCount = 0;
            for (int i = 0; i < authoring.composition.Length; i++)
            {
                SquadMemberEntry entry = authoring.composition[i];
                if (entry == null || entry.prefab == null || entry.count <= 0)
                    continue;

                totalCount += entry.count;
            }

            if (totalCount <= 0)
            {
                Debug.LogWarning($"SceneSquadAuthoring on '{authoring.name}' has no valid members.", authoring);
                return;
            }

            Entity entity = GetEntity(TransformUsageFlags.None);

            float3 worldPos = authoring.transform.position;
            float2 spawnAnchor = new float2(worldPos.x, worldPos.y);

            AddComponent(entity, new CreateSquadCommand
            {
                faction = authoring.faction,
                role = authoring.role,
                initialState = ShipState.Idle,
                initialTargetPosition = spawnAnchor,

                memberCount = totalCount,
                formation = authoring.formation,
                spacing = authoring.spacing,

                spawnAnchor = spawnAnchor,
                anchorEntity = Entity.Null,

                defaultFireMode = authoring.defaultFireMode,
                defaultMoveMode = authoring.defaultMoveMode,
                initialTactics = Tactics.Neutral,

                origin = authoring.origin,
                originEntity = Entity.Null,
                carrierSlotIndex = -1,
                initialEndurance = 0f,

                targetStrikeGroupEntity = Entity.Null,
                requestTag = 0,
            });

            DynamicBuffer<CreateSquadMemberTemplate> buffer =
                AddBuffer<CreateSquadMemberTemplate>(entity);

            int slotIndex = 0;

            for (int i = 0; i < authoring.composition.Length; i++)
            {
                SquadMemberEntry entry = authoring.composition[i];                if (entry == null || entry.prefab == null || entry.count <= 0)
                    continue;

                Entity prefabEntity = GetEntity(entry.prefab, TransformUsageFlags.Dynamic);

                for (int c = 0; c < entry.count; c++)
                {
                    buffer.Add(new CreateSquadMemberTemplate
                    {
                        slotIndex = slotIndex,
                        memberPrefab = prefabEntity,
                        memberPrefabIndex = entry.memberPrefabIndex,
                    });

                    slotIndex++;
                }
            }
        }
    }
}
