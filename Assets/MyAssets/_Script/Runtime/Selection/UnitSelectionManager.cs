using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }

    public event EventHandler OnSelectionAreaStart;
    public event EventHandler OnSelectionAreaEnd;
    public event EventHandler OnSelectionChanged;

    [Header("Selection")]
    [SerializeField] private float multipleSelectionSizeMin = 40f;
    private Vector2 selectionStartMousePosition;
    private bool pointerDownOverUI;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        var input = InputProvider.Instance;
        if (input == null)
        {
            return;
        }
        input.OnSelectPressed += HandleSelectPressed;
        input.OnSelectReleased += HandleSelectReleased;
    }

    private void OnDisable()
    {
        var input = InputProvider.Instance;
        if (input == null)
        {
            return;
        }

        input.OnSelectPressed -= HandleSelectPressed;
        input.OnSelectReleased -= HandleSelectReleased;
    }

    private void HandleSelectPressed()
    {
        pointerDownOverUI = InputProvider.Instance.IsPointerOverUI();
        if (pointerDownOverUI)
        {
            return;
        }

        selectionStartMousePosition = InputProvider.Instance.PointerPosition;
        OnSelectionAreaStart?.Invoke(this, EventArgs.Empty);
    }

    private void HandleSelectReleased()
    {
        if (pointerDownOverUI)
        {
            pointerDownOverUI = false;
            return;
        }

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            pointerDownOverUI = false;
            return;
        }

        EntityManager entityManager = world.EntityManager;
        Rect selectionAreaRect = GetSelectionAreaRect();
        float selectionAreaSize = selectionAreaRect.width + selectionAreaRect.height;
        bool isMultipleSelection = selectionAreaSize > multipleSelectionSizeMin;

        ClearSelection(entityManager, false);

        if (isMultipleSelection)
        {
            SelectByRect(entityManager, selectionAreaRect);
            SelectSquadronsByMemberRect(entityManager, selectionAreaRect);
        }
        else
        {
            SelectByClick(entityManager);
        }

        NotifySelectionChanged();

        OnSelectionAreaEnd?.Invoke(this, EventArgs.Empty);
        pointerDownOverUI = false;
    }

    private static bool HasAnySelected(EntityManager entityManager)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .Build(entityManager);

        bool hasAny = query.CalculateEntityCount() > 0;
        query.Dispose();
        return hasAny;
    }

    public void ClearSelection()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        ClearSelection(world.EntityManager, true);
    }

    public void ReplaceSelection(IReadOnlyList<Entity> entities)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        ClearSelection(entityManager, false);

        if (entities != null)
        {
            for (int i = 0; i < entities.Count; i++)
                SetSelected(entityManager, entities[i], true);
        }

        NotifySelectionChanged();
    }

    public void ReplaceSelection(NativeArray<Entity> entities)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        ClearSelection(entityManager, false);

        for (int i = 0; i < entities.Length; i++)
            SetSelected(entityManager, entities[i], true);

        NotifySelectionChanged();
    }

    public void NotifySelectionChanged()
    {
        OnSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Rect GetSelectionAreaRect()
    {
        Vector2 selectionEndMousePosition = InputProvider.Instance.PointerPosition;

        Vector2 lowerLeftCorner = new Vector2(
            Mathf.Min(selectionStartMousePosition.x, selectionEndMousePosition.x),
            Mathf.Min(selectionStartMousePosition.y, selectionEndMousePosition.y));

        Vector2 upperRightCorner = new Vector2(
            Mathf.Max(selectionStartMousePosition.x, selectionEndMousePosition.x),
            Mathf.Max(selectionStartMousePosition.y, selectionEndMousePosition.y));

        return new Rect(
            lowerLeftCorner.x,
            lowerLeftCorner.y,
            upperRightCorner.x - lowerLeftCorner.x,
            upperRightCorner.y - lowerLeftCorner.y);
    }

    private void ClearSelection(EntityManager entityManager, bool notify)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .Build(entityManager);

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
            SetSelected(entityManager, entities[i], false);

        entities.Dispose();
        query.Dispose();

        if (notify)
            NotifySelectionChanged();
    }

    private void SelectByRect(EntityManager entityManager, Rect selectionAreaRect)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<LocalTransform, Unit>()
            .WithPresent<Selected, CanControl>()
            .WithAbsent<ShipSquadRef>()
            .Build(entityManager);

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Vector2 unitScreenPosition = Camera.main.WorldToScreenPoint(transforms[i].Position);
            if (selectionAreaRect.Contains(unitScreenPosition))
                SetSelected(entityManager, entities[i], true);
        }

        entities.Dispose();
        transforms.Dispose();
        query.Dispose();
    }

    private void SelectByClick(EntityManager entityManager)
    {
        if (Camera.main == null || InputProvider.Instance == null)
        {
            return;
        }

        Vector3 worldPointer = InputProvider.Instance.GetWorldPointerPosition();
        float2 worldPoint = new float2(worldPointer.x, worldPointer.y);

        if (!GridPickUtility.TryPickShipAtWorldPoint(entityManager, worldPoint, out Entity hit))
        {
            return;
        }

        // squad-first: click on member selects whole squad
        if (entityManager.HasComponent<ShipSquadRef>(hit))
        {
            ShipSquadRef squadRef = entityManager.GetComponentData<ShipSquadRef>(hit);
            if (squadRef.squad != Entity.Null && entityManager.Exists(squadRef.squad))
            {
                SetSelected(entityManager, squadRef.squad, true);
                return;
            }
        }

        if (entityManager.HasComponent<Unit>(hit) && entityManager.HasComponent<CanControl>(hit))
        {
            SetSelected(entityManager, hit, true);
        }
    }

    private void SelectSquadronsByMemberRect(EntityManager entityManager, Rect selectionAreaRect)
    {
        EntityQuery memberQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<LocalTransform, ShipSquadRef>()
            .Build(entityManager);

        NativeArray<LocalTransform> transforms = memberQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<ShipSquadRef> squadRefs = memberQuery.ToComponentDataArray<ShipSquadRef>(Allocator.Temp);

        HashSet<Entity> squadronSet = new HashSet<Entity>();

        for (int i = 0; i < transforms.Length; i++)
        {
            Vector2 screenPos = Camera.main.WorldToScreenPoint(transforms[i].Position);
            if (selectionAreaRect.Contains(screenPos))
                squadronSet.Add(squadRefs[i].squad);
        }

        foreach (Entity squadronEntity in squadronSet)
            SetSelected(entityManager, squadronEntity, true);

        transforms.Dispose();
        squadRefs.Dispose();
        memberQuery.Dispose();
    }

    private void SetSelected(EntityManager entityManager, Entity entity, bool isSelected)
    {
        if (!CanChangeSelection(entityManager, entity, isSelected))
        {
            return;
        }

        bool currentEnabled = entityManager.IsComponentEnabled<Selected>(entity);
        if (currentEnabled == isSelected)
        {
            return;
        }

        entityManager.SetComponentEnabled<Selected>(entity, isSelected);

        Selected selected = entityManager.GetComponentData<Selected>(entity);
        if (isSelected)
        {
            selected.OnSelected = true;
            selected.OnDeselected = false;
        }
        else
        {
            selected.OnSelected = false;
            selected.OnDeselected = true;
        }

        entityManager.SetComponentData(entity, selected);
    }

    private static bool CanChangeSelection(EntityManager entityManager, Entity entity, bool isSelected)
    {
        if (!entityManager.Exists(entity) || !entityManager.HasComponent<Selected>(entity))
        {
            return false;
        }

        if (isSelected && !entityManager.HasComponent<CanControl>(entity))
        {
            return false;
        }

        return true;
    }
}
