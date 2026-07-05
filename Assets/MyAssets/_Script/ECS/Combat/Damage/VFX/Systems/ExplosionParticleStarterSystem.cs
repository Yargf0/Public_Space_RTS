using Unity.Burst;
using Unity.Entities;
using UnityEngine;

partial struct ExplosionParticleStarterSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        //EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(state.WorldUpdateAllocator);
        foreach ((
                    RefRW<ExplosionParticleStarter> explosionParticleStarter,
                    RefRW<SelfDeleter> selfDeleter,
                    RefRW<BlowRadius> blowRadius,
                    Entity entity)
                    in SystemAPI.Query<
                        RefRW<ExplosionParticleStarter>,
                        RefRW<SelfDeleter>,
                        RefRW<BlowRadius>>().WithEntityAccess())
        {
            if (blowRadius.ValueRO.firstTime)
            {
                blowRadius.ValueRW.firstTime = false;
                ParticleSystem sparkParticleSystem = state.EntityManager.GetComponentObject<ParticleSystem>(explosionParticleStarter.ValueRO.sparkEntity);
                ParticleSystem flashParticleSystem = state.EntityManager.GetComponentObject<ParticleSystem>(explosionParticleStarter.ValueRO.flashEntity);
                ParticleSystem fireParticleSystem = state.EntityManager.GetComponentObject<ParticleSystem>(explosionParticleStarter.ValueRO.fireEntity);
                ParticleSystem smokeParticleSystem = state.EntityManager.GetComponentObject<ParticleSystem>(explosionParticleStarter.ValueRO.smokeEntity);

                ConfigureSparkBurst(sparkParticleSystem, blowRadius.ValueRO.blowRadius);
                ConfigureSparkBurst(fireParticleSystem, blowRadius.ValueRO.blowRadius);
                ConfigureSparkBurst(smokeParticleSystem, blowRadius.ValueRO.blowRadius);

                var flashMain = flashParticleSystem.main;
                flashMain.startSize = blowRadius.ValueRO.blowRadius * 5f;

                sparkParticleSystem.Play();
                flashParticleSystem.Play();
                fireParticleSystem.Play();
                smokeParticleSystem.Play();
                selfDeleter.ValueRW.LifeTime = 1f;
            }
        }

        //entityCommandBuffer.Playback(state.EntityManager);
    }

    public void ConfigureSparkBurst(ParticleSystem particleSystem, float blowRadius)
    {
        // particle shape radius
        var sparkShape = particleSystem.shape;
        sparkShape.radius = blowRadius * 0.2f;

        // emission params
        var sparkEmission = particleSystem.emission;
        ParticleSystem.Burst[] sparkBursts = new ParticleSystem.Burst[sparkEmission.burstCount];

        sparkEmission.GetBursts(sparkBursts);

        ParticleSystem.Burst firstBurst = sparkBursts[0];
        firstBurst.count = new ParticleSystem.MinMaxCurve(
            min: 15f * blowRadius,
            max: 25f * blowRadius
        );
        sparkBursts[0] = firstBurst;
        sparkEmission.SetBursts(sparkBursts);       
    }
}
