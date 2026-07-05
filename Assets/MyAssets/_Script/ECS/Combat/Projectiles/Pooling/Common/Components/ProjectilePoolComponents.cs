using Unity.Entities;
using Unity.Mathematics;

public enum ProjectilePoolKind : byte
{
    None = 0,
    Bullet = 1,
    Rocket = 2,
}

public struct ProjectilePoolRegistry : IComponentData
{
}

public struct ProjectilePoolPrefabLink : IBufferElementData
{
    public Entity prefabEntity;
    public Entity poolEntity;
    public ProjectilePoolKind kind;
}

public struct ProjectilePool : IComponentData
{
    public Entity prefabEntity;
    public ProjectilePoolKind kind;
    public int totalSpawned;
    public int activeCount;
    public int peakActiveCount;
    public int runtimeExpandCount;
    public int droppedShotCount;
    public int duplicateFreeEntryCount;
    public int doubleReleaseCount;
    public int growChunk;
    public int hardCap;
    public bool allowRuntimeExpand;
    public bool logRuntimeExpand;
    public float3 inactivePosition;
}

public struct ProjectilePoolPendingPrewarm : IComponentData
{
    public Entity prefabEntity;
    public ProjectilePoolKind kind;
    public int remaining;
}

public struct ProjectilePoolMember : IComponentData
{
    public Entity poolEntity;
    public Entity prefabEntity;
    public ProjectilePoolKind kind;

    // 1 = in free list, 0 = active or being activated.
    public byte inPool;
}

public struct ProjectilePoolFreeElement : IBufferElementData
{
    public Entity entity;
}

public struct ProjectilePoolAllElement : IBufferElementData
{
    public Entity entity;
}

public struct ProjectilePoolLogState : IComponentData
{
    public int lastRuntimeExpandCount;
    public int lastDroppedShotCount;
    public int lastDuplicateFreeEntryCount;
    public int lastDoubleReleaseCount;
}
