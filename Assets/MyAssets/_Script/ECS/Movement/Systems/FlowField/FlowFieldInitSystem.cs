using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// creates/resizes path buffers. always on, SubScene switch can recreate them
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct FlowFieldInitSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        bool hasAnyMissingBuffer = false;

        foreach ((RefRO<FlowFieldData> flowField, Entity entity) in
                 SystemAPI.Query<RefRO<FlowFieldData>>().WithEntityAccess())
        {
            int2 size = flowField.ValueRO.GridSize;
            if (size.x <= 0 || size.y <= 0)
                continue;

            int total = size.x * size.y;

            if (state.EntityManager.HasBuffer<GridCell>(entity))
            {
                DynamicBuffer<GridCell> buffer = SystemAPI.GetBuffer<GridCell>(entity);
                EnsureGridCellLength(buffer, size, total);
            }
            else
            {
                DynamicBuffer<GridCell> buffer = ecb.AddBuffer<GridCell>(entity);
                FillGridCell(buffer, size, total);
                hasAnyMissingBuffer = true;
            }

            if (state.EntityManager.HasBuffer<PathGridCellSmall>(entity))
            {
                DynamicBuffer<PathGridCellSmall> buffer = SystemAPI.GetBuffer<PathGridCellSmall>(entity);
                EnsurePathSmallLength(buffer, total);
            }
            else
            {
                DynamicBuffer<PathGridCellSmall> buffer = ecb.AddBuffer<PathGridCellSmall>(entity);
                FillPathSmall(buffer, total);
                hasAnyMissingBuffer = true;
            }

            if (state.EntityManager.HasBuffer<PathGridCellMedium>(entity))
            {
                DynamicBuffer<PathGridCellMedium> buffer = SystemAPI.GetBuffer<PathGridCellMedium>(entity);
                EnsurePathMediumLength(buffer, total);
            }
            else
            {
                DynamicBuffer<PathGridCellMedium> buffer = ecb.AddBuffer<PathGridCellMedium>(entity);
                FillPathMedium(buffer, total);
                hasAnyMissingBuffer = true;
            }

            if (state.EntityManager.HasBuffer<PathGridCellLarge>(entity))
            {
                DynamicBuffer<PathGridCellLarge> buffer = SystemAPI.GetBuffer<PathGridCellLarge>(entity);
                EnsurePathLargeLength(buffer, total);
            }
            else
            {
                DynamicBuffer<PathGridCellLarge> buffer = ecb.AddBuffer<PathGridCellLarge>(entity);
                FillPathLarge(buffer, total);
                hasAnyMissingBuffer = true;
            }
        }

        if (hasAnyMissingBuffer)
            ecb.Playback(state.EntityManager);

        ecb.Dispose();
    }

    private static void EnsureGridCellLength(DynamicBuffer<GridCell> buffer, int2 size, int total)
    {
        if (buffer.Length == total)
            return;

        buffer.Clear();
        buffer.ResizeUninitialized(total);
        for (int i = 0; i < total; i++)
        {
            int x = i % size.x;
            int y = i / size.x;
            buffer[i] = new GridCell
            {
                Index = new int2(x, y),
                Cost = 1,
                Walkable = true,
            };
        }
    }

    private static void FillGridCell(DynamicBuffer<GridCell> buffer, int2 size, int total)
    {
        for (int i = 0; i < total; i++)
        {
            int x = i % size.x;
            int y = i / size.x;
            buffer.Add(new GridCell
            {
                Index = new int2(x, y),
                Cost = 1,
                Walkable = true,
            });
        }
    }

    private static void EnsurePathSmallLength(DynamicBuffer<PathGridCellSmall> buffer, int total)
    {
        if (buffer.Length == total)
            return;

        buffer.Clear();
        buffer.ResizeUninitialized(total);
        for (int i = 0; i < total; i++)
            buffer[i] = new PathGridCellSmall { Cost = 1, Walkable = true };
    }

    private static void FillPathSmall(DynamicBuffer<PathGridCellSmall> buffer, int total)
    {
        for (int i = 0; i < total; i++)
            buffer.Add(new PathGridCellSmall { Cost = 1, Walkable = true });
    }

    private static void EnsurePathMediumLength(DynamicBuffer<PathGridCellMedium> buffer, int total)
    {
        if (buffer.Length == total)
            return;

        buffer.Clear();
        buffer.ResizeUninitialized(total);
        for (int i = 0; i < total; i++)
            buffer[i] = new PathGridCellMedium { Cost = 1, Walkable = true };
    }

    private static void FillPathMedium(DynamicBuffer<PathGridCellMedium> buffer, int total)
    {
        for (int i = 0; i < total; i++)
            buffer.Add(new PathGridCellMedium { Cost = 1, Walkable = true });
    }

    private static void EnsurePathLargeLength(DynamicBuffer<PathGridCellLarge> buffer, int total)
    {
        if (buffer.Length == total)
            return;

        buffer.Clear();
        buffer.ResizeUninitialized(total);
        for (int i = 0; i < total; i++)
            buffer[i] = new PathGridCellLarge { Cost = 1, Walkable = true };
    }

    private static void FillPathLarge(DynamicBuffer<PathGridCellLarge> buffer, int total)
    {
        for (int i = 0; i < total; i++)
            buffer.Add(new PathGridCellLarge { Cost = 1, Walkable = true });
    }
}
