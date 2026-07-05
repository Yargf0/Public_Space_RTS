using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
partial struct SpawnerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        ComponentLookup<Unit> unitLookup = SystemAPI.GetComponentLookup<Unit>(true);
        ComponentLookup<ShipStateComponent> shipStateLookup = SystemAPI.GetComponentLookup<ShipStateComponent>(true);
        ComponentLookup<UnitMover> moverLookup = SystemAPI.GetComponentLookup<UnitMover>(true);
        ComponentLookup<LocalTransform> transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

        foreach ((RefRO<LocalTransform> transform, RefRW<Spawner> spawner, DynamicBuffer<SpawnerSquadMemberElement> composition)
            in SystemAPI.Query<RefRO<LocalTransform>, RefRW<Spawner>, DynamicBuffer<SpawnerSquadMemberElement>>())
        {
            TickWave(ref spawner.ValueRW, dt);

            spawner.ValueRW.timer -= dt;
            if (spawner.ValueRO.timer > 0f)
                continue;

            spawner.ValueRW.timer = spawner.ValueRO.spawnTime;

            int spawnCount = spawner.ValueRO.spawnObjectInOneTime + spawner.ValueRO.waveIndex * spawner.ValueRO.addUnitsPerWave;
            if (spawnCount <= 0)
                continue;

            float2 basePos = transform.ValueRO.Position.xy;

            if (spawner.ValueRO.spawnAsSquad)
            {
                for (int i = 0; i < spawnCount; i++)
                {
                    float2 spawnPos = basePos + GetSpawnOffset(i);
                    QueueSquadSpawn(ref ecb, unitLookup, spawner.ValueRO, composition, spawnPos);
                }
            }
            else
            {
                for (int i = 0; i < spawnCount; i++)
                {
                    float2 spawnPos = basePos + GetSpawnOffset(i);
                    QueueSingleShipSpawn(
                        ref ecb,
                        unitLookup,
                        shipStateLookup,
                        moverLookup,
                        transformLookup,
                        spawner.ValueRO,
                        spawnPos);
                }
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private static void TickWave(ref Spawner spawner, float dt)
    {
        if (spawner.waveInterval <= 0f || spawner.addUnitsPerWave == 0)
            return;

        spawner.waveTimer -= dt;
        if (spawner.waveTimer <= 0f)
        {
            spawner.waveTimer += spawner.waveInterval;
            spawner.waveIndex++;
        }
    }

    private static float2 GetSpawnOffset(int index)
    {
        return new float2((index % 3 - 1) * 4f, (index / 3) * 4f);
    }

    private static void QueueSquadSpawn(
        ref EntityCommandBuffer ecb,
        ComponentLookup<Unit> unitLookup,
        in Spawner spawner,
        DynamicBuffer<SpawnerSquadMemberElement> composition,
        float2 spawnPosition)
    {
        Faction faction = ResolveFaction(unitLookup, spawner.prefabToSpawn, composition);

        Entity requestEntity = ecb.CreateEntity();

        ecb.AddComponent(requestEntity, new SpawnSquadRequest
        {
            mode = SpawnSquadRequestMode.SpawnNewSquad,
            targetSquadEntity = Entity.Null,
            targetStrikeGroupEntity = Entity.Null,

            singlePrefab = spawner.prefabToSpawn,
            singlePrefabCount = spawner.squadSize,
            singlePrefabMemberIndex = 0,

            spawnPosition = spawnPosition,
            targetPosition = spawner.positionToMove,

            faction = faction,
            role = spawner.squadRole,

            initialState = spawner.initialState,
            initialMoveMode = spawner.initialMoveMode,
            initialFireMode = spawner.initialFireMode,
            initialTactics = spawner.initialTactics,

            createOrJoinStrikeGroup = spawner.createOrJoinStrikeGroup && spawner.groupId != 0,
            groupId = spawner.groupId,
            groupOwnership = spawner.groupOwnership,
            ownerEntity = spawner.ownerEntity,

            requestTag = 0,
        });

        if (!HasValidComposition(composition))
            return;

        DynamicBuffer<SpawnSquadRequestMemberElement> requestMembers =
            ecb.AddBuffer<SpawnSquadRequestMemberElement>(requestEntity);

        for (int i = 0; i < composition.Length; i++)
        {
            SpawnerSquadMemberElement src = composition[i];
            if (src.prefab == Entity.Null || src.count <= 0)
                continue;

            requestMembers.Add(new SpawnSquadRequestMemberElement
            {
                prefab = src.prefab,
                count = src.count,
                memberPrefabIndex = src.memberPrefabIndex,
            });
        }
    }

    private static void QueueSingleShipSpawn(
        ref EntityCommandBuffer ecb,
        ComponentLookup<Unit> unitLookup,
        ComponentLookup<ShipStateComponent> shipStateLookup,
        ComponentLookup<UnitMover> moverLookup,
        ComponentLookup<LocalTransform> transformLookup,
        in Spawner spawner,
        float2 spawnPosition)
    {
        if (spawner.prefabToSpawn == Entity.Null)
            return;

        Entity ship = ecb.Instantiate(spawner.prefabToSpawn);

        if (transformLookup.HasComponent(spawner.prefabToSpawn))
        {
            float shipZ = unitLookup.HasComponent(spawner.prefabToSpawn)
                ? GameConstants.GetShipZ(unitLookup[spawner.prefabToSpawn].shipSize)
                : GameConstants.ShipDefaultZ;

            SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
                ref ecb,
                ship,
                LocalTransform.FromPosition(new float3(spawnPosition.x, spawnPosition.y, shipZ)),
                true,
                true);
        }

        if (shipStateLookup.HasComponent(spawner.prefabToSpawn))
        {
            ShipStateComponent state = shipStateLookup[spawner.prefabToSpawn];
            state.previousState = state.currentState;
            state.currentState = spawner.initialState;
            state.mode = spawner.initialFireMode;
            state.moveMode = spawner.initialMoveMode;
            state.forcedTarget = Entity.Null;
            ecb.SetComponent(ship, state);
        }

        if (moverLookup.HasComponent(spawner.prefabToSpawn))
        {
            UnitMover mover = moverLookup[spawner.prefabToSpawn];
            mover.targetPos = spawner.positionToMove;
            mover.fightTarget = spawner.positionToMove;
            ecb.SetComponent(ship, mover);
        }
    }

    private static bool HasValidComposition(DynamicBuffer<SpawnerSquadMemberElement> composition)
    {
        for (int i = 0; i < composition.Length; i++)
        {
            if (composition[i].prefab != Entity.Null && composition[i].count > 0)
                return true;
        }

        return false;
    }

    private static Faction ResolveFaction(
        ComponentLookup<Unit> unitLookup,
        Entity fallbackPrefab,
        DynamicBuffer<SpawnerSquadMemberElement> composition)
    {
        for (int i = 0; i < composition.Length; i++)
        {
            Entity prefab = composition[i].prefab;
            if (prefab != Entity.Null && unitLookup.HasComponent(prefab))
                return unitLookup[prefab].faction;
        }

        if (fallbackPrefab != Entity.Null && unitLookup.HasComponent(fallbackPrefab))
            return unitLookup[fallbackPrefab].faction;

        return Faction.Enemy;
    }
}