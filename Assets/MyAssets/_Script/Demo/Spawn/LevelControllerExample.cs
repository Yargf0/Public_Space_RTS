using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// example controller. ArmyPlan is just spawn preset
public class LevelControllerExample : MonoBehaviour
{
    [Header("Spawn points and target")]
    public Transform[] groupSpawnPoints;
    public Transform playerBase;

    [Header("Waves")]
    public ArmyPlan[] waves;
    public float firstWaveDelay = 5f;
    public float intervalBetweenWaves = 30f;

    private int nextWaveIndex;
    private float nextWaveAt;
    private readonly System.Collections.Generic.Dictionary<int, Entity> waveGroups = new System.Collections.Generic.Dictionary<int, Entity>();

    private void Start()
    {
        nextWaveAt = Time.time + firstWaveDelay;
    }

    private void Update()
    {
        if (waves == null || Time.time < nextWaveAt)
            return;

        if (nextWaveIndex >= waves.Length)
        {
            enabled = false;
            return;
        }

        SpawnWave(nextWaveIndex);
        nextWaveIndex++;
        nextWaveAt = Time.time + intervalBetweenWaves;
    }

    private void SpawnWave(int waveIndex)
    {
        ArmyPlan plan = waves[waveIndex];
        if (plan == null)
            return;

        float2 center = GetSpawnPoint(waveIndex);
        float2 target = playerBase != null ? ToFloat2(playerBase.position) : center;

        int baseTag = 1000 + waveIndex * 100;
        int groupId = LevelSpawnApi.ReserveGroupId();
        Entity groupEntity = LevelSpawnApi.RequestStrikeGroup(
            plan,
            center,
            target,
            groupId,
            plan.defaultTactics,
            StrikeGroupOwnership.Debug,
            Entity.Null,
            baseTag);

        if (groupEntity == Entity.Null)
        {
            Debug.LogWarning($"[Level] Wave {waveIndex} ({plan.name}) failed to create StrikeGroup.");
            return;
        }

        waveGroups[waveIndex] = groupEntity;
        OrderGroupToAttack(waveIndex, target);
        Debug.Log($"[Level] Wave {waveIndex} ({plan.name}) started as StrikeGroup id={groupId}, entity={groupEntity.Index}:{groupEntity.Version}.");
    }

    public void OrderGroupToAttack(int waveIndex, float2 attackPoint)
    {
        if (!waveGroups.TryGetValue(waveIndex, out Entity groupEntity) || groupEntity == Entity.Null)
            return;

        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
        if (!em.Exists(groupEntity) || !em.HasComponent<StrikeGroupOrder>(groupEntity))
            return;

        StrikeGroupOrder order = em.GetComponentData<StrikeGroupOrder>(groupEntity);
        order.stance = Stance.AttackMove;
        order.targetEntity = Entity.Null;
        order.targetPosition = attackPoint;
        order.radius = 24f;
        order.version++;
        em.SetComponentData(groupEntity, order);
    }

    private float2 GetSpawnPoint(int index)
    {
        if (groupSpawnPoints == null || groupSpawnPoints.Length == 0 || groupSpawnPoints[index % groupSpawnPoints.Length] == null)
            return float2.zero;

        Transform t = groupSpawnPoints[index % groupSpawnPoints.Length];
        return ToFloat2(t.position);
    }

    private static float2 ToFloat2(Vector3 v)
    {
        return new float2(v.x, v.y);
    }
}
