using Unity.Entities;
using Unity.Mathematics;

// sets absolute action timers after spawn
// baked values are 0 but runtime time can be big already
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmbeddedActionVisualInitSystem))]
[UpdateBefore(typeof(EmbeddedActionTargetSystem))]
public partial struct EmbeddedActionRuntimeInitSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float now = (float)SystemAPI.Time.ElapsedTime;
        EntityCommandBuffer ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        bool changed = false;

        foreach ((DynamicBuffer<EmbeddedActionSlot> actions, Entity shipEntity) in
                 SystemAPI.Query<DynamicBuffer<EmbeddedActionSlot>>()
                     .WithAll<EmbeddedWeaponHost>()
                     .WithNone<EmbeddedActionRuntimeInitialized>()
                     .WithEntityAccess())
        {
            DynamicBuffer<EmbeddedActionSlot> actionBuffer = actions;

            for (int i = 0; i < actionBuffer.Length; i++)
            {
                EmbeddedActionSlot action = actionBuffer[i];

                uint baseHash = math.hash(new uint3((uint)shipEntity.Index, (uint)shipEntity.Version, (uint)(i + 1)));
                float tickInterval = math.max(0.01f, action.tickInterval);
                float searchInterval = math.max(0.01f, action.searchInterval);
                float visualInterval = math.max(0.01f, action.visualInterval);
                float aimInterval = math.max(0.01f, action.aimInterval > 0f ? action.aimInterval : (1f / 30f));

                float tickOffset = Hash01(baseHash) * math.min(tickInterval, 0.30f);
                float searchOffset = Hash01(baseHash ^ 0x9E3779B9u) * math.min(searchInterval, 0.50f);
                float visualOffset = Hash01(baseHash ^ 0x85EBCA6Bu) * math.min(visualInterval, 0.30f);
                float aimOffset = Hash01(baseHash ^ 0xC2B2AE35u) * math.min(aimInterval, 0.20f);

                // first tick waits full interval, no fast first tick after spawn
                action.timer = now + tickInterval + tickOffset;
                action.searchTimer = now + searchOffset;
                action.visualTimer = now + visualOffset;
                action.aimInterval = aimInterval;
                action.aimTimer = now + aimOffset;
                action.scanCursor = (int)(baseHash & 0x3FFFFFFFu);

                actionBuffer[i] = action;
            }

            EmbeddedActionHostRuntime hostRuntime = EmbeddedActionRuntimeUtility.BuildHostRuntimeFromActions(actionBuffer);

            if (state.EntityManager.HasComponent<EmbeddedActionHostRuntime>(shipEntity))
            {
                ecb.SetComponent(shipEntity, hostRuntime);
            }
            else
            {
                ecb.AddComponent(shipEntity, hostRuntime);
            }

            ecb.AddComponent<EmbeddedActionRuntimeInitialized>(shipEntity);

            // track dead->alive respawn. if already dead keep wasDead=0 so ResetSystem clears stale targets
            if (state.EntityManager.HasComponent<EmbeddedActionRuntimeDeadState>(shipEntity))
            {
                ecb.SetComponent(shipEntity, new EmbeddedActionRuntimeDeadState { wasDead = 0 });
            }
            else
            {
                ecb.AddComponent(shipEntity, new EmbeddedActionRuntimeDeadState { wasDead = 0 });
            }

            changed = true;
        }

        if (changed)
        {
            ecb.Playback(state.EntityManager);
        }
    }

    private static float Hash01(uint value)
    {
        return (value & 0x00FFFFFFu) / 16777215f;
    }
}
