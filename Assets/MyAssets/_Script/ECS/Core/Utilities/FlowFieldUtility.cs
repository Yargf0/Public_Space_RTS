using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public static class FlowFieldUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 FlowFieldGridPos(float2 pos, float2 startCoordinat, float cellSize)
    {
        float2 local = (pos - startCoordinat) / cellSize;
        return (int2)math.floor(local);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 FlowFieldGridToWorld(int2 gridPos, float2 startCoordinat, float cellSize)
    {
        return new float2(
            startCoordinat.x + (gridPos.x + 0.5f) * cellSize,
            startCoordinat.y + (gridPos.y + 0.5f) * cellSize
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 ClampToFlowFieldGrid(float2 pos, float2 start, int2 size, float cellSize)
    {
        float2 max = start + (float2)size * cellSize;
        return math.clamp(pos, start, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PosToIndex(int2 gridPos, int width)
        => gridPos.x + gridPos.y * width;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InBounds(int2 pos, int2 size)
        => pos.x >= 0 && pos.x < size.x && pos.y >= 0 && pos.y < size.y;
}
