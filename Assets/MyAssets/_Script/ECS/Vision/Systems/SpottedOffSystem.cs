using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[UpdateBefore(typeof(SearchlightSpotSystem))]
partial struct VisibilityFadeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach ((RefRW<Visibility> visibility, EnabledRefRW<Visibility> visibilityEnabled)
            in SystemAPI.Query<RefRW<Visibility>, EnabledRefRW<Visibility>>())
        {
            visibility.ValueRW.visibleToFriendlyTimer = math.max(0f, visibility.ValueRO.visibleToFriendlyTimer - dt);
            visibility.ValueRW.visibleToEnemyTimer = math.max(0f, visibility.ValueRO.visibleToEnemyTimer - dt);

            if (visibility.ValueRO.visibleToFriendlyTimer <= 0f && visibility.ValueRO.visibleToEnemyTimer <= 0f)
            {
                visibility.ValueRW.visibleToFriendlyTimer = 0f;
                visibility.ValueRW.visibleToEnemyTimer = 0f;
                visibilityEnabled.ValueRW = false;
            }
        }
    }
}
