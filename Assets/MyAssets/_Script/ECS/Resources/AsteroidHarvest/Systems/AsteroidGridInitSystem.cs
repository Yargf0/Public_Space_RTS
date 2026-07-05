using Unity.Collections;
using Unity.Entities;

// creates singleton with AsteroidGridData on world start
// same idea as GridSystemInit
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct AsteroidGridInitSystem : ISystem
{
    private bool initialized;

    public void OnCreate(ref SystemState state)
    {
        initialized = false;
    }

    public void OnUpdate(ref SystemState state)
    {
        if (initialized)
        {
            state.Enabled = false;
            return;
        }

        Entity entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new AsteroidGridData
        {
            Map = new NativeParallelMultiHashMap<Unity.Mathematics.int2, AsteroidGridEntry>(64, Allocator.Persistent),
        });

        initialized = true;
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<AsteroidGridData>(out AsteroidGridData gridData))
        {
            return;
        }

        if (gridData.Map.IsCreated)
        {
            gridData.Map.Dispose();
        }
    }
}