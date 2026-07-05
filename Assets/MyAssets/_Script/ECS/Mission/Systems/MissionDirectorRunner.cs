using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(MissionDirectorAuthoring))]
public class MissionDirectorRunner : MonoBehaviour
{
    [Tooltip("Trigger evaluation step in seconds. 0 = every frame.")]
    public float evaluateInterval = 0.2f;

    [Header("Logs")]
    [Tooltip("General MissionDirectorRunner info logs.")]
    public bool logInfo = true;

    [Tooltip("Warning logs about invalid mission setup.")]
    public bool logWarnings = true;

    [Tooltip("Log every fired mission event.")]
    public bool logFiredEvents = true;

    [Tooltip("Log when runtime MissionDirector entity is auto-created.")]
    public bool logRuntimeDirectorCreation = true;

    private MissionDirectorAuthoring authoring;
    private MissionCommandExecutionSystem missionCommandSystem;
    private float evalTimer;
    private readonly List<int> eventsToFire = new List<int>(16);

    private Entity runtimeDirectorEntity = Entity.Null;
    private bool createdRuntimeDirectorEntity;

    private void Awake()
    {
        authoring = GetComponent<MissionDirectorAuthoring>();
    }

    private void Start()
    {
        BindActiveScriptToMissionSystem();
    }

    private void OnDestroy()
    {
        if (!createdRuntimeDirectorEntity || runtimeDirectorEntity == Entity.Null) return;
        if (!TryGetEm(out EntityManager em)) return;

        if (em.Exists(runtimeDirectorEntity))
            em.DestroyEntity(runtimeDirectorEntity);

        runtimeDirectorEntity = Entity.Null;
        createdRuntimeDirectorEntity = false;
    }

    private void Update()
    {
        BindActiveScriptToMissionSystem();

        MissionScript script = authoring != null ? authoring.GetScript() : null;
        if (script == null) return;
        if (!TryGetEm(out EntityManager em)) return;

        Entity directorEntity = FindOrCreateSingleDirectorEntity(em);
        if (directorEntity == Entity.Null) return;

        float dt = Time.deltaTime;

        MissionDirectorState state = em.GetComponentData<MissionDirectorState>(directorEntity);
        state.missionTime += dt;
        em.SetComponentData(directorEntity, state);

        if (!state.initialSpawnsDone)
        {
            ResetMissionEventsAndTriggers(em, directorEntity, script);
            EmitInitialSpawnCommands(em, directorEntity, script);

            state = em.GetComponentData<MissionDirectorState>(directorEntity);
            state.initialSpawnsDone = true;
            em.SetComponentData(directorEntity, state);
        }

        evalTimer -= dt;
        if (evalTimer > 0f) return;

        evalTimer = Mathf.Max(0f, evaluateInterval);

        EvaluateEvents(em, directorEntity, script, state.missionTime, dt);
    }

    private void BindActiveScriptToMissionSystem()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            missionCommandSystem = null;
            return;
        }

        missionCommandSystem = world.GetExistingSystemManaged<MissionCommandExecutionSystem>();
        if (missionCommandSystem != null)
            missionCommandSystem.ActiveScript = authoring != null ? authoring.GetScript() : null;
    }

    private void ResetMissionEventsAndTriggers(EntityManager em, Entity directorEntity, MissionScript script)
    {
        if (script == null) return;

        if (em.HasBuffer<MissionEventState>(directorEntity))
        {
            DynamicBuffer<MissionEventState> states = em.GetBuffer<MissionEventState>(directorEntity);
            states.Clear();
        }

        // trigger state lives in MissionEventState buffer, cleared above
    }

    private void EmitInitialSpawnCommands(EntityManager em, Entity directorEntity, MissionScript script)
    {
        if (script.initialSpawnPresetIndexes == null) return;

        for (int i = 0; i < script.initialSpawnPresetIndexes.Length; i++)
        {
            int presetIndex = script.initialSpawnPresetIndexes[i];

            if (script.spawnPresets == null || presetIndex < 0 || presetIndex >= script.spawnPresets.Length)
            {
                if (logWarnings)
                    Debug.LogWarning($"[Mission] Invalid initial spawn preset index={presetIndex}");

                continue;
            }

            MissionSpawnPreset preset = script.spawnPresets[presetIndex];
            if (preset == null || preset.plan == null) continue;

            MissionExecContext execCtx = new MissionExecContext
            {
                em = em,
                missionDirectorEntity = directorEntity,
                missionTime = 0f,
                commandSystem = missionCommandSystem,
            };

            execCtx.EmitCommand(new MissionSpawnGroupCommand
            {
                directorEntity = directorEntity,
                spawnPresetIndex = presetIndex,
            });
        }
    }

    private void EvaluateEvents(EntityManager em, Entity directorEntity, MissionScript script, float missionTime, float dt)
    {
        if (script.events == null) return;

        eventsToFire.Clear();

        DynamicBuffer<MissionEventState> states = em.GetBuffer<MissionEventState>(directorEntity);

        while (states.Length < script.events.Length)
        {
            states.Add(new MissionEventState
            {
                eventIndex = states.Length,
                fired = false,
                fireCount = 0,
                lastFireTime = 0f,
                triggerHasEverExisted = false,
            });
        }

        MissionEvalContext evalCtx = new MissionEvalContext
        {
            em = em,
            missionTime = missionTime,
            deltaTime = dt,
            missionDirectorEntity = directorEntity,
        };

        // first pass: only read/write MissionEventState, no entity creation
        // creating entities while buffer is read breaks its safety handle
        for (int i = 0; i < script.events.Length; i++)
        {
            MissionEvent ev = script.events[i];
            if (ev == null) continue;

            MissionEventState st = states[i];

            if (ev.fireOnce && st.fired) continue;
            if (ev.cooldownSeconds > 0f && st.fireCount > 0 && missionTime - st.lastFireTime < ev.cooldownSeconds) continue;

            bool triggerPassed = ev.trigger.Evaluate(evalCtx, ref st);

            if (!triggerPassed)
            {
                states[i] = st;
                continue;
            }

            st.fired = true;
            st.fireCount++;
            st.lastFireTime = missionTime;

            states[i] = st;
            eventsToFire.Add(i);
        }

        if (eventsToFire.Count == 0) return;

        MissionExecContext execCtx = new MissionExecContext
        {
            em = em,
            missionDirectorEntity = directorEntity,
            missionTime = missionTime,
            commandSystem = missionCommandSystem,
        };

        // second pass: structural changes ok now, buffer is not used anymore
        for (int n = 0; n < eventsToFire.Count; n++)
        {
            int i = eventsToFire[n];
            MissionEvent ev = script.events[i];
            if (ev == null) continue;

            if (ev.actions != null)
            {
                for (int a = 0; a < ev.actions.Length; a++)
                    ev.actions[a].EmitCommand(execCtx);
            }

            if (logInfo && logFiredEvents)
                Debug.Log($"[Mission] Fired event '{ev.label}' at t={missionTime:F1}s");
        }
    }

    private Entity FindOrCreateSingleDirectorEntity(EntityManager em)
    {
        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadOnly<MissionDirectorTag>(),
            ComponentType.ReadOnly<MissionDirectorState>());

        NativeArray<Entity> all = query.ToEntityArray(Allocator.Temp);
        Entity result = all.Length > 0 ? all[0] : Entity.Null;

        if (all.Length > 1 && logWarnings)
            Debug.LogWarning("[MissionDirectorRunner] MVP supports only one MissionDirector in scene.");

        all.Dispose();
        query.Dispose();

        if (result != Entity.Null)
            return result;

        Entity directorEntity = em.CreateEntity(
            ComponentType.ReadWrite<MissionDirectorTag>(),
            ComponentType.ReadWrite<MissionDirectorState>(),
            ComponentType.ReadWrite<MissionEventState>());

        em.SetComponentData(directorEntity, new MissionDirectorState
        {
            missionTime = 0f,
            initialSpawnsDone = false,
        });

        runtimeDirectorEntity = directorEntity;
        createdRuntimeDirectorEntity = true;

        if (logInfo && logRuntimeDirectorCreation)
            Debug.Log("[MissionDirectorRunner] Created runtime MissionDirector entity. SubScene MissionDirectorAuthoring is not required.");

        return directorEntity;
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
}