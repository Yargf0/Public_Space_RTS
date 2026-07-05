using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;

using EntityHash128 = Unity.Entities.Hash128;

public class AiDemoSubSceneSwitcherV3 : MonoBehaviour
{
    [Serializable]
    public class DemoSceneEntry
    {
        [Header("Scene")]
        public string displayName;

        [Tooltip("SubScene object from master scene. Set Auto Load Scene = false on this SubScene.")]
        public SubScene subScene;

        [Header("Linked Objects")]
        [Tooltip("Ordinary GameObjects enabled only while this demo scene is active: UI text, markers, camera points, etc.")]
        public GameObject[] objectsToEnable;

        [Header("Camera")]
        [Tooltip("If true, camera position XY and orthographic size will be applied when this demo scene becomes active.")]
        public bool applyCameraSettings = false;

        [Tooltip("Camera position for this demo scene. Only X/Y are applied. Camera Z is preserved.")]
        public Vector2 cameraPositionXY;

        [Tooltip("Orthographic camera size for this demo scene.")]
        [Min(0.01f)]
        public float cameraOrthographicSize = 20f;
    }

    [Header("Demo Scenes")]
    [SerializeField] private DemoSceneEntry[] demoScenes;

    [Header("Input")]
    [SerializeField] private KeyCode previousKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode nextKey = KeyCode.RightArrow;
    [SerializeField] private KeyCode reloadKey = KeyCode.R;

    [Header("Startup")]
    [SerializeField] private bool loadOnStart = true;
    [SerializeField] private int startIndex = 0;

    [Header("Loading")]
    [Tooltip("If true, SceneSystem waits until scene is fully streamed in during LoadSceneAsync.")]
    [SerializeField] private bool blockOnStreamIn = false;

    [Header("Safe Switching")]
    [SerializeField] private bool blockInputWhileSwitching = true;

    [Tooltip("Frames before unload request.")]
    [SerializeField] private int framesBeforeUnload = 1;

    [Tooltip("Frames after unload request before loading next scene.")]
    [SerializeField] private int framesAfterUnload = 3;

    [Tooltip("Frames after scene becomes loaded before helper GameObjects are enabled.")]
    [SerializeField] private int framesAfterLoad = 1;

    [Tooltip("Safety guard so switcher cannot wait forever if scene state is weird.")]
    [SerializeField] private int maxWaitFrames = 300;

    [Header("Camera Switching")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool fallbackToMainCamera = true;

    [Header("Debug")]
    [SerializeField] private bool logSwitching = true;

    private World world;
    private EntityManager entityManager;
    private Entity runtimeEntity = Entity.Null;

    private int lastObservedSwitchSerial = -1;
    private int lastEnabledIndex = -999;
    private bool linkedObjectsAreDisabled = true;

    private void Awake()
    {
        DisableAllLinkedObjects();
    }

    private void Start()
    {
        EnsureRuntimeEntity();

        if (loadOnStart && demoScenes != null && demoScenes.Length > 0)
        {
            int safeIndex = Mathf.Clamp(startIndex, 0, demoScenes.Length - 1);
            RequestLoadIndex(safeIndex, true);
        }
    }

    private void Update()
    {
        if (!EnsureRuntimeEntity())
            return;

        AiDemoSubSceneSwitcherRuntime runtime = entityManager.GetComponentData<AiDemoSubSceneSwitcherRuntime>(runtimeEntity);

        SyncLinkedObjects(runtime);

        if (blockInputWhileSwitching && (runtime.isSwitching != 0 || runtime.hasRequest != 0))
            return;

        if (Input.GetKeyDown(previousKey))
        {
            RequestLoadPrevious();
        }

        if (Input.GetKeyDown(nextKey))
        {
            RequestLoadNext();
        }

        if (Input.GetKeyDown(reloadKey))
        {
            RequestReloadCurrent();
        }
    }

    public void RequestLoadNext()
    {
        if (!EnsureRuntimeEntity())
            return;

        AiDemoSubSceneSwitcherRuntime runtime = entityManager.GetComponentData<AiDemoSubSceneSwitcherRuntime>(runtimeEntity);

        int currentIndex = runtime.currentIndex;
        if (currentIndex < 0)
            currentIndex = startIndex;

        int nextIndex = currentIndex + 1;
        if (nextIndex >= demoScenes.Length)
            nextIndex = 0;

        RequestLoadIndex(nextIndex, false);
    }

    public void RequestLoadPrevious()
    {
        if (!EnsureRuntimeEntity())
            return;

        AiDemoSubSceneSwitcherRuntime runtime = entityManager.GetComponentData<AiDemoSubSceneSwitcherRuntime>(runtimeEntity);

        int currentIndex = runtime.currentIndex;
        if (currentIndex < 0)
            currentIndex = startIndex;

        int previousIndex = currentIndex - 1;
        if (previousIndex < 0)
            previousIndex = demoScenes.Length - 1;

        RequestLoadIndex(previousIndex, false);
    }

    public void RequestReloadCurrent()
    {
        if (!EnsureRuntimeEntity())
            return;

        AiDemoSubSceneSwitcherRuntime runtime = entityManager.GetComponentData<AiDemoSubSceneSwitcherRuntime>(runtimeEntity);

        int indexToReload = runtime.currentIndex;
        if (indexToReload < 0)
            indexToReload = Mathf.Clamp(startIndex, 0, demoScenes.Length - 1);

        RequestLoadIndex(indexToReload, true);
    }

    public void RequestLoadIndex(int index)
    {
        RequestLoadIndex(index, false);
    }

    public void RequestUnloadCurrent()
    {
        if (!EnsureRuntimeEntity())
            return;

        DisableAllLinkedObjects();

        AiDemoSubSceneSwitcherRuntime runtime = entityManager.GetComponentData<AiDemoSubSceneSwitcherRuntime>(runtimeEntity);

        if (blockInputWhileSwitching && (runtime.isSwitching != 0 || runtime.hasRequest != 0))
            return;

        runtime.requestedIndex = -1;
        runtime.forceReload = 1;
        runtime.hasRequest = 1;

        CopySettingsToRuntime(ref runtime);

        entityManager.SetComponentData(runtimeEntity, runtime);
    }

    private void RequestLoadIndex(int index, bool forceReload)
    {
        if (!EnsureRuntimeEntity())
            return;

        if (demoScenes == null || demoScenes.Length == 0)
            return;

        if (index < 0 || index >= demoScenes.Length)
        {
            Debug.LogWarning($"AiDemoSubSceneSwitcherV3: index {index} is out of range.");
            return;
        }

        DemoSceneEntry entry = demoScenes[index];
        if (entry == null || entry.subScene == null)
        {
            Debug.LogWarning($"AiDemoSubSceneSwitcherV3: scene entry {index} is null or has no SubScene.");
            return;
        }

        AiDemoSubSceneSwitcherRuntime runtime = entityManager.GetComponentData<AiDemoSubSceneSwitcherRuntime>(runtimeEntity);

        if (blockInputWhileSwitching && (runtime.isSwitching != 0 || runtime.hasRequest != 0))
            return;

        if (!forceReload && runtime.isLoaded != 0 && runtime.currentIndex == index)
            return;

        DisableAllLinkedObjects();

        runtime.requestedIndex = index;
        runtime.forceReload = forceReload ? (byte)1 : (byte)0;
        runtime.hasRequest = 1;

        CopySettingsToRuntime(ref runtime);

        entityManager.SetComponentData(runtimeEntity, runtime);
    }

    private bool EnsureRuntimeEntity()
    {
        if (world == null || !world.IsCreated)
        {
            world = World.DefaultGameObjectInjectionWorld;
        }

        if (world == null || !world.IsCreated)
            return false;

        entityManager = world.EntityManager;

        if (runtimeEntity != Entity.Null && entityManager.Exists(runtimeEntity))
            return true;

        DestroyOldRuntimeEntities();

        runtimeEntity = entityManager.CreateEntity(typeof(AiDemoSubSceneSwitcherRuntime));
        DynamicBuffer<AiDemoSubSceneEntryElement> buffer =
            entityManager.AddBuffer<AiDemoSubSceneEntryElement>(runtimeEntity);

        RebuildSceneBuffer(buffer);

        AiDemoSubSceneSwitcherRuntime runtime = new AiDemoSubSceneSwitcherRuntime
        {
            currentIndex = -1,
            requestedIndex = -1,
            targetIndex = -1,
            currentSceneEntity = Entity.Null,
            unloadingSceneEntity = Entity.Null,
            hasRequest = 0,
            forceReload = 0,
            isSwitching = 0,
            isLoaded = 0,
            phase = AiDemoSubSceneSwitchPhase.Idle,
            framesRemaining = 0,
            waitGuardFrames = 0,
            switchSerial = 0,
        };

        CopySettingsToRuntime(ref runtime);
        entityManager.SetComponentData(runtimeEntity, runtime);

        return true;
    }

    private void DestroyOldRuntimeEntities()
    {
        EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AiDemoSubSceneSwitcherRuntime>());
        NativeArray<Entity> oldEntities = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < oldEntities.Length; i++)
        {
            if (entityManager.Exists(oldEntities[i]))
            {
                entityManager.DestroyEntity(oldEntities[i]);
            }
        }

        oldEntities.Dispose();
        query.Dispose();
    }

    private void RebuildSceneBuffer(DynamicBuffer<AiDemoSubSceneEntryElement> buffer)
    {
        buffer.Clear();

        if (demoScenes == null)
            return;

        for (int i = 0; i < demoScenes.Length; i++)
        {
            DemoSceneEntry entry = demoScenes[i];

            EntityHash128 sceneGuid = default;

            if (entry != null && entry.subScene != null)
            {
                sceneGuid = entry.subScene.SceneGUID;
            }
            else
            {
                Debug.LogWarning($"AiDemoSubSceneSwitcherV3: scene entry {i} has no SubScene.");
            }

            buffer.Add(new AiDemoSubSceneEntryElement
            {
                sceneGuid = sceneGuid
            });
        }
    }

    private void CopySettingsToRuntime(ref AiDemoSubSceneSwitcherRuntime runtime)
    {
        runtime.blockOnStreamIn = blockOnStreamIn ? (byte)1 : (byte)0;
        runtime.framesBeforeUnload = Mathf.Max(0, framesBeforeUnload);
        runtime.framesAfterUnload = Mathf.Max(0, framesAfterUnload);
        runtime.framesAfterLoad = Mathf.Max(0, framesAfterLoad);
        runtime.maxWaitFrames = Mathf.Max(1, maxWaitFrames);
        runtime.logSwitching = logSwitching ? (byte)1 : (byte)0;
    }

    private void SyncLinkedObjects(AiDemoSubSceneSwitcherRuntime runtime)
    {
        if (runtime.isSwitching != 0 || runtime.hasRequest != 0 || runtime.isLoaded == 0)
        {
            if (!linkedObjectsAreDisabled)
            {
                DisableAllLinkedObjects();
            }

            return;
        }

        if (runtime.currentIndex < 0 || demoScenes == null || runtime.currentIndex >= demoScenes.Length)
        {
            if (!linkedObjectsAreDisabled)
            {
                DisableAllLinkedObjects();
            }

            return;
        }

        if (runtime.switchSerial == lastObservedSwitchSerial && lastEnabledIndex == runtime.currentIndex)
            return;

        DisableAllLinkedObjects();

        DemoSceneEntry activeEntry = demoScenes[runtime.currentIndex];
        EnableObjects(activeEntry);
        ApplyCameraSettings(activeEntry);

        lastObservedSwitchSerial = runtime.switchSerial;
        lastEnabledIndex = runtime.currentIndex;
        linkedObjectsAreDisabled = false;
    }

    private void ApplyCameraSettings(DemoSceneEntry entry)
    {
        if (entry == null || !entry.applyCameraSettings)
            return;

        Camera cameraToUse = targetCamera;

        if (cameraToUse == null && fallbackToMainCamera)
        {
            cameraToUse = Camera.main;
        }

        if (cameraToUse == null)
        {
            Debug.LogWarning("AiDemoSubSceneSwitcherV3: camera settings requested, but no target camera found.");
            return;
        }

        Transform cameraTransform = cameraToUse.transform;
        Vector3 currentPosition = cameraTransform.position;

        cameraTransform.position = new Vector3(
            entry.cameraPositionXY.x,
            entry.cameraPositionXY.y,
            currentPosition.z);

        if (!cameraToUse.orthographic)
        {
            Debug.LogWarning("AiDemoSubSceneSwitcherV3: target camera is not orthographic. Orthographic size was not applied.");
            return;
        }

        cameraToUse.orthographicSize = Mathf.Max(0.01f, entry.cameraOrthographicSize);
    }

    private void EnableObjects(DemoSceneEntry entry)
    {
        if (entry == null || entry.objectsToEnable == null)
            return;

        for (int i = 0; i < entry.objectsToEnable.Length; i++)
        {
            GameObject obj = entry.objectsToEnable[i];

            if (obj == null)
                continue;

            if (obj == gameObject)
            {
                Debug.LogWarning("AiDemoSubSceneSwitcherV3: do not add switcher object itself to objectsToEnable.");
                continue;
            }

            obj.SetActive(true);
        }
    }

    private void DisableAllLinkedObjects()
    {
        if (demoScenes == null)
            return;

        for (int sceneIndex = 0; sceneIndex < demoScenes.Length; sceneIndex++)
        {
            DemoSceneEntry entry = demoScenes[sceneIndex];

            if (entry == null || entry.objectsToEnable == null)
                continue;

            for (int objectIndex = 0; objectIndex < entry.objectsToEnable.Length; objectIndex++)
            {
                GameObject obj = entry.objectsToEnable[objectIndex];

                if (obj == null)
                    continue;

                if (obj == gameObject)
                    continue;

                obj.SetActive(false);
            }
        }

        linkedObjectsAreDisabled = true;
        lastEnabledIndex = -999;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        framesBeforeUnload = Mathf.Max(0, framesBeforeUnload);
        framesAfterUnload = Mathf.Max(0, framesAfterUnload);
        framesAfterLoad = Mathf.Max(0, framesAfterLoad);
        maxWaitFrames = Mathf.Max(1, maxWaitFrames);

        if (demoScenes == null || demoScenes.Length == 0)
        {
            startIndex = 0;
            return;
        }

        startIndex = Mathf.Clamp(startIndex, 0, demoScenes.Length - 1);

        for (int i = 0; i < demoScenes.Length; i++)
        {
            DemoSceneEntry entry = demoScenes[i];

            if (entry == null)
                continue;

            entry.cameraOrthographicSize = Mathf.Max(0.01f, entry.cameraOrthographicSize);
        }
    }
#endif
}

public struct AiDemoSubSceneEntryElement : IBufferElementData
{
    public EntityHash128 sceneGuid;
}

public struct AiDemoSubSceneSwitcherRuntime : IComponentData
{
    public int currentIndex;
    public int requestedIndex;
    public int targetIndex;

    public Entity currentSceneEntity;
    public Entity unloadingSceneEntity;

    public byte hasRequest;
    public byte forceReload;
    public byte isSwitching;
    public byte isLoaded;

    public AiDemoSubSceneSwitchPhase phase;

    public int framesRemaining;
    public int waitGuardFrames;

    public int framesBeforeUnload;
    public int framesAfterUnload;
    public int framesAfterLoad;
    public int maxWaitFrames;

    public byte blockOnStreamIn;
    public byte logSwitching;

    public int switchSerial;
}

public enum AiDemoSubSceneSwitchPhase : byte
{
    Idle = 0,
    WaitBeforeUnload = 1,
    UnloadCurrent = 2,
    WaitAfterUnload = 3,
    WaitUntilUnloaded = 4,
    LoadNext = 5,
    WaitUntilLoaded = 6,
    WaitAfterLoad = 7,
    Complete = 8,
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct AiDemoSubSceneSwitchSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AiDemoSubSceneSwitcherRuntime>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity switcherEntity = SystemAPI.GetSingletonEntity<AiDemoSubSceneSwitcherRuntime>();
        EntityManager em = state.EntityManager;

        AiDemoSubSceneSwitcherRuntime runtime =
            em.GetComponentData<AiDemoSubSceneSwitcherRuntime>(switcherEntity);

        DynamicBuffer<AiDemoSubSceneEntryElement> scenes =
            em.GetBuffer<AiDemoSubSceneEntryElement>(switcherEntity);

        if (runtime.hasRequest != 0 && runtime.isSwitching == 0)
        {
            BeginSwitch(ref runtime, scenes.Length);
            em.SetComponentData(switcherEntity, runtime);
            return;
        }

        if (runtime.isSwitching == 0)
            return;

        switch (runtime.phase)
        {
            case AiDemoSubSceneSwitchPhase.WaitBeforeUnload:
                {
                    if (TickFrames(ref runtime))
                        break;

                    runtime.phase = AiDemoSubSceneSwitchPhase.UnloadCurrent;
                    break;
                }

            case AiDemoSubSceneSwitchPhase.UnloadCurrent:
                {
                    UnloadCurrent(ref state, ref runtime);

                    runtime.framesRemaining = runtime.framesAfterUnload;
                    runtime.waitGuardFrames = runtime.maxWaitFrames;
                    runtime.phase = AiDemoSubSceneSwitchPhase.WaitAfterUnload;
                    break;
                }

            case AiDemoSubSceneSwitchPhase.WaitAfterUnload:
                {
                    if (TickFrames(ref runtime))
                        break;

                    runtime.phase = AiDemoSubSceneSwitchPhase.WaitUntilUnloaded;
                    break;
                }

            case AiDemoSubSceneSwitchPhase.WaitUntilUnloaded:
                {
                    if (!IsOldSceneGone(ref state, ref runtime))
                        break;

                    if (runtime.targetIndex < 0)
                    {
                        runtime.phase = AiDemoSubSceneSwitchPhase.Complete;
                        break;
                    }

                    runtime.phase = AiDemoSubSceneSwitchPhase.LoadNext;
                    break;
                }

            case AiDemoSubSceneSwitchPhase.LoadNext:
                {
                    LoadNext(ref state, ref runtime, scenes);
                    runtime.waitGuardFrames = runtime.maxWaitFrames;
                    runtime.phase = AiDemoSubSceneSwitchPhase.WaitUntilLoaded;
                    break;
                }

            case AiDemoSubSceneSwitchPhase.WaitUntilLoaded:
                {
                    if (!IsCurrentSceneLoaded(ref state, ref runtime))
                        break;

                    runtime.framesRemaining = runtime.framesAfterLoad;
                    runtime.phase = AiDemoSubSceneSwitchPhase.WaitAfterLoad;
                    break;
                }

            case AiDemoSubSceneSwitchPhase.WaitAfterLoad:
                {
                    if (TickFrames(ref runtime))
                        break;

                    runtime.phase = AiDemoSubSceneSwitchPhase.Complete;
                    break;
                }

            case AiDemoSubSceneSwitchPhase.Complete:
                {
                    CompleteSwitch(ref runtime);
                    break;
                }

            default:
                {
                    FailSwitch(ref runtime);
                    break;
                }
        }

        em.SetComponentData(switcherEntity, runtime);
    }

    private static void BeginSwitch(ref AiDemoSubSceneSwitcherRuntime runtime, int sceneCount)
    {
        int requestedIndex = runtime.requestedIndex;

        runtime.hasRequest = 0;

        if (requestedIndex >= sceneCount)
        {
            if (runtime.logSwitching != 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"AiDemoSubSceneSwitchSystem: requested index {requestedIndex} is out of range. Scene count: {sceneCount}.");
            }

            runtime.requestedIndex = -1;
            return;
        }

        if (requestedIndex < -1)
        {
            runtime.requestedIndex = -1;
            return;
        }

        if (requestedIndex == runtime.currentIndex &&
            runtime.forceReload == 0 &&
            runtime.isLoaded != 0)
        {
            runtime.requestedIndex = -1;
            return;
        }

        runtime.targetIndex = requestedIndex;
        runtime.isSwitching = 1;
        runtime.isLoaded = 0;
        runtime.framesRemaining = runtime.framesBeforeUnload;
        runtime.waitGuardFrames = runtime.maxWaitFrames;
        runtime.phase = AiDemoSubSceneSwitchPhase.WaitBeforeUnload;

        if (runtime.logSwitching != 0)
        {
            UnityEngine.Debug.Log($"AiDemoSubSceneSwitchSystem: switch started. Target index: {runtime.targetIndex}");
        }
    }

    private static void UnloadCurrent(ref SystemState state, ref AiDemoSubSceneSwitcherRuntime runtime)
    {
        runtime.unloadingSceneEntity = runtime.currentSceneEntity;

        if (runtime.currentSceneEntity != Entity.Null &&
            state.EntityManager.Exists(runtime.currentSceneEntity))
        {
            SceneSystem.UnloadScene(
                state.WorldUnmanaged,
                runtime.currentSceneEntity,
                SceneSystem.UnloadParameters.DestroyMetaEntities);

            if (runtime.logSwitching != 0)
            {
                UnityEngine.Debug.Log("AiDemoSubSceneSwitchSystem: unload requested.");
            }
        }

        runtime.currentSceneEntity = Entity.Null;
        runtime.currentIndex = -1;
    }

    private static bool IsOldSceneGone(ref SystemState state, ref AiDemoSubSceneSwitcherRuntime runtime)
    {
        if (runtime.unloadingSceneEntity == Entity.Null)
            return true;

        if (!state.EntityManager.Exists(runtime.unloadingSceneEntity))
        {
            runtime.unloadingSceneEntity = Entity.Null;
            return true;
        }

        if (runtime.waitGuardFrames <= 0)
        {
            if (runtime.logSwitching != 0)
            {
                UnityEngine.Debug.LogWarning("AiDemoSubSceneSwitchSystem: unload wait guard reached. Continuing.");
            }

            runtime.unloadingSceneEntity = Entity.Null;
            return true;
        }

        runtime.waitGuardFrames--;

        SceneSystem.SceneStreamingState streamingState =
            SceneSystem.GetSceneStreamingState(state.WorldUnmanaged, runtime.unloadingSceneEntity);

        if (streamingState == SceneSystem.SceneStreamingState.Unloaded)
        {
            runtime.unloadingSceneEntity = Entity.Null;
            return true;
        }

        if (streamingState != SceneSystem.SceneStreamingState.Unloading)
        {
            runtime.unloadingSceneEntity = Entity.Null;
            return true;
        }

        return false;
    }

    private static void LoadNext(
        ref SystemState state,
        ref AiDemoSubSceneSwitcherRuntime runtime,
        DynamicBuffer<AiDemoSubSceneEntryElement> scenes)
    {
        if (runtime.targetIndex < 0 || runtime.targetIndex >= scenes.Length)
        {
            runtime.phase = AiDemoSubSceneSwitchPhase.Complete;
            return;
        }

        EntityHash128 sceneGuid = scenes[runtime.targetIndex].sceneGuid;

        if (sceneGuid.Equals(default(EntityHash128)))
        {
            if (runtime.logSwitching != 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"AiDemoSubSceneSwitchSystem: scene GUID at index {runtime.targetIndex} is empty.");
            }

            FailSwitch(ref runtime);
            return;
        }

        SceneSystem.LoadParameters loadParameters = default;

        if (runtime.blockOnStreamIn != 0)
        {
            loadParameters.Flags = SceneLoadFlags.BlockOnStreamIn;
        }

        runtime.currentSceneEntity =
            SceneSystem.LoadSceneAsync(state.WorldUnmanaged, sceneGuid, loadParameters);

        if (runtime.logSwitching != 0)
        {
            UnityEngine.Debug.Log($"AiDemoSubSceneSwitchSystem: load requested. Target index: {runtime.targetIndex}");
        }
    }

    private static bool IsCurrentSceneLoaded(ref SystemState state, ref AiDemoSubSceneSwitcherRuntime runtime)
    {
        if (runtime.currentSceneEntity == Entity.Null)
        {
            FailSwitch(ref runtime);
            return true;
        }

        if (!state.EntityManager.Exists(runtime.currentSceneEntity))
        {
            if (runtime.waitGuardFrames <= 0)
            {
                if (runtime.logSwitching != 0)
                {
                    UnityEngine.Debug.LogWarning("AiDemoSubSceneSwitchSystem: load wait guard reached, but scene entity does not exist.");
                }

                FailSwitch(ref runtime);
                return true;
            }

            runtime.waitGuardFrames--;
            return false;
        }

        SceneSystem.SceneStreamingState streamingState =
            SceneSystem.GetSceneStreamingState(state.WorldUnmanaged, runtime.currentSceneEntity);

        if (streamingState == SceneSystem.SceneStreamingState.LoadedSuccessfully)
            return true;

        if (SceneSystem.IsSceneLoaded(state.WorldUnmanaged, runtime.currentSceneEntity))
            return true;

        if (streamingState == SceneSystem.SceneStreamingState.FailedLoadingSceneHeader ||
            streamingState == SceneSystem.SceneStreamingState.LoadedWithSectionErrors)
        {
            if (runtime.logSwitching != 0)
            {
                UnityEngine.Debug.LogError($"AiDemoSubSceneSwitchSystem: scene failed to load. State: {streamingState}");
            }

            FailSwitch(ref runtime);
            return true;
        }

        if (runtime.waitGuardFrames <= 0)
        {
            if (runtime.logSwitching != 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"AiDemoSubSceneSwitchSystem: load wait guard reached. State: {streamingState}");
            }

            return true;
        }

        runtime.waitGuardFrames--;
        return false;
    }

    private static void CompleteSwitch(ref AiDemoSubSceneSwitcherRuntime runtime)
    {
        runtime.currentIndex = runtime.targetIndex;
        runtime.requestedIndex = -1;
        runtime.targetIndex = -1;
        runtime.forceReload = 0;
        runtime.isSwitching = 0;
        runtime.isLoaded = runtime.currentSceneEntity != Entity.Null ? (byte)1 : (byte)0;
        runtime.phase = AiDemoSubSceneSwitchPhase.Idle;
        runtime.switchSerial++;

        if (runtime.logSwitching != 0)
        {
            UnityEngine.Debug.Log($"AiDemoSubSceneSwitchSystem: switch complete. Current index: {runtime.currentIndex}");
        }
    }

    private static void FailSwitch(ref AiDemoSubSceneSwitcherRuntime runtime)
    {
        runtime.requestedIndex = -1;
        runtime.targetIndex = -1;
        runtime.forceReload = 0;
        runtime.isSwitching = 0;
        runtime.isLoaded = 0;
        runtime.phase = AiDemoSubSceneSwitchPhase.Idle;
        runtime.currentSceneEntity = Entity.Null;
        runtime.unloadingSceneEntity = Entity.Null;
    }

    private static bool TickFrames(ref AiDemoSubSceneSwitcherRuntime runtime)
    {
        if (runtime.framesRemaining <= 0)
            return false;

        runtime.framesRemaining--;
        return true;
    }
}
