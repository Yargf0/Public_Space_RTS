using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class SpawnerAuthoring : MonoBehaviour
{
    [Serializable]
    public class SquadMemberEntryAuthoring
    {
        [Tooltip("Prefab of this ship type inside one squad.")]
        public GameObject prefab;

        [Tooltip("How many ships of this type are inside one squad.")]
        public int count = 1;

        [Tooltip("Composition type index. For one type usually 0; for mixed squad 0, 1, 2...")]
        public int memberPrefabIndex = 0;
    }

    [Header("Spawn Defaults")]
    [FormerlySerializedAs("initialState")]
    public ShipState spawnInitialState = ShipState.MovingToTarget;
    [FormerlySerializedAs("initialMoveMode")]
    public MoveMode spawnInitialMoveMode = MoveMode.MoveAndEngage;
    [FormerlySerializedAs("initialFireMode")]
    public FireMode spawnInitialFireMode = FireMode.FireAtWill;

    [Header("Spawn Mode")]
    [FormerlySerializedAs("spawnAsSquadron")]
    public bool spawnAsSquad = true;

    [Header("Spawn")]
    [FormerlySerializedAs("entitiyToSpawn")]
    public GameObject prefabToSpawn;
    [FormerlySerializedAs("spawnTime")]
    public float spawnInterval = 5f;
    [FormerlySerializedAs("spawnObjectInOneTime")]
    public int unitsPerSpawn = 1;
    [FormerlySerializedAs("positionToMove")]
    public float2 defaultMoveTarget;

    [Header("Waves")]
    [FormerlySerializedAs("waveInterval")]
    public float waveIntervalSeconds = 20f;
    [FormerlySerializedAs("addUnitsPerWave")]
    public int extraUnitsPerWave = 2;

    [Header("Squad")]
    [FormerlySerializedAs("squadronSize")]
    public int squadSize = 5;
    public SquadMemberEntryAuthoring[] squadComposition;

    [FormerlySerializedAs("enemyRole")]
    public SquadRole squadRole = SquadRole.Interceptor;

    [Header("StrikeGroup")]
    [FormerlySerializedAs("assignSquadToArmy")]
    [FormerlySerializedAs("createOrJoinArmy")]
    public bool assignSquadToStrikeGroup = false;
    [FormerlySerializedAs("targetArmyId")]
    [FormerlySerializedAs("armyId")]
    public int targetGroupId = 0;
    public Tactics initialTactics = Tactics.Neutral;

    private class Baker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            Entity singlePrefabEntity = authoring.prefabToSpawn != null
                ? GetEntity(authoring.prefabToSpawn, TransformUsageFlags.Dynamic)
                : Entity.Null;

            AddComponent(entity, new Spawner
            {
                initialState = authoring.spawnInitialState,
                initialMoveMode = authoring.spawnInitialMoveMode,
                initialFireMode = authoring.spawnInitialFireMode,
                prefabToSpawn = singlePrefabEntity,
                spawnTime = math.max(0.05f, authoring.spawnInterval),
                timer = 0f,
                spawnObjectInOneTime = math.max(0, authoring.unitsPerSpawn),
                positionToMove = authoring.defaultMoveTarget,
                waveInterval = math.max(0f, authoring.waveIntervalSeconds),
                waveTimer = math.max(0f, authoring.waveIntervalSeconds),
                addUnitsPerWave = authoring.extraUnitsPerWave,
                waveIndex = 0,
                spawnAsSquad = authoring.spawnAsSquad,
                squadSize = math.max(1, authoring.squadSize),
                squadRole = authoring.squadRole,
                createOrJoinStrikeGroup = authoring.assignSquadToStrikeGroup,
                groupId = authoring.targetGroupId,
                initialTactics = authoring.initialTactics,
                groupOwnership = StrikeGroupOwnership.Debug,
                ownerEntity = Entity.Null,
            });

            DynamicBuffer<SpawnerSquadMemberElement> composition = AddBuffer<SpawnerSquadMemberElement>(entity);
            if (authoring.squadComposition == null) return;

            for (int i = 0; i < authoring.squadComposition.Length; i++)
            {
                SquadMemberEntryAuthoring src = authoring.squadComposition[i];
                if (src == null || src.prefab == null || src.count <= 0) continue;

                composition.Add(new SpawnerSquadMemberElement
                {
                    prefab = GetEntity(src.prefab, TransformUsageFlags.Dynamic),
                    count = math.max(1, src.count),
                    memberPrefabIndex = src.memberPrefabIndex,
                });
            }
        }
    }
}

public struct SpawnerSquadMemberElement : IBufferElementData
{
    public Entity prefab;
    public int count;
    public int memberPrefabIndex;
}

public struct Spawner : IComponentData
{
    public ShipState initialState;
    public MoveMode initialMoveMode;
    public FireMode initialFireMode;

    public Entity prefabToSpawn;
    public float spawnTime;
    public float timer;
    public int spawnObjectInOneTime;
    public float2 positionToMove;

    public float waveInterval;
    public float waveTimer;
    public int addUnitsPerWave;
    public int waveIndex;

    public bool spawnAsSquad;
    public int squadSize;
    public SquadRole squadRole;

    public bool createOrJoinStrikeGroup;
    public int groupId;
    public Tactics initialTactics;
    public StrikeGroupOwnership groupOwnership;
    public Entity ownerEntity;
}
