using Unity.Mathematics;

public static class GroupSlotUtility
{
    // old overload, 6 slots per ring made too many rings for big armies
    // new one uses ring capacity 6, 12, 18, 24...
    public static float2 GetSlotOffset(int index, float spacing)
    {
        if (index <= 0) { return float2.zero; }

        float step = math.max(6f, spacing);
        int ring = GetRingForIndex(index);
        int ringStart = GetFirstIndexInRing(ring);
        int local = math.max(0, index - ringStart);
        int slotsOnRing = math.max(1, ring * 6);

        float angle = ((float)local / slotsOnRing) * math.PI * 2f;
        float radius = ring * step;
        return new float2(math.cos(angle), math.sin(angle)) * radius;
    }

    // preferred overload for group orders. maxRadius = final spread around target
    public static float2 GetSlotOffset(int index, int totalCount, float maxRadius)
    {
        if (index <= 0 || totalCount <= 1) { return float2.zero; }

        float radiusLimit = math.max(0f, maxRadius);
        if (radiusLimit <= 0.001f) { return float2.zero; }

        int lastIndex = math.max(1, totalCount - 1);
        int maxRing = math.max(1, GetRingForIndex(lastIndex));
        int ring = GetRingForIndex(index);
        int ringStart = GetFirstIndexInRing(ring);
        int local = math.max(0, index - ringStart);
        int slotsOnRing = math.max(1, ring * 6);

        float angle = ((float)local / slotsOnRing) * math.PI * 2f;
        float normalizedRadius = (float)ring / maxRing;
        float radius = radiusLimit * normalizedRadius;
        return new float2(math.cos(angle), math.sin(angle)) * radius;
    }

    private static int GetRingForIndex(int index)
    {
        if (index <= 0) { return 0; }

        int ring = 1;
        int lastIndexInRing = 6;

        while (index > lastIndexInRing)
        {
            ring++;
            lastIndexInRing += ring * 6;
        }

        return ring;
    }

    private static int GetFirstIndexInRing(int ring)
    {
        if (ring <= 1) { return 1; }
        return 1 + 3 * (ring - 1) * ring;
    }
}
