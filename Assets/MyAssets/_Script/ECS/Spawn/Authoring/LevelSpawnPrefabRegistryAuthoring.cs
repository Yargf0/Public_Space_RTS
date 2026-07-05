using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// scene registry: entity prefabs by string id
// mission scripts can ask prefab without storing Entity refs
public class LevelSpawnPrefabRegistryAuthoring : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("String prefab id used by level scripts.")]
        public string id;

        [Tooltip("GameObject with ship or squad-member authoring components.")]
        public GameObject prefab;
    }

    [Tooltip("Prefabs available to level scripts by id.")]
    public Entry[] entries;

    class Baker : Baker<LevelSpawnPrefabRegistryAuthoring>
    {
        public override void Bake(LevelSpawnPrefabRegistryAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new LevelPrefabRegistryTag());

            DynamicBuffer<LevelPrefabElement> buffer = AddBuffer<LevelPrefabElement>(entity);

            if (authoring.entries == null)
            {
                return;
            }

            for (int i = 0; i < authoring.entries.Length; i++)
            {
                Entry src = authoring.entries[i];
                if (src == null || src.prefab == null || string.IsNullOrEmpty(src.id))
                {
                    continue;
                }

                buffer.Add(new LevelPrefabElement
                {
                    // FixedString32 is enough for short ids
                    id = new FixedString32Bytes(src.id),
                    prefab = GetEntity(src.prefab, TransformUsageFlags.Dynamic),
                });
            }
        }
    }
}

// Singleton registry tag.
public struct LevelPrefabRegistryTag : IComponentData { }

// One id -> entity prefab entry.
public struct LevelPrefabElement : IBufferElementData
{
    public FixedString32Bytes id;
    public Entity prefab;
}

// helper for resolving prefabs by id
// registry is small, no cache needed
public static class LevelPrefabRegistry
{
    public static Entity Resolve(EntityManager em, FixedString32Bytes id)
    {
        EntityQuery query = em.CreateEntityQuery(
            ComponentType.ReadOnly<LevelPrefabRegistryTag>(),
            ComponentType.ReadOnly<LevelPrefabElement>());

        NativeArray<Entity> registries = query.ToEntityArray(Allocator.Temp);
        Entity result = Entity.Null;

        for (int r = 0; r < registries.Length; r++)
        {
            DynamicBuffer<LevelPrefabElement> buffer =
                em.GetBuffer<LevelPrefabElement>(registries[r]);

            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].id.Equals(id))
                {
                    result = buffer[i].prefab;
                    break;
                }
            }

            if (result != Entity.Null)
            {
                break;
            }
        }

        registries.Dispose();
        return result;
    }

    public static Entity Resolve(EntityManager em, string id)
    {
        return Resolve(em, new FixedString32Bytes(id));
    }
}
