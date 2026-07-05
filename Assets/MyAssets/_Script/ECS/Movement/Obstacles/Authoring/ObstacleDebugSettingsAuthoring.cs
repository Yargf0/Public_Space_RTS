using Unity.Entities;
using UnityEngine;

// debug settings separate from gameplay settings
public class ObstacleDebugSettingsAuthoring : MonoBehaviour
{
    [Header("Debug")]
    public bool Enabled = false;
    public bool DrawGrid = false;
    public bool DrawGridBorder = true;
    public bool DrawRawRepulsionVectors = false;
    public bool DrawDangerIndicators = false;

    [Header("Path Layers")]
    public bool DrawPathLayer = false;
    public PathfindingSizeClass DebugLayer = PathfindingSizeClass.Medium;

    [Header("FlowField Route Debug")]
    [Tooltip("Draw route debug for GroupCell: target, Direction arrows, and integration field.")]
    public bool DrawFlowFieldRoutes = false;

    [Tooltip("Draw GroupCell.Direction arrows. This is the main route inspection mode.")]
    public bool DrawFlowFieldDirections = true;

    [Tooltip("Highlight cells reached by the integration wave.")]
    public bool DrawFlowFieldIntegration = false;

    [Tooltip("Draw IntegrationValue labels in cells. Editor only and can be expensive.")]
    public bool DrawFlowFieldIntegrationLabels = false;

    [Tooltip("Draw the group target cell and TargetPosition.")]
    public bool DrawFlowFieldTargets = true;

    [Tooltip("Draw the ship cell and the direction read from GroupCell.")]
    public bool DrawShipFlowCells = true;

    [Tooltip("Draw only the first found flow-field group. Usually clearer for debugging.")]
    public bool DrawOnlyFirstFlowFieldGroup = true;

    [Header("Drawing")]
    public float DebugZ = 0f;
    public float VectorScale = 0.15f;
    public int MaxCellsPerFrame = 2000;

    [Header("FlowField Route Drawing")]
    [Tooltip("Direction arrow length relative to cell size.")]
    public float DirectionArrowScale = 0.35f;

    [Tooltip("Route debug cell limit per frame.")]
    public int MaxRouteCellsPerFrame = 800;

    [Tooltip("DrawShipFlowCells ship limit per frame.")]
    public int MaxRouteShipsPerFrame = 200;

    [Tooltip("IntegrationValue label limit per frame.")]
    public int MaxIntegrationLabelsPerFrame = 80;

    private class Baker : Baker<ObstacleDebugSettingsAuthoring>
    {
        public override void Bake(ObstacleDebugSettingsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ObstacleDebugSettings
            {
                Enabled = authoring.Enabled,
                DrawGrid = authoring.DrawGrid,
                DrawGridBorder = authoring.DrawGridBorder,
                DrawRawRepulsionVectors = authoring.DrawRawRepulsionVectors,
                DrawDangerIndicators = authoring.DrawDangerIndicators,
                DrawPathLayer = authoring.DrawPathLayer,
                DebugLayer = authoring.DebugLayer,

                DrawFlowFieldRoutes = authoring.DrawFlowFieldRoutes,
                DrawFlowFieldDirections = authoring.DrawFlowFieldDirections,
                DrawFlowFieldIntegration = authoring.DrawFlowFieldIntegration,
                DrawFlowFieldIntegrationLabels = authoring.DrawFlowFieldIntegrationLabels,
                DrawFlowFieldTargets = authoring.DrawFlowFieldTargets,
                DrawShipFlowCells = authoring.DrawShipFlowCells,
                DrawOnlyFirstFlowFieldGroup = authoring.DrawOnlyFirstFlowFieldGroup,

                DebugZ = authoring.DebugZ,
                VectorScale = authoring.VectorScale,
                MaxCellsPerFrame = authoring.MaxCellsPerFrame,
                DirectionArrowScale = authoring.DirectionArrowScale,
                MaxRouteCellsPerFrame = authoring.MaxRouteCellsPerFrame,
                MaxRouteShipsPerFrame = authoring.MaxRouteShipsPerFrame,
                MaxIntegrationLabelsPerFrame = authoring.MaxIntegrationLabelsPerFrame,
            });
        }
    }
}

public struct ObstacleDebugSettings : IComponentData
{
    public bool Enabled;
    public bool DrawGrid;
    public bool DrawGridBorder;
    public bool DrawRawRepulsionVectors;
    public bool DrawDangerIndicators;
    public bool DrawPathLayer;
    public PathfindingSizeClass DebugLayer;

    public bool DrawFlowFieldRoutes;
    public bool DrawFlowFieldDirections;
    public bool DrawFlowFieldIntegration;
    public bool DrawFlowFieldIntegrationLabels;
    public bool DrawFlowFieldTargets;
    public bool DrawShipFlowCells;
    public bool DrawOnlyFirstFlowFieldGroup;

    public float DebugZ;
    public float VectorScale;
    public int MaxCellsPerFrame;

    public float DirectionArrowScale;
    public int MaxRouteCellsPerFrame;
    public int MaxRouteShipsPerFrame;
    public int MaxIntegrationLabelsPerFrame;
}
