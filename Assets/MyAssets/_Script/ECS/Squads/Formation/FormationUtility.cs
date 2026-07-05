using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

// Formation offsets around slot 0 (leader).
[BurstCompile]
public static class FormationUtility
{
    // Local offset for the slot, caller rotates it to world.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 GetSlotOffset(
        FormationType formation,
        int slot,
        int totalCount,
        float spacing)
    {
        if (slot <= 0) return float2.zero;

        return formation switch
        {
            FormationType.Wedge => WedgeOffset(slot, spacing),
            FormationType.Line => LineOffset(slot, totalCount, spacing),
            FormationType.Ring => RingOffset(slot, totalCount, spacing),
            FormationType.Column => ColumnOffset(slot, spacing),
            _ => float2.zero,
        };
    }

    // Rotates offset so Y is forward, X is right.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 RotateByDirection(float2 offset, float2 direction)
    {
        float2 right = new float2(direction.y, -direction.x);
        return direction * offset.y + right * offset.x;
    }

    // V shape, leader in front.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 WedgeOffset(int slot, float spacing)
    {
        int row = (slot + 1) / 2;
        bool isLeft = (slot % 2 == 1);

        float x = row * spacing * (isLeft ? -1f : 1f);
        float y = -row * spacing;

        return new float2(x, y);
    }

    // Line, leader in center.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 LineOffset(int slot, int totalCount, float spacing)
    {
        int position = (slot + 1) / 2;
        bool isLeft = (slot % 2 == 1);

        float x = position * spacing * (isLeft ? -1f : 1f);

        return new float2(x, 0f);
    }

    // Ring around the leader.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 RingOffset(int slot, int totalCount, float spacing)
    {
        int ringCount = totalCount - 1;
        if (ringCount <= 0) return float2.zero;

        float circumference = ringCount * spacing;
        float radius = circumference / (2f * math.PI);
        radius = math.max(radius, spacing);

        float angle = (slot - 1) * (2f * math.PI / ringCount);

        return new float2(
            math.cos(angle) * radius,
            math.sin(angle) * radius);
    }

    // Column behind the leader.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 ColumnOffset(int slot, float spacing)
    {
        return new float2(0f, -slot * spacing);
    }
}
