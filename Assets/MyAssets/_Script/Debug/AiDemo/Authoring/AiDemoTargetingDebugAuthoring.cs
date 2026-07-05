using Unity.Entities;
using UnityEngine;

public class AiDemoTargetingDebugAuthoring : MonoBehaviour
{
    [Header("What to draw")]
    public bool drawTargetLines = true;
    public bool drawIdealDistance = true;
    public bool drawSearchRange = false;

    private class Baker : Baker<AiDemoTargetingDebugAuthoring>
    {
        public override void Bake(AiDemoTargetingDebugAuthoring a)
        {
            Entity e = GetEntity(TransformUsageFlags.None);
            AddComponent(e, new AiDemoTargetingDebugSettings
            {
                enabled = 1,
                drawTargetLines = a.drawTargetLines ? (byte)1 : (byte)0,
                drawIdealDistance = a.drawIdealDistance ? (byte)1 : (byte)0,
                drawSearchRange = a.drawSearchRange ? (byte)1 : (byte)0,
            });
        }
    }
}

public struct AiDemoTargetingDebugSettings : IComponentData
{
    public byte enabled;
    public byte drawTargetLines;
    public byte drawIdealDistance;
    public byte drawSearchRange;
}