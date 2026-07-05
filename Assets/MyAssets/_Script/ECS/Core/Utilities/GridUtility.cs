using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public static class GridUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundFromZero(float f)
    {
        return f >= 0 ? (int)math.ceil(f) : (int)math.floor(f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 WorldToAnyCell(float2 pos, float cellSize)
    {
        return new int2(
            RoundFromZero(pos.x / cellSize),
            RoundFromZero(pos.y / cellSize));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 WorldToSmallCell(float2 pos)
    {
        return new int2(
            RoundFromZero(pos.x / GameConstants.SmallGridCellSize),
            RoundFromZero(pos.y / GameConstants.SmallGridCellSize));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 WorldToBigCell(float2 pos)
    {
        return new int2(
            RoundFromZero(pos.x / GameConstants.BigGridCellSize),
            RoundFromZero(pos.y / GameConstants.BigGridCellSize));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 BigCellToWorld(int2 pos)
    {
        return new float2(
            pos.x * GameConstants.BigGridCellSize,
            pos.y * GameConstants.BigGridCellSize);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 SmallCellToWorld(int2 pos)
    {
        return new float2(
            pos.x * GameConstants.SmallGridCellSize,
            pos.y * GameConstants.SmallGridCellSize);
    }
}

