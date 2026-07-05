using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// old demo controller, now on StrikeGroup. ArmyPlan is just spawn preset
public class ArenaArmyVsArmyController : MonoBehaviour
{
    [System.Serializable]
    public class ArenaPreset
    {
        public string label = "Preset";
        public ArmyPlan sideAPlan;
        public ArmyPlan sideBPlan;
    }

    [Header("Presets")]
    public ArenaPreset[] presets;
    public int startPresetIndex = 0;

    [Header("Positions")]
    public Transform sideASpawn;
    public Transform sideBSpawn;
    public Vector2 fallbackSideAPosition = new Vector2(-60f, 0f);
    public Vector2 fallbackSideBPosition = new Vector2(60f, 0f);

    [Header("Runtime")]
    public bool startOnPlay = true;
    public float attackOrderDelay = 2f;
    public bool cleanupPreviousBeforeStart = true;

    private Entity groupA = Entity.Null;
    private Entity groupB = Entity.Null;
    private int groupIdA;
    private int groupIdB;
    private float orderAt = -1f;
    private int activePreset = -1;

    private void Start()
    {
        if (startOnPlay)
            StartPreset(startPresetIndex);
    }

    private void Update()
    {
        if (orderAt > 0f && Time.time >= orderAt)
        {
            orderAt = -1f;
            IssueAttackOrders();
        }
    }

    public void StartPreset(int presetIndex)
    {
        if (presets == null || presetIndex < 0 || presetIndex >= presets.Length)
        {
            Debug.LogWarning($"[Arena] Invalid preset index={presetIndex}", this);
            return;
        }

        if (!TryGetEm(out EntityManager em))
            return;

        if (cleanupPreviousBeforeStart)
            CleanupStrikeGroups(em);

        ArenaPreset preset = presets[presetIndex];
        if (preset == null || preset.sideAPlan == null || preset.sideBPlan == null)
        {
            Debug.LogWarning($"[Arena] Preset {presetIndex} is incomplete.", this);
            return;
        }

        float2 posA = GetPosition(sideASpawn, fallbackSideAPosition);
        float2 posB = GetPosition(sideBSpawn, fallbackSideBPosition);

        groupIdA = LevelSpawnApi.ReserveGroupId();
        groupIdB = LevelSpawnApi.ReserveGroupId();

        groupA = LevelSpawnApi.RequestStrikeGroup(
            preset.sideAPlan,
            posA,
            posB,
            groupIdA,
            preset.sideAPlan.defaultTactics,
            StrikeGroupOwnership.Debug,
            Entity.Null,
            1000 + presetIndex * 10000);

        groupB = LevelSpawnApi.RequestStrikeGroup(
            preset.sideBPlan,
            posB,
            posA,
            groupIdB,
            preset.sideBPlan.defaultTactics,
            StrikeGroupOwnership.Debug,
            Entity.Null,
            2000 + presetIndex * 10000);

        activePreset = presetIndex;
        orderAt = Time.time + math.max(0f, attackOrderDelay);

        Debug.Log($"[Arena] Started preset {presetIndex}: A group={groupIdA} entity={Format(groupA)} -> {posB}; B group={groupIdB} entity={Format(groupB)} -> {posA}", this);
    }

    public void IssueAttackOrders()
    {
        if (!TryGetEm(out EntityManager em))
            return;

        float2 posA = GetPosition(sideASpawn, fallbackSideAPosition);
        float2 posB = GetPosition(sideBSpawn, fallbackSideBPosition);

        SetOrder(em, groupA, Stance.AttackMove, posB, 32f);
        SetOrder(em, groupB, Stance.AttackMove, posA, 32f);

        Debug.Log($"[Arena] Attack orders issued. preset={activePreset} A={Format(groupA)} B={Format(groupB)}", this);
    }

    public void HoldPosition()
    {
        if (!TryGetEm(out EntityManager em))
            return;

        if (em.Exists(groupA) && em.HasComponent<StrikeGroupData>(groupA))
            SetOrder(em, groupA, Stance.HoldPosition, em.GetComponentData<StrikeGroupData>(groupA).center, 24f);
        if (em.Exists(groupB) && em.HasComponent<StrikeGroupData>(groupB))
            SetOrder(em, groupB, Stance.HoldPosition, em.GetComponentData<StrikeGroupData>(groupB).center, 24f);
    }

    private static void SetOrder(EntityManager em, Entity group, Stance stance, float2 target, float radius)
    {
        if (group == Entity.Null || !em.Exists(group) || !em.HasComponent<StrikeGroupOrder>(group))
            return;

        StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(group);
        order.stance = stance;
        order.targetEntity = Entity.Null;
        order.targetPosition = target;
        order.radius = radius > 0f ? radius : 24f;
        order.version++;
        em.SetComponentData(group, order);
    }

    private static void CleanupStrikeGroups(EntityManager em)
    {
        EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<StrikeGroupTag>(), ComponentType.ReadOnly<StrikeGroupData>());
        NativeArray<Entity> groups = q.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < groups.Length; i++)
        {
            Entity group = groups[i];
            if (!em.Exists(group)) continue;

            if (em.HasBuffer<StrikeGroupSquadElement>(group))
            {
                DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(group);
                for (int s = squads.Length - 1; s >= 0; s--)
                {
                    Entity squad = squads[s].squadEntity;
                    if (squad == Entity.Null || !em.Exists(squad)) continue;

                    if (em.HasBuffer<SquadMember>(squad))
                    {
                        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squad);
                        for (int m = 0; m < members.Length; m++)
                        {
                            Entity ship = members[m].ship;
                            if (ship != Entity.Null && em.Exists(ship))
                                em.DestroyEntity(ship);
                        }
                    }

                    em.DestroyEntity(squad);
                }
            }

            em.DestroyEntity(group);
        }

        groups.Dispose();
        q.Dispose();
    }

    private static float2 GetPosition(Transform t, Vector2 fallback)
    {
        if (t == null)
            return new float2(fallback.x, fallback.y);

        Vector3 p = t.position;
        return new float2(p.x, p.y);
    }

    private static bool TryGetEm(out EntityManager em)
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

    private static string Format(Entity e)
    {
        return e == Entity.Null ? "Null" : $"{e.Index}:{e.Version}";
    }
}
