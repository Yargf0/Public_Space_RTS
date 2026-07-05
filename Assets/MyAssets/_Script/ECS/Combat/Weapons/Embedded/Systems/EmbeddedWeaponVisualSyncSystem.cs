using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedWeaponAimSystem))]
public partial struct EmbeddedWeaponVisualSyncSystem : ISystem
{
    private ComponentLookup<LocalTransform> localTransformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        localTransformLookup = state.GetComponentLookup<LocalTransform>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        localTransformLookup.Update(ref state);

        foreach ((DynamicBuffer<EmbeddedWeaponSlot> slots,
                  DynamicBuffer<EmbeddedWeaponVisualSlot> visuals)
            in SystemAPI.Query<DynamicBuffer<EmbeddedWeaponSlot>, DynamicBuffer<EmbeddedWeaponVisualSlot>>()
                .WithAll<EmbeddedWeaponHost>())
        {
            int count = math.min(slots.Length, visuals.Length);
            for (int i = 0; i < count; i++)
            {
                EmbeddedWeaponVisualSlot visual = visuals[i];
                if (visual.visualEntity == Entity.Null ||
                    (visual.flags & EmbeddedWeaponVisualSlotFlags.Rotate) == 0 ||
                    !localTransformLookup.HasComponent(visual.visualEntity))
                {
                    continue;
                }

                EmbeddedWeaponSlot slot = slots[i];
                float deltaAngle = CombatUtility.NormalizeAngleRad(slot.currentLocalAngle - slot.baseLocalAngle);

                LocalTransform visualTransform = localTransformLookup[visual.visualEntity];

                float3 visualLocalPosition = visualTransform.Position;
                visualLocalPosition.z = GameConstants.EmbeddedWeaponVisualLocalZ;
                visualTransform.Position = visualLocalPosition;

                visualTransform.Rotation = math.mul(visual.baseLocalRotation, quaternion.RotateZ(deltaAngle));
                localTransformLookup[visual.visualEntity] = visualTransform;
            }
        }
    }
}
