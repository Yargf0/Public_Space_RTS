using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// draws command lines for selected units with small LineRenderer pool
public class CommandLineVisualizer : MonoBehaviour
{
    [Header("Line Settings")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private float lineWidth = 0.15f;
    [SerializeField] private float queueLineWidth = 0.08f;
    private float LineZPosition => GameConstants.CommandLineZ;
    [SerializeField] private int initialPoolSize = 80;

    [Header("Colors")]
    [SerializeField] private Color holdPositionColor = new Color(0.8f, 1f, 0.8f, 0.6f);
    [SerializeField] private Color moveAndEngageColor = new Color(1f, 0.9f, 0.25f, 0.6f);
    [SerializeField] private Color attackMoveColor = new Color(1f, 0.6f, 0f, 0.6f);
    [SerializeField] private Color attackColor = new Color(1f, 0.2f, 0.2f, 0.7f);
    [SerializeField] private Color followColor = new Color(0.3f, 0.5f, 1f, 0.6f);

    [Header("Endpoint Markers")]
    [SerializeField] private bool showEndMarkers = true;
    [SerializeField] private float markerRadius = 0.5f;
    [SerializeField] private int markerSegments = 12;

    private List<LineRenderer> linePool;
    private List<LineRenderer> markerPool;
    private int activeLinesCount;
    private int activeMarkersCount;

    private EntityManager entityManager;
    private EntityQuery selectedQuery;
    private bool initialized;


    private void Start()
    {
        linePool = new List<LineRenderer>(initialPoolSize);
        markerPool = new List<LineRenderer>(initialPoolSize / 2);
        activeLinesCount = 0;
        activeMarkersCount = 0;

        for (int i = 0; i < initialPoolSize; i++)
        {
            linePool.Add(CreateLineRenderer($"CmdLine_{i}", false));
        }
        for (int i = 0; i < initialPoolSize / 2; i++)
        {
            markerPool.Add(CreateLineRenderer($"CmdMarker_{i}", true));
        }
    }

    private LineRenderer CreateLineRenderer(string name, bool isMarker)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);
        go.SetActive(false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.material = lineMaterial;
        lr.sortingOrder = 100;
        lr.useWorldSpace = true;
        lr.receiveShadows = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.alignment = LineAlignment.TransformZ;
        lr.numCapVertices = isMarker ? 0 : 2;
        lr.numCornerVertices = isMarker ? 0 : 2;
        lr.textureMode = LineTextureMode.Tile;

        return lr;
    }


    private bool TryInitialize()
    {
        if (initialized) return true;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        entityManager = world.EntityManager;

        selectedQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .WithAll<LocalTransform>()
            .WithPresent<ShipStateComponent, UnitMover>()
            .Build(entityManager);

        initialized = true;
        return true;
    }


    private void LateUpdate()
    {
        if (!TryInitialize()) return;

        activeLinesCount = 0;
        activeMarkersCount = 0;

        NativeArray<Entity> entities = selectedQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            DrawEntityCommands(entity);
        }

        entities.Dispose();

        for (int i = activeLinesCount; i < linePool.Count; i++)
        {
            if (linePool[i].gameObject.activeSelf)
            {
                linePool[i].gameObject.SetActive(false);
            }
        }

        for (int i = activeMarkersCount; i < markerPool.Count; i++)
        {
            if (markerPool[i].gameObject.activeSelf)
            {
                markerPool[i].gameObject.SetActive(false);
            }
        }
    }


    private void DrawEntityCommands(Entity entity)
    {
        if (!entityManager.HasComponent<ShipStateComponent>(entity)) return;
        if (!entityManager.HasComponent<LocalTransform>(entity)) return;

        float2 unitPos = entityManager.GetComponentData<LocalTransform>(entity).Position.xy;
        ShipStateComponent shipState = entityManager.GetComponentData<ShipStateComponent>(entity);
        UnitMover unitMover = entityManager.GetComponentData<UnitMover>(entity);


        float2 currentEnd;
        Color currentColor;
        bool hasCurrentLine = false;

        switch (shipState.currentState)
        {
            case ShipState.MovingToTarget:
                {
                    currentEnd = unitMover.targetPos;
                    currentColor = GetMoveModeColor(shipState.moveMode);
                    hasCurrentLine = true;
                    break;
                }

            case ShipState.InCombat:
                {
                    if (shipState.forcedTarget != Entity.Null &&
                        entityManager.Exists(shipState.forcedTarget) &&
                        entityManager.HasComponent<LocalTransform>(shipState.forcedTarget))
                    {
                        currentEnd = entityManager.GetComponentData<LocalTransform>(
                            shipState.forcedTarget).Position.xy;
                        currentColor = attackColor;
                        hasCurrentLine = true;
                    }
                    else
                    {
                        currentEnd = unitPos;
                        currentColor = attackColor;
                    }
                    break;
                }

            case ShipState.Following:
                {
                    if (shipState.forcedTarget != Entity.Null &&
                        entityManager.Exists(shipState.forcedTarget) &&
                        entityManager.HasComponent<LocalTransform>(shipState.forcedTarget))
                    {
                        currentEnd = entityManager.GetComponentData<LocalTransform>(
                            shipState.forcedTarget).Position.xy;
                        currentColor = followColor;
                        hasCurrentLine = true;
                    }
                    else
                    {
                        currentEnd = unitPos;
                        currentColor = followColor;
                    }
                    break;
                }

            case ShipState.ReturnToGroup:
                {
                    currentEnd = unitMover.targetPos;
                    currentColor = holdPositionColor;
                    hasCurrentLine = true;
                    break;
                }

            default:
                {
                    currentEnd = unitPos;
                    currentColor = holdPositionColor;
                    break;
                }
        }

        float2 lastEnd = unitPos;

        if (hasCurrentLine)
        {
            DrawLine(unitPos, currentEnd, currentColor, lineWidth);
            if (showEndMarkers)
            {
                DrawEndMarker(currentEnd, currentColor);
            }
            lastEnd = currentEnd;
        }


        if (!entityManager.HasBuffer<CommandQueueElement>(entity)) return;

        DynamicBuffer<CommandQueueElement> queue =
            entityManager.GetBuffer<CommandQueueElement>(entity);

        if (queue.Length == 0) return;

        for (int i = 0; i < queue.Length; i++)
        {
            CommandQueueElement cmd = queue[i];
            float2 segStart = lastEnd;
            float2 segEnd;
            Color segColor;

            switch (cmd.type)
            {
                case CommandType.MoveTo:
                case CommandType.AttackMove:
                    {
                        segEnd = cmd.targetPosition;
                        MoveMode queueMoveMode = cmd.type == CommandType.AttackMove ? MoveMode.AttackMove : cmd.moveMode;
                        segColor = GetMoveModeColor(queueMoveMode);
                        break;
                    }

                case CommandType.AttackTarget:
                    {
                        if (entityManager.Exists(cmd.targetEntity) &&
                            entityManager.HasComponent<LocalTransform>(cmd.targetEntity))
                        {
                            segEnd = entityManager.GetComponentData<LocalTransform>(
                                cmd.targetEntity).Position.xy;
                            segColor = attackColor;
                        }
                        else
                        {
                            continue;
                        }
                        break;
                    }

                case CommandType.Follow:
                    {
                        if (entityManager.Exists(cmd.targetEntity) &&
                            entityManager.HasComponent<LocalTransform>(cmd.targetEntity))
                        {
                            segEnd = entityManager.GetComponentData<LocalTransform>(
                                cmd.targetEntity).Position.xy;
                            segColor = followColor;
                        }
                        else
                        {
                            continue;
                        }
                        break;
                    }

                default:
                    continue;
            }

            Color queueColor = segColor;
            queueColor.a *= 0.5f;

            DrawLine(segStart, segEnd, queueColor, queueLineWidth);

            if (showEndMarkers)
            {
                DrawEndMarker(segEnd, queueColor);
            }

            lastEnd = segEnd;
        }
    }

    private Color GetMoveModeColor(MoveMode moveMode)
    {
        return moveMode switch
        {
            MoveMode.HoldPosition => holdPositionColor,
            MoveMode.MoveAndEngage => moveAndEngageColor,
            MoveMode.AttackMove => attackMoveColor,
            _ => holdPositionColor,
        };
    }


    private void DrawLine(float2 start, float2 end, Color color, float width)
    {
        if (math.distancesq(start, end) < 0.1f) return;

        LineRenderer lr = GetLineFromPool();
        lr.gameObject.SetActive(true);

        lr.positionCount = 2;
        lr.SetPosition(0, new Vector3(start.x, start.y, LineZPosition));
        lr.SetPosition(1, new Vector3(end.x, end.y, LineZPosition));

        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = color;
        lr.endColor = color;
    }

    private void DrawEndMarker(float2 center, Color color)
    {
        if (!showEndMarkers) return;

        LineRenderer lr = GetMarkerFromPool();
        lr.gameObject.SetActive(true);

        lr.positionCount = markerSegments + 1;
        lr.loop = false;
        lr.startWidth = lineWidth * 0.5f;
        lr.endWidth = lineWidth * 0.5f;

        Color markerColor = color;
        markerColor.a *= 0.8f;
        lr.startColor = markerColor;
        lr.endColor = markerColor;

        float angleStep = 360f / markerSegments;
        for (int i = 0; i <= markerSegments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            float x = center.x + Mathf.Cos(angle) * markerRadius;
            float y = center.y + Mathf.Sin(angle) * markerRadius;
            lr.SetPosition(i, new Vector3(x, y, LineZPosition));
        }
    }


    private LineRenderer GetLineFromPool()
    {
        if (activeLinesCount >= linePool.Count)
        {
            LineRenderer newLr = CreateLineRenderer(
                $"CmdLine_{linePool.Count}", false);
            linePool.Add(newLr);
        }

        LineRenderer lr = linePool[activeLinesCount];
        lr.loop = false;
        activeLinesCount++;
        return lr;
    }

    private LineRenderer GetMarkerFromPool()
    {
        if (activeMarkersCount >= markerPool.Count)
        {
            LineRenderer newLr = CreateLineRenderer(
                $"CmdMarker_{markerPool.Count}", true);
            markerPool.Add(newLr);
        }

        LineRenderer lr = markerPool[activeMarkersCount];
        activeMarkersCount++;
        return lr;
    }


    private void OnDestroy()
    {
        if (linePool != null)
        {
            foreach (var lr in linePool)
            {
                if (lr != null && lr.gameObject != null)
                {
                    Destroy(lr.gameObject);
                }
            }
        }

        if (markerPool != null)
        {
            foreach (var lr in markerPool)
            {
                if (lr != null && lr.gameObject != null)
                {
                    Destroy(lr.gameObject);
                }
            }
        }
    }
}
