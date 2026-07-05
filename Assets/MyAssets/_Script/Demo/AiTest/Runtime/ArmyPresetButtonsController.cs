using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// buttons 1-4: clear battle, spawn two armies, attack-move
// button 5: same but with respawn
public class ArmyPresetButtonsController : MonoBehaviour
{
    [Serializable]
    public class BattleButtonSlot
    {
        public string label = "Battle Preset";
        public Button button;

        [Header("Army A")]
        public ArmyPlan armyA;
        public Transform spawnPointA;
        public Vector2 fallbackSpawnPositionA = new Vector2(-50f, 0f);

        [Header("Army B")]
        public ArmyPlan armyB;
        public Transform spawnPointB;
        public Vector2 fallbackSpawnPositionB = new Vector2(50f, 0f);

        [Header("Shared target")]
        [Tooltip("Optional. If null, fallbackTargetPosition is used. For your request keep this at (0,0).")]
        public Transform targetPoint;
        public Vector2 fallbackTargetPosition = Vector2.zero;

        [Header("Order")]
        public bool issueAttackMove = true;
        [Min(0f)] public float attackRadius = 32f;

        [Header("Tactics")]
        public bool usePlanDefaultTactics = true;
        public Tactics armyATacticsOverride = Tactics.Neutral;
        public Tactics armyBTacticsOverride = Tactics.Neutral;

        [Header("Selection")]
        [Tooltip("If enabled, spawned friendly squads are AI-owned and cannot be selected or controlled by the player.")]
        public bool makeSpawnedShipsUnselectable = false;

        [Header("Respawn points A")]
        [Tooltip("Optional extra respawn points for Army A. If empty, spawnPointA/fallbackSpawnPositionA is used.")]
        public Transform[] respawnPointsA;
        public Vector2[] fallbackRespawnPositionsA;

        [Header("Respawn points B")]
        [Tooltip("Optional extra respawn points for Army B. If empty, spawnPointB/fallbackSpawnPositionB is used.")]
        public Transform[] respawnPointsB;
        public Vector2[] fallbackRespawnPositionsB;
    }

    private struct BattleRuntimeSnapshot
    {
        public ArmyPlan armyA;
        public ArmyPlan armyB;
        public float2 spawnA;
        public float2 spawnB;
        public float2 target;
        public bool issueAttackMove;
        public float attackRadius;
        public Tactics tacticsA;
        public Tactics tacticsB;
        public bool makeUnselectable;
        public float2[] respawnPointsA;
        public float2[] respawnPointsB;
        public int sourceSlotIndex;
    }

    [Header("Buttons: expected size = 5")]
    public BattleButtonSlot[] slots = new BattleButtonSlot[5];

    [Header("Button 5 respawn")]
    [Tooltip("0-based index. 4 means the fifth button.")]
    [Range(0, 4)] public int respawnButtonIndex = 4;
    public bool enableRespawnOnRespawnButton = true;
    [Tooltip("For button 5/subscene 5: spawned friendly ships/squads cannot be selected by click or selection box.")]
    public bool makeRespawnButtonUnselectable = true;
    [Tooltip("For button 5: always sends spawned and respawned armies to Force Respawn Button Target Position.")]
    public bool forceRespawnButtonAttackMove = true;
    public Vector2 forceRespawnButtonTargetPosition = Vector2.zero;

    [Header("Demo scene selection lock")]
    [Tooltip("If true, any button pressed while Demo SubScene index is active will spawn AI-owned, unselectable ships/squads.")]
    public bool makeAllButtonsUnselectableOnDemoScene = true;
    [Tooltip("0-based index. 4 means the fifth SubScene.")]
    [Range(0, 4)] public int unselectableDemoSceneIndex = 4;
    [Tooltip("Emergency/global mode. If true, every button always spawns unselectable ships, regardless of active SubScene.")]
    public bool makeAllButtonsUnselectableAlways = false;

    [Header("Startup")]
    public bool spawnOnStart = false;
    [Range(0, 4)] public int startSlotIndex = 0;

    [Header("Cleanup")]
    [Tooltip("Deletes pending SpawnSquadRequest/CreateSquadCommand entities before spawning the new preset, so old delayed requests do not leak into the new battle.")]
    public bool cleanupPendingSpawnRequests = true;

    [Tooltip("Also deletes all current StrikeGroup and Squadron entities. Usually should stay enabled for demo battle reset buttons.")]
    public bool cleanupSquadsAndGroups = true;

    [Header("Debug")]
    public bool logActions = true;
    public int requestTagBase = 600000;
    public int requestTagStride = 10000;
    public int requestTagSideStride = 1000;

    private UnityAction[] buttonHandlers;
    private Entity activeGroupA = Entity.Null;
    private Entity activeGroupB = Entity.Null;
    private int activeSlotIndex = -1;
    private BattleRuntimeSnapshot activeSnapshot;

    private void OnEnable()
    {
        BindButtons();
    }

    private void Start()
    {
        if (spawnOnStart)
            SelectSlot(startSlotIndex);
    }

    private void OnDisable()
    {
        UnbindButtons();
    }

    public void SelectSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            Debug.LogWarning($"[ArmyPresetButtons] Invalid slot index={slotIndex}.", this);
            return;
        }

        BattleButtonSlot slot = GetSlot(slotIndex);
        if (slot == null)
        {
            Debug.LogWarning($"[ArmyPresetButtons] Slot {slotIndex + 1} is null.", this);
            return;
        }

        DisableRespawnBattle();
        CleanupCurrentBattle();

        BattleRuntimeSnapshot snapshot = BuildSnapshot(slot, slotIndex);
        SpawnBattle(snapshot, makeActive: true, enableRespawn: enableRespawnOnRespawnButton && slotIndex == respawnButtonIndex);
    }

    public void SelectSlot1() { SelectSlot(0); }
    public void SelectSlot2() { SelectSlot(1); }
    public void SelectSlot3() { SelectSlot(2); }
    public void SelectSlot4() { SelectSlot(3); }
    public void SelectSlot5() { SelectSlot(4); }

    public void CleanupCurrentBattle()
    {
        if (!TryGetEntityManager(out EntityManager em))
            return;

        if (cleanupPendingSpawnRequests)
        {
            DestroyAllByComponent(em, ComponentType.ReadOnly<SpawnSquadRequest>());
            DestroyAllByComponent(em, ComponentType.ReadOnly<CreateSquadCommand>());
            DestroyAllByComponent(em, ComponentType.ReadOnly<SquadMemberDeathEvent>());
            DestroyAllByComponent(em, ComponentType.ReadOnly<ArmyPresetRespawnRequest>());
        }

        DestroyAllShipEntities(em);

        if (cleanupSquadsAndGroups)
        {
            DestroyAllByComponent(em, ComponentType.ReadOnly<SquadronTag>());
            DestroyAllByComponent(em, ComponentType.ReadOnly<StrikeGroupTag>());
        }

        activeGroupA = Entity.Null;
        activeGroupB = Entity.Null;
        activeSlotIndex = -1;
    }

    private void SpawnBattle(BattleRuntimeSnapshot snapshot, bool makeActive, bool enableRespawn)
    {
        Entity groupA = SpawnArmy(snapshot.armyA, snapshot.spawnA, snapshot.target, snapshot.tacticsA, snapshot.sourceSlotIndex, 0, snapshot.makeUnselectable);
        Entity groupB = SpawnArmy(snapshot.armyB, snapshot.spawnB, snapshot.target, snapshot.tacticsB, snapshot.sourceSlotIndex, 1, snapshot.makeUnselectable);

        if (snapshot.issueAttackMove)
        {
            SetAttackMoveOrder(groupA, snapshot.target, snapshot.attackRadius);
            SetAttackMoveOrder(groupB, snapshot.target, snapshot.attackRadius);
        }

        if (enableRespawn)
            EnableRespawnBattle(groupA, groupB, snapshot.spawnA, snapshot.spawnB, snapshot.target, snapshot.respawnPointsA, snapshot.respawnPointsB);

        if (makeActive)
        {
            activeGroupA = groupA;
            activeGroupB = groupB;
            activeSlotIndex = snapshot.sourceSlotIndex;
            activeSnapshot = snapshot;
        }

        if (logActions)
        {
            Debug.Log($"[ArmyPresetButtons] Spawned battle slot={snapshot.sourceSlotIndex + 1} groupA={Format(groupA)} groupB={Format(groupB)} spawnA={snapshot.spawnA} spawnB={snapshot.spawnB} target={snapshot.target} respawn={enableRespawn}", this);
        }
    }

    private Entity SpawnArmy(ArmyPlan plan, float2 spawnPosition, float2 targetPosition, Tactics tactics, int slotIndex, int sideIndex, bool makeUnselectable)
    {
        if (plan == null)
        {
            Debug.LogWarning($"[ArmyPresetButtons] Slot {slotIndex + 1}, side {(sideIndex == 0 ? "A" : "B")} has no ArmyPlan.", this);
            return Entity.Null;
        }

        int groupId = LevelSpawnApi.ReserveGroupId();
        int tag = requestTagBase + math.max(0, slotIndex) * requestTagStride + sideIndex * requestTagSideStride;

        StrikeGroupOwnership ownership = makeUnselectable ? StrikeGroupOwnership.Director : StrikeGroupOwnership.Debug;

        Entity group = LevelSpawnApi.RequestStrikeGroup(
            plan,
            spawnPosition,
            targetPosition,
            groupId,
            tactics,
            ownership,
            Entity.Null,
            tag);

        if (group == Entity.Null)
            Debug.LogWarning($"[ArmyPresetButtons] Failed to spawn ArmyPlan '{plan.name}' for side {(sideIndex == 0 ? "A" : "B")}.", this);

        return group;
    }

    private BattleRuntimeSnapshot BuildSnapshot(BattleButtonSlot slot, int slotIndex)
    {
        float2 spawnA = GetPosition(slot.spawnPointA, slot.fallbackSpawnPositionA);
        float2 spawnB = GetPosition(slot.spawnPointB, slot.fallbackSpawnPositionB);
        bool isRespawnButton = slotIndex == respawnButtonIndex;
        bool forceRespawnOrder = forceRespawnButtonAttackMove && isRespawnButton;
        float2 target = forceRespawnOrder
            ? new float2(forceRespawnButtonTargetPosition.x, forceRespawnButtonTargetPosition.y)
            : GetPosition(slot.targetPoint, slot.fallbackTargetPosition);

        bool makeUnselectable = slot.makeSpawnedShipsUnselectable ||
            makeAllButtonsUnselectableAlways ||
            (makeAllButtonsUnselectableOnDemoScene && IsDemoSceneIndexActive(unselectableDemoSceneIndex)) ||
            (makeRespawnButtonUnselectable && isRespawnButton);

        return new BattleRuntimeSnapshot
        {
            armyA = slot.armyA,
            armyB = slot.armyB,
            spawnA = spawnA,
            spawnB = spawnB,
            target = target,
            issueAttackMove = forceRespawnOrder || slot.issueAttackMove,
            attackRadius = slot.attackRadius > 0f ? slot.attackRadius : 32f,
            tacticsA = slot.usePlanDefaultTactics && slot.armyA != null ? slot.armyA.defaultTactics : slot.armyATacticsOverride,
            tacticsB = slot.usePlanDefaultTactics && slot.armyB != null ? slot.armyB.defaultTactics : slot.armyBTacticsOverride,
            makeUnselectable = makeUnselectable,
            respawnPointsA = BuildRespawnPositions(slot.respawnPointsA, slot.fallbackRespawnPositionsA, spawnA),
            respawnPointsB = BuildRespawnPositions(slot.respawnPointsB, slot.fallbackRespawnPositionsB, spawnB),
            sourceSlotIndex = slotIndex,
        };
    }

    private static void SetAttackMoveOrder(Entity group, float2 target, float radius)
    {
        if (!TryGetEntityManager(out EntityManager em))
            return;

        if (group == Entity.Null || !em.Exists(group) || !em.HasComponent<StrikeGroupOrder>(group))
            return;

        StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(group);
        order.stance = Stance.AttackMove;
        order.targetEntity = Entity.Null;
        order.targetPosition = target;
        order.radius = radius > 0f ? radius : 32f;
        order.version++;
        em.SetComponentData(group, order);
    }

    private void EnableRespawnBattle(Entity groupA, Entity groupB, float2 spawnA, float2 spawnB, float2 target, float2[] respawnPointsA, float2[] respawnPointsB)
    {
        if (!TryGetEntityManager(out EntityManager em))
            return;

        DestroyAllByComponent(em, ComponentType.ReadOnly<ArmyPresetRespawnBattle>());
        Entity configEntity = em.CreateEntity();
        em.AddComponentData(configEntity, new ArmyPresetRespawnBattle
        {
            enabled = 1,
            groupA = groupA,
            groupB = groupB,
            spawnA = spawnA,
            spawnB = spawnB,
            target = target,
        });

        DynamicBuffer<ArmyPresetRespawnPoint> points = em.AddBuffer<ArmyPresetRespawnPoint>(configEntity);
        AddRespawnPoints(points, 0, respawnPointsA, spawnA);
        AddRespawnPoints(points, 1, respawnPointsB, spawnB);
    }

    private static void DisableRespawnBattle()
    {
        if (!TryGetEntityManager(out EntityManager em))
            return;

        DestroyAllByComponent(em, ComponentType.ReadOnly<ArmyPresetRespawnBattle>());
        DestroyAllByComponent(em, ComponentType.ReadOnly<ArmyPresetRespawnRequest>());
    }

    private static float2[] BuildRespawnPositions(Transform[] transforms, Vector2[] fallbackPositions, float2 defaultPosition)
    {
        int transformCount = transforms != null ? transforms.Length : 0;
        int fallbackCount = fallbackPositions != null ? fallbackPositions.Length : 0;
        int count = math.max(transformCount, fallbackCount);

        if (count <= 0)
            return new[] { defaultPosition };

        float2[] result = new float2[count];
        for (int i = 0; i < count; i++)
        {
            if (i < transformCount && transforms[i] != null)
            {
                Vector3 p = transforms[i].position;
                result[i] = new float2(p.x, p.y);
            }
            else if (i < fallbackCount)
            {
                Vector2 p = fallbackPositions[i];
                result[i] = new float2(p.x, p.y);
            }
            else
            {
                result[i] = defaultPosition;
            }
        }

        return result;
    }

    private static void AddRespawnPoints(DynamicBuffer<ArmyPresetRespawnPoint> buffer, byte sideIndex, float2[] points, float2 fallback)
    {
        if (points == null || points.Length == 0)
        {
            buffer.Add(new ArmyPresetRespawnPoint { sideIndex = sideIndex, position = fallback });
            return;
        }

        for (int i = 0; i < points.Length; i++)
            buffer.Add(new ArmyPresetRespawnPoint { sideIndex = sideIndex, position = points[i] });
    }

    private static bool IsDemoSceneIndexActive(int zeroBasedIndex)
    {
        if (!TryGetEntityManager(out EntityManager em))
            return false;

        EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<AiDemoSubSceneSwitcherRuntime>());
        NativeArray<AiDemoSubSceneSwitcherRuntime> runtimes = query.ToComponentDataArray<AiDemoSubSceneSwitcherRuntime>(Allocator.Temp);

        bool isActive = false;
        for (int i = 0; i < runtimes.Length; i++)
        {
            AiDemoSubSceneSwitcherRuntime runtime = runtimes[i];
            if (runtime.isLoaded != 0 && runtime.currentIndex == zeroBasedIndex)
            {
                isActive = true;
                break;
            }
        }

        runtimes.Dispose();
        query.Dispose();
        return isActive;
    }


    private static void DestroyAllShipEntities(EntityManager em)
    {
        EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Unit>() },
            None = new[] { ComponentType.ReadOnly<Prefab>() },
            Options = EntityQueryOptions.IncludeDisabledEntities,
        });

        NativeArray<Entity> ships = query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < ships.Length; i++)
            DestroyEntityWithLinkedGroup(em, ships[i]);

        ships.Dispose();
        query.Dispose();
    }

    private static void DestroyAllByComponent(EntityManager em, ComponentType componentType)
    {
        EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { componentType },
            None = new[] { ComponentType.ReadOnly<Prefab>() },
            Options = EntityQueryOptions.IncludeDisabledEntities,
        });

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            if (entity != Entity.Null && em.Exists(entity))
                em.DestroyEntity(entity);
        }

        entities.Dispose();
        query.Dispose();
    }

    private static void DestroyEntityWithLinkedGroup(EntityManager em, Entity root)
    {
        if (root == Entity.Null || !em.Exists(root))
            return;

        if (!em.HasBuffer<LinkedEntityGroup>(root))
        {
            em.DestroyEntity(root);
            return;
        }

        DynamicBuffer<LinkedEntityGroup> linked = em.GetBuffer<LinkedEntityGroup>(root);
        NativeList<Entity> copy = new NativeList<Entity>(linked.Length, Allocator.Temp);
        for (int i = 0; i < linked.Length; i++)
            copy.Add(linked[i].Value);

        for (int i = 0; i < copy.Length; i++)
        {
            Entity entity = copy[i];
            if (entity != Entity.Null && em.Exists(entity))
                em.DestroyEntity(entity);
        }

        copy.Dispose();
    }

    private void BindButtons()
    {
        UnbindButtons();

        if (slots == null)
            return;

        buttonHandlers = new UnityAction[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            int capturedIndex = i;
            BattleButtonSlot slot = slots[i];
            if (slot == null || slot.button == null)
                continue;

            UnityAction handler = () => SelectSlot(capturedIndex);
            buttonHandlers[i] = handler;
            slot.button.onClick.AddListener(handler);
        }
    }

    private void UnbindButtons()
    {
        if (slots == null || buttonHandlers == null)
            return;

        int count = math.min(slots.Length, buttonHandlers.Length);
        for (int i = 0; i < count; i++)
        {
            BattleButtonSlot slot = slots[i];
            UnityAction handler = buttonHandlers[i];
            if (slot != null && slot.button != null && handler != null)
                slot.button.onClick.RemoveListener(handler);
        }

        buttonHandlers = null;
    }

    private BattleButtonSlot GetSlot(int index)
    {
        if (!IsValidSlotIndex(index))
            return null;

        return slots[index];
    }

    private bool IsValidSlotIndex(int index)
    {
        return slots != null && index >= 0 && index < slots.Length;
    }

    private static float2 GetPosition(Transform t, Vector2 fallback)
    {
        if (t == null)
            return new float2(fallback.x, fallback.y);

        Vector3 p = t.position;
        return new float2(p.x, p.y);
    }

    private static bool TryGetEntityManager(out EntityManager em)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            em = default;
            return false;
        }

        em = world.EntityManager;
        return true;
    }

    private static string Format(Entity entity)
    {
        return entity == Entity.Null ? "Null" : $"{entity.Index}:{entity.Version}";
    }
}

public struct ArmyPresetRespawnBattle : IComponentData
{
    public byte enabled;
    public Entity groupA;
    public Entity groupB;
    public float2 spawnA;
    public float2 spawnB;
    public float2 target;
}

[InternalBufferCapacity(8)]
public struct ArmyPresetRespawnPoint : IBufferElementData
{
    public byte sideIndex;
    public float2 position;
}

public struct ArmyPresetRespawnRequest : IComponentData
{
    public Entity squad;
    public Entity group;
    public int slotIndex;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthDeadTestSystem))]
[UpdateBefore(typeof(SquadronHealthSystem))]
public partial struct ArmyPresetRespawnCaptureSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        if (!TryGetConfig(em, out ArmyPresetRespawnBattle config) || config.enabled == 0)
            return;

        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach ((RefRO<SquadMemberDeathEvent> deathEvent, Entity _) in SystemAPI.Query<RefRO<SquadMemberDeathEvent>>().WithEntityAccess())
        {
            Entity squad = deathEvent.ValueRO.squad;
            Entity group = ResolveGroup(em, squad);
            if (!IsRespawnGroup(group, config))
                continue;

            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new ArmyPresetRespawnRequest
            {
                squad = squad,
                group = group,
                slotIndex = deathEvent.ValueRO.slotIndex,
            });
        }

        ecb.Playback(state.EntityManager);
    }

    private static bool TryGetConfig(EntityManager em, out ArmyPresetRespawnBattle config)
    {
        EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<ArmyPresetRespawnBattle>());
        NativeArray<ArmyPresetRespawnBattle> configs = query.ToComponentDataArray<ArmyPresetRespawnBattle>(Allocator.Temp);
        bool found = configs.Length > 0;
        config = found ? configs[0] : default;
        configs.Dispose();
        query.Dispose();
        return found;
    }

    private static Entity ResolveGroup(EntityManager em, Entity squad)
    {
        if (squad == Entity.Null || !em.Exists(squad) || !em.HasComponent<StrikeGroupMember>(squad))
            return Entity.Null;

        return em.GetComponentData<StrikeGroupMember>(squad).groupEntity;
    }

    private static bool IsRespawnGroup(Entity group, in ArmyPresetRespawnBattle config)
    {
        return group != Entity.Null && (group == config.groupA || group == config.groupB);
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SquadronHealthSystem))]
[UpdateBefore(typeof(SquadDefaultsSystem))]
[UpdateBefore(typeof(SquadronCleanupSystem))]
public partial struct ArmyPresetRespawnExecutionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        bool hasConfig = TryGetConfig(em, out Entity configEntity, out ArmyPresetRespawnBattle config) && config.enabled != 0;

        NativeList<Entity> requests = new NativeList<Entity>(Allocator.Temp);
        foreach ((RefRO<ArmyPresetRespawnRequest> _, Entity requestEntity) in SystemAPI.Query<RefRO<ArmyPresetRespawnRequest>>().WithEntityAccess())
            requests.Add(requestEntity);

        for (int i = 0; i < requests.Length; i++)
        {
            Entity requestEntity = requests[i];
            if (requestEntity == Entity.Null || !em.Exists(requestEntity))
                continue;

            ArmyPresetRespawnRequest request = em.GetComponentData<ArmyPresetRespawnRequest>(requestEntity);

            if (hasConfig && IsRespawnGroup(request.group, config))
            {
                byte sideIndex = request.group == config.groupB ? (byte)1 : (byte)0;
                float2 fallbackSpawn = sideIndex == 1 ? config.spawnB : config.spawnA;
                float2 spawn = ResolveRespawnPoint(em, configEntity, sideIndex, request.squad, request.slotIndex, fallbackSpawn);
                float2 target = config.target;

                ForceGroupAttackMoveOrder(em, request.group, target);
                RespawnOneShip(em, request.squad, request.slotIndex, spawn, target);
                ForceRefreshGroupOrder(em, request.group);
            }

            if (em.Exists(requestEntity))
                em.DestroyEntity(requestEntity);
        }

        requests.Dispose();
    }

    private static bool TryGetConfig(EntityManager em, out Entity configEntity, out ArmyPresetRespawnBattle config)
    {
        EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<ArmyPresetRespawnBattle>());
        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        bool found = entities.Length > 0;

        configEntity = found ? entities[0] : Entity.Null;
        config = found ? em.GetComponentData<ArmyPresetRespawnBattle>(configEntity) : default;

        entities.Dispose();
        query.Dispose();
        return found;
    }

    private static bool IsRespawnGroup(Entity group, in ArmyPresetRespawnBattle config)
    {
        return group != Entity.Null && (group == config.groupA || group == config.groupB);
    }

    private static float2 ResolveRespawnPoint(EntityManager em, Entity configEntity, byte sideIndex, Entity squadEntity, int slotIndex, float2 fallback)
    {
        if (configEntity == Entity.Null || !em.Exists(configEntity) || !em.HasBuffer<ArmyPresetRespawnPoint>(configEntity))
            return fallback;

        DynamicBuffer<ArmyPresetRespawnPoint> points = em.GetBuffer<ArmyPresetRespawnPoint>(configEntity);
        int sideCount = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i].sideIndex == sideIndex)
                sideCount++;
        }

        if (sideCount <= 0)
            return fallback;

        int stableSlot = slotIndex >= 0 ? slotIndex : 0;
        int seed = math.abs(stableSlot + squadEntity.Index);
        int pick = seed % sideCount;

        int current = 0;
        for (int i = 0; i < points.Length; i++)
        {
            ArmyPresetRespawnPoint point = points[i];
            if (point.sideIndex != sideIndex)
                continue;

            if (current == pick)
                return point.position;

            current++;
        }

        return fallback;
    }

    private static void ForceGroupAttackMoveOrder(EntityManager em, Entity group, float2 target)
    {
        if (group == Entity.Null || !em.Exists(group) || !em.HasComponent<StrikeGroupOrder>(group))
            return;

        StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(group);
        order.stance = Stance.AttackMove;
        order.targetEntity = Entity.Null;
        order.targetPosition = target;
        order.radius = order.radius > 0f ? order.radius : 32f;
        order.version++;
        em.SetComponentData(group, order);
    }

    private static bool RespawnOneShip(EntityManager em, Entity squadEntity, int preferredSlotIndex, float2 spawnPosition, float2 targetPosition)
    {
        if (squadEntity == Entity.Null ||
            !em.Exists(squadEntity) ||
            !em.HasComponent<SquadComponent>(squadEntity) ||
            !em.HasBuffer<SquadMember>(squadEntity) ||
            !em.HasBuffer<SquadSlotTemplate>(squadEntity))
        {
            return false;
        }

        SquadComponent squad = em.GetComponentData<SquadComponent>(squadEntity);
        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);

        if (squad.maxMembers <= 0 || members.Length >= squad.maxMembers)
            return false;

        int stableSlot = preferredSlotIndex >= 0 && !IsSlotOccupied(members, preferredSlotIndex)
            ? preferredSlotIndex
            : FindFirstEmptySlot(members, squad.maxMembers);

        if (stableSlot < 0)
            return false;

        DynamicBuffer<SquadSlotTemplate> templates = em.GetBuffer<SquadSlotTemplate>(squadEntity);
        Entity prefab = ResolvePrefabForSlot(templates, stableSlot);
        if (prefab == Entity.Null)
            return false;

        int memberPrefabIndex = ResolvePrefabIndexForSlot(templates, stableSlot);
        int formationSlotIndex = members.Length;
        int formationMemberCount = math.max(1, squad.maxMembers);

        Entity ship = em.Instantiate(prefab);
        float2 offset = FormationUtility.GetSlotOffset(squad.formation, formationSlotIndex, formationMemberCount, squad.spacing);
        float2 shipPos = spawnPosition + offset;

        SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
            em,
            ship,
            LocalTransform.FromPosition(new float3(shipPos.x, shipPos.y, ResolveShipZ(em, ship))));

        SquadConfigurator.AddOrSetComponent(em, ship, new ShipSquadRef
        {
            squad = squadEntity,
            slotIndex = stableSlot,
            formationSlotIndex = formationSlotIndex,
        });

        SquadConfigurator.AddOrSetComponent(em, ship, new ShipPriorityHint
        {
            target = squad.priorityTarget,
            weight = 50f,
        });

        if (em.HasComponent<Selected>(ship))
            em.SetComponentEnabled<Selected>(ship, false);

        if (em.HasComponent<CanControl>(ship))
            em.RemoveComponent<CanControl>(ship);

        DynamicBuffer<SquadMember> updatedMembers = em.GetBuffer<SquadMember>(squadEntity);
        updatedMembers.Add(new SquadMember
        {
            ship = ship,
            slotIndex = stableSlot,
            formationSlotIndex = formationSlotIndex,
        });

        EnsureTemplateExists(em.GetBuffer<SquadSlotTemplate>(squadEntity), stableSlot, prefab, memberPrefabIndex);
        SquadFormationRuntimeUtility.RebuildCompactFormationSlots(em, updatedMembers);

        squad.aliveCount = updatedMembers.Length;
        em.SetComponentData(squadEntity, squad);
        SquadCommandApplyUtility.UpdateSquadPathSizeClass(em, squadEntity, updatedMembers);

        SquadConfigurator.ApplyInitialState(em, ship, targetPosition, ShipState.MovingToTarget, squad.defaultMoveMode, squad.defaultFireMode);
        return true;
    }

    private static bool IsSlotOccupied(DynamicBuffer<SquadMember> members, int slotIndex)
    {
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i].slotIndex == slotIndex)
                return true;
        }

        return false;
    }

    private static int FindFirstEmptySlot(DynamicBuffer<SquadMember> members, int maxMembers)
    {
        for (int slot = 0; slot < maxMembers; slot++)
        {
            if (!IsSlotOccupied(members, slot))
                return slot;
        }

        return -1;
    }

    private static Entity ResolvePrefabForSlot(DynamicBuffer<SquadSlotTemplate> templates, int slotIndex)
    {
        for (int i = 0; i < templates.Length; i++)
        {
            if (templates[i].slotIndex == slotIndex && templates[i].memberPrefab != Entity.Null)
                return templates[i].memberPrefab;
        }

        return Entity.Null;
    }

    private static int ResolvePrefabIndexForSlot(DynamicBuffer<SquadSlotTemplate> templates, int slotIndex)
    {
        for (int i = 0; i < templates.Length; i++)
        {
            if (templates[i].slotIndex == slotIndex)
                return templates[i].memberPrefabIndex;
        }

        return 0;
    }

    private static void EnsureTemplateExists(DynamicBuffer<SquadSlotTemplate> templates, int slotIndex, Entity prefab, int memberPrefabIndex)
    {
        for (int i = 0; i < templates.Length; i++)
        {
            if (templates[i].slotIndex == slotIndex)
                return;
        }

        templates.Add(new SquadSlotTemplate
        {
            slotIndex = slotIndex,
            memberPrefab = prefab,
            memberPrefabIndex = memberPrefabIndex,
        });
    }

    private static void ForceRefreshGroupOrder(EntityManager em, Entity group)
    {
        if (group == Entity.Null || !em.Exists(group))
            return;

        if (em.HasComponent<StrikeGroupOrderRuntime>(group))
        {
            StrikeGroupOrderRuntime runtime = em.GetComponentData<StrikeGroupOrderRuntime>(group);
            runtime.appliedVersion = 0;
            em.SetComponentData(group, runtime);
        }

        if (em.HasComponent<StrikeGroupData>(group))
        {
            StrikeGroupData data = em.GetComponentData<StrikeGroupData>(group);
            data.summaryTimer = 0f;
            em.SetComponentData(group, data);
        }
    }
    private static float ResolveShipZ(EntityManager em, Entity ship)
    {
        if (ship != Entity.Null && em.Exists(ship) && em.HasComponent<Unit>(ship))
            return GameConstants.GetShipZ(em.GetComponentData<Unit>(ship).shipSize);

        return GameConstants.ShipDefaultZ;
    }

}
