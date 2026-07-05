using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AiDemoDpsLabelAuthoring : MonoBehaviour
{
    [Header("Damage / Heal Label")]
    [Tooltip("Text before calculated incoming damage per second.")]
    public string labelPrefix = "Damage/s";

    [Tooltip("World offset from target pivot.")]
    public Vector3 worldOffset = new Vector3(0f, 2.5f, 0f);

    [Tooltip("How often DPS/HPS is recalculated. 1 second is usually best for readable demo numbers.")]
    public float sampleWindow = 1f;

    [Tooltip("If true, label is hidden when there is no damage or healing.")]
    public bool showOnlyWhenChanging = false;

    [Tooltip("Show current Health value.")]
    public bool showHealth = true;

    [Tooltip("Show Heal/s line when target is healed.")]
    public bool showHealing = true;

    private class Baker : Baker<AiDemoDpsLabelAuthoring>
    {
        public override void Bake(AiDemoDpsLabelAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new AiDemoDpsLabel
            {
                labelPrefix = new FixedString64Bytes(
                    string.IsNullOrWhiteSpace(authoring.labelPrefix) ? "Damage/s" : authoring.labelPrefix),

                worldOffset = authoring.worldOffset,
                sampleWindow = math.max(0.05f, authoring.sampleWindow),

                showOnlyWhenChanging = authoring.showOnlyWhenChanging,
                showHealth = authoring.showHealth,
                showHealing = authoring.showHealing,

                initialized = false,

                currentHealth = 0f,
                maxHealth = 0f,
                lastHealth = 0f,

                sampleTimer = 0f,
                damageAccumulated = 0f,
                healAccumulated = 0f,

                damagePerSecond = 0f,
                healPerSecond = 0f,
                netPerSecond = 0f,
            });
        }
    }
}

public struct AiDemoDpsLabel : IComponentData
{
    public FixedString64Bytes labelPrefix;
    public float3 worldOffset;
    public float sampleWindow;

    public bool showOnlyWhenChanging;
    public bool showHealth;
    public bool showHealing;

    public bool initialized;

    public float currentHealth;
    public float maxHealth;
    public float lastHealth;

    public float sampleTimer;
    public float damageAccumulated;
    public float healAccumulated;

    public float damagePerSecond;
    public float healPerSecond;

    // Positive = healing dominates.
    // Negative = damage dominates.
    public float netPerSecond;
}

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial struct AiDemoDpsLabelCalculateSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AiDemoDpsLabel>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        // smoothing param, higher = faster reaction
        const float smoothFactor = 1f; // 2..5 depends on fire rate

        foreach ((RefRW<AiDemoDpsLabel> label, RefRO<Health> health)
                 in SystemAPI.Query<RefRW<AiDemoDpsLabel>, RefRO<Health>>())
        {
            float currentHealth = health.ValueRO.healthAmount;
            float maxHealth = health.ValueRO.healthAmountMax;

            label.ValueRW.currentHealth = currentHealth;
            label.ValueRW.maxHealth = maxHealth;

            if (!label.ValueRO.initialized)
            {
                label.ValueRW.initialized = true;
                label.ValueRW.lastHealth = currentHealth;

                label.ValueRW.damagePerSecond = 0f;
                label.ValueRW.healPerSecond = 0f;
                label.ValueRW.netPerSecond = 0f;

                continue;
            }

            float healthDelta = currentHealth - label.ValueRO.lastHealth;
            label.ValueRW.lastHealth = currentHealth;

            // damage/heal rate this frame
            float instantDamageRate = healthDelta < 0f ? (-healthDelta / dt) : 0f;
            float instantHealRate = healthDelta > 0f ? (healthDelta / dt) : 0f;

            // exponential smoothing
            float t = 1f - math.exp(-smoothFactor * dt);

            label.ValueRW.damagePerSecond = math.lerp(label.ValueRW.damagePerSecond, instantDamageRate, t);
            label.ValueRW.healPerSecond = math.lerp(label.ValueRW.healPerSecond, instantHealRate, t);
            label.ValueRW.netPerSecond = label.ValueRW.healPerSecond - label.ValueRW.damagePerSecond;
        }
    }
}