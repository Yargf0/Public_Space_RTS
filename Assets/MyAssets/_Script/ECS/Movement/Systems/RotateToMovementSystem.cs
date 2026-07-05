using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MoveVelocitySystem))]
partial struct RotateToMovementSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach ((
            RefRO<UnitMover> unitMover,
            RefRW<LocalTransform> localTransform,
            RefRO<Velocity> velocity)
            in SystemAPI.Query<
                RefRO<UnitMover>,
                RefRW<LocalTransform>,
                RefRO<Velocity>>())
        {
            float2 v = velocity.ValueRO.velocity;

            if (!math.isfinite(v.x) || !math.isfinite(v.y)) { continue; }
            if (math.lengthsq(v) < 0.0001f) { continue; }

            float rotationSpeed = unitMover.ValueRO.rotationSpeed;
            if (!math.isfinite(rotationSpeed) || rotationSpeed <= 0.0001f) { continue; }

            float desiredAngle = math.atan2(v.y, v.x);

            float3 currentForward3 = math.mul(localTransform.ValueRO.Rotation, new float3(0f, 1f, 0f));
            float2 currentForward = math.normalizesafe(currentForward3.xy, new float2(0f, 1f));
            float currentAngle = math.atan2(currentForward.y, currentForward.x);

            float deltaAngle = WrapRadians(desiredAngle - currentAngle);
            float maxStep = rotationSpeed * dt;
            float step = math.clamp(deltaAngle, -maxStep, maxStep);
            float newAngle = currentAngle + step;

            localTransform.ValueRW.Rotation = quaternion.RotateZ(newAngle - math.PI / 2f);
        }
    }

    private static float WrapRadians(float angle)
    {
        return math.atan2(math.sin(angle), math.cos(angle));
    }
}
