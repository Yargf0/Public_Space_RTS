using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup (typeof(LateSimulationSystemGroup))]
partial struct HealthBarSystem : ISystem
{

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach ((
            RefRO<HealthBar> healthBar,
            RefRW<LocalTransform> localTransform)
            in SystemAPI.Query<
                RefRO<HealthBar>, 
                RefRW<LocalTransform>>())
        {
            if (SystemAPI.HasComponent<Health>(healthBar.ValueRO.healthEntity))
            {
                Health health = SystemAPI.GetComponent<Health>(healthBar.ValueRO.healthEntity);
                if (!health.onHealthChanged)
                {
                    continue;
                }

                float healthNormolized = (float)health.healthAmount / health.healthAmountMax;

                if (healthNormolized == 1f)
                {
                    localTransform.ValueRW.Scale = 0f;
                }
                else
                {
                    localTransform.ValueRW.Scale = 1f;
                }

                RefRW<PostTransformMatrix> barVisualPostTransformMatrix = SystemAPI.GetComponentRW<PostTransformMatrix>(healthBar.ValueRO.barVisualEntity);
                barVisualPostTransformMatrix.ValueRW.Value = float4x4.Scale(healthNormolized, 1, 1);
            }
            //barVisualLocalTransform.ValueRW.Scale = healthNormolized;
        }
    }
}
