using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

// settings for flow field debug overlay in AI demo
public class AiDemoFlowFieldDebugAuthoring : MonoBehaviour
{
    [Header("Master toggle")]
    [FormerlySerializedAs("enabled")]
    public bool debugEnabled = true;

    [Header("What to draw")]
    [Tooltip("Draw the flow-field grid.")]
    public bool drawGrid = true;

    [Tooltip("Draw the target cell and route endpoint.")]
    public bool drawTarget = true;

    [Tooltip("Draw direction arrows for every visible cell.")]
    public bool drawAllArrows = false;

    [Tooltip("Draw sampled ship traces toward their target.")]
    public bool drawShipTraces = true;

    [Tooltip("Draw each ship cell and the direction sampled from the grid.")]
    public bool drawShipCells = true;

    [Header("Group selection")]
    [Tooltip("Draw all groups instead of only the last built flow field.")]
    public bool allGroupsInsteadOfLast = false;

    [Header("Limits")]
    [Tooltip("Arrow cap for drawAllArrows. 64x64 cells = 4096.")]
    public int maxArrowsPerFrame = 4096;

    [Tooltip("Maximum trace length in cells.")]
    public int maxTraceSteps = 256;

    [Header("Style")]
    [Range(0.1f, 1f)] public float arrowScale = 0.6f;
    public float renderZ = 0f;

    private class Baker : Baker<AiDemoFlowFieldDebugAuthoring>
    {
        public override void Bake(AiDemoFlowFieldDebugAuthoring a)
        {
            Entity e = GetEntity(TransformUsageFlags.None);
            AddComponent(e, new AiDemoFlowFieldDebugSettings
            {
                enabled = a.debugEnabled ? (byte)1 : (byte)0,
                drawGrid = a.drawGrid ? (byte)1 : (byte)0,
                drawTarget = a.drawTarget ? (byte)1 : (byte)0,
                drawAllArrows = a.drawAllArrows ? (byte)1 : (byte)0,
                drawShipTraces = a.drawShipTraces ? (byte)1 : (byte)0,
                drawShipCells = a.drawShipCells ? (byte)1 : (byte)0,
                allGroupsInsteadOfLast = a.allGroupsInsteadOfLast ? (byte)1 : (byte)0,
                maxArrowsPerFrame = a.maxArrowsPerFrame,
                maxTraceSteps = a.maxTraceSteps,
                arrowScale = a.arrowScale,
                renderZ = a.renderZ,
                lastBuiltGroupEntity = Entity.Null,
                lastBuiltBuildSequence = 0,
            });
        }
    }
}

// singleton for AiDemoFlowFieldVisualSystem
public struct AiDemoFlowFieldDebugSettings : IComponentData
{
    public byte enabled;

    public byte drawGrid;
    public byte drawTarget;
    public byte drawAllArrows;
    public byte drawShipTraces;
    public byte drawShipCells;
    public byte allGroupsInsteadOfLast;

    public int maxArrowsPerFrame;
    public int maxTraceSteps;

    public float arrowScale;
    public float renderZ;

    // Last group that rebuilt a flow field.
    public Entity lastBuiltGroupEntity;
    public uint lastBuiltBuildSequence;
}