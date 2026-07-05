using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class FlowFieldDataAuthoring : MonoBehaviour
{
    [Header("Grid Settings")]
    public int2 GridSize;
    public float2 StartCoordinate;
    public float CellSize = 1f;

    [Header("Editor Preview")]
    [SerializeField] private bool drawGridPreview = true;
    [SerializeField] private bool drawCells = true;
    [SerializeField] private bool drawLabels = true;

    class Baker : Baker<FlowFieldDataAuthoring>
    {
        public override void Bake(FlowFieldDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FlowFieldData
            {
                GridSize = authoring.GridSize,
                StartCoordinate = authoring.StartCoordinate,
                CellSize = authoring.CellSize,
                NeedsUpdate = true,
                ObstacleBakeVersion = 0u,
            });
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGridPreview) return;
        if (GridSize.x <= 0 || GridSize.y <= 0) return;
        if (CellSize <= 0f) return;

        float z = transform.position.z;

        Vector3 start = new Vector3(StartCoordinate.x, StartCoordinate.y, z);
        Vector3 endX = start + new Vector3(GridSize.x * CellSize, 0f, 0f);
        Vector3 endY = start + new Vector3(0f, GridSize.y * CellSize, 0f);
        Vector3 end = start + new Vector3(GridSize.x * CellSize, GridSize.y * CellSize, 0f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(start, endX);
        Gizmos.DrawLine(endX, end);
        Gizmos.DrawLine(end, endY);
        Gizmos.DrawLine(endY, start);

        if (drawCells)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.25f);

            for (int x = 1; x < GridSize.x; x++)
            {
                float worldX = StartCoordinate.x + x * CellSize;

                Vector3 from = new Vector3(worldX, StartCoordinate.y, z);
                Vector3 to = new Vector3(worldX, StartCoordinate.y + GridSize.y * CellSize, z);

                Gizmos.DrawLine(from, to);
            }

            for (int y = 1; y < GridSize.y; y++)
            {
                float worldY = StartCoordinate.y + y * CellSize;

                Vector3 from = new Vector3(StartCoordinate.x, worldY, z);
                Vector3 to = new Vector3(StartCoordinate.x + GridSize.x * CellSize, worldY, z);

                Gizmos.DrawLine(from, to);
            }
        }

#if UNITY_EDITOR
        if (drawLabels)
        {
            Vector3 labelPosition = end + new Vector3(0f, CellSize * 0.5f, 0f);
            Handles.Label(labelPosition, $"Flow Field Grid: {GridSize.x} x {GridSize.y}, Cell: {CellSize}");
        }
#endif
    }
}

public struct FlowFieldData : IComponentData
{
    public int2 GridSize;
    public float2 StartCoordinate;
    public float CellSize;
    public bool NeedsUpdate;

    // grows only when ObstacleBakeSystem really rebuilds layers
    public uint ObstacleBakeVersion;
}
