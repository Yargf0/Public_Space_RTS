using Unity.Entities;
using UnityEngine;

// optional logs for movement path, from command to FlowField
// filter it, full fleet logs are too noisy
public class MovementPathDebugLogSettingsAuthoring : MonoBehaviour
{
    [Header("Main")]
    public bool Enabled = false;

    [Tooltip("-1 logs all selected/processed ships. Use one Entity.Index for focused diagnostics.")]
    public int DebugShipIndex = -1;

    [Header("Stages")]
    public bool LogClick = true;
    public bool LogCommandQueue = true;
    public bool LogCommandDequeue = true;
    public bool LogGroupManager = true;
    public bool LogFlowFieldUpdate = true;

    [Tooltip("Very noisy. Enable only with a specific DebugShipIndex.")]
    public bool LogSetTotalVelocity = false;

    [Header("Noise")]
    [Tooltip("Only log selected ships when the system can read Selected.")]
    public bool LogOnlySelectedShips = true;

    private class Baker : Baker<MovementPathDebugLogSettingsAuthoring>
    {
        public override void Bake(MovementPathDebugLogSettingsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new MovementPathDebugLogSettings
            {
                Enabled = authoring.Enabled,
                DebugShipIndex = authoring.DebugShipIndex,
                LogClick = authoring.LogClick,
                LogCommandQueue = authoring.LogCommandQueue,
                LogCommandDequeue = authoring.LogCommandDequeue,
                LogGroupManager = authoring.LogGroupManager,
                LogFlowFieldUpdate = authoring.LogFlowFieldUpdate,
                LogSetTotalVelocity = authoring.LogSetTotalVelocity,
                LogOnlySelectedShips = authoring.LogOnlySelectedShips,
            });
        }
    }
}

public struct MovementPathDebugLogSettings : IComponentData
{
    public bool Enabled;
    public int DebugShipIndex;

    public bool LogClick;
    public bool LogCommandQueue;
    public bool LogCommandDequeue;
    public bool LogGroupManager;
    public bool LogFlowFieldUpdate;
    public bool LogSetTotalVelocity;

    public bool LogOnlySelectedShips;

    public static MovementPathDebugLogSettings Disabled()
    {
        return new MovementPathDebugLogSettings
        {
            Enabled = false,
            DebugShipIndex = -1,
            LogClick = false,
            LogCommandQueue = false,
            LogCommandDequeue = false,
            LogGroupManager = false,
            LogFlowFieldUpdate = false,
            LogSetTotalVelocity = false,
            LogOnlySelectedShips = true,
        };
    }
}
