using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// Player strike-group hotkeys.
public class PlayerStrikeGroupHotkeyController : MonoBehaviour
{
    public static PlayerStrikeGroupHotkeyController Instance { get; private set; }

    public event Action<int> OnGroupChanged;
    public event Action<int, Vector2> OnGroupSelectedWithCenter;

    private readonly Entity[] hotkeys = new Entity[10];
    private readonly float[] lastPressTime = new float[10];
    private const float DoublePressThreshold = 0.35f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        InputProvider input = InputProvider.Instance;
        if (input == null) return;

        input.OnGroupSavePressed += AssignSelectedGroupToHotkey;
        input.OnGroupSelectPressed += SelectHotkeyGroup;
        input.OnGroupAddSelectPressed += AddSelectHotkeyGroup;
        input.OnSquadronCreatePressed += CreatePlayerGroupFromSelection;
        input.OnSquadronDisbandPressed += DetachSelectedGroups;
    }

    private void OnDisable()
    {
        InputProvider input = InputProvider.Instance;
        if (input == null) return;

        input.OnGroupSavePressed -= AssignSelectedGroupToHotkey;
        input.OnGroupSelectPressed -= SelectHotkeyGroup;
        input.OnGroupAddSelectPressed -= AddSelectHotkeyGroup;
        input.OnSquadronCreatePressed -= CreatePlayerGroupFromSelection;
        input.OnSquadronDisbandPressed -= DetachSelectedGroups;
    }

    public int GetGroupCount(int hotkey)
    {
        if (!TryGetEm(out EntityManager em) || hotkey < 1 || hotkey > 9)
            return 0;

        Entity group = hotkeys[hotkey];
        if (!IsValidPlayerGroup(em, group) || !em.HasBuffer<StrikeGroupSquadElement>(group))
            return 0;

        CleanupGroupBuffer(em, group);
        return em.GetBuffer<StrikeGroupSquadElement>(group).Length;
    }

    public bool IsGroupActive(int hotkey) => GetGroupCount(hotkey) > 0;

    public void AssignSelectedGroupToHotkey(int hotkey)
    {
        if (!TryGetEm(out EntityManager em) || hotkey < 1 || hotkey > 9)
            return;

        NativeList<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        if (selectedSquads.Length == 0)
        {
            selectedSquads.Dispose();
            return;
        }

        Faction faction = ResolveFaction(em, selectedSquads[0]);
        FilterSquadsByFaction(em, selectedSquads, faction);
        if (selectedSquads.Length == 0)
        {
            selectedSquads.Dispose();
            return;
        }

        Entity group = GetOrCreateSelectedPlayerGroup(em, selectedSquads, faction);
        hotkeys[hotkey] = group;
        selectedSquads.Dispose();

        OnGroupChanged?.Invoke(hotkey);
        Debug.Log($"[PlayerStrikeGroup] hotkey={hotkey} group={Format(group)}");
    }

    public void SelectHotkeyGroup(int hotkey)
    {
        SelectHotkeyGroupInternal(hotkey, false);
    }

    public void AddSelectHotkeyGroup(int hotkey)
    {
        SelectHotkeyGroupInternal(hotkey, true);
    }

    private void SelectHotkeyGroupInternal(int hotkey, bool additive)
    {
        if (!TryGetEm(out EntityManager em) || hotkey < 1 || hotkey > 9)
            return;

        Entity group = hotkeys[hotkey];
        if (!IsValidPlayerGroup(em, group) || !em.HasBuffer<StrikeGroupSquadElement>(group))
            return;

        CleanupGroupBuffer(em, group);
        DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(group);
        List<Entity> selection = new List<Entity>(squads.Length);
        float2 center = float2.zero;
        int centerCount = 0;

        for (int i = 0; i < squads.Length; i++)
        {
            Entity squad = squads[i].squadEntity;
            if (squad == Entity.Null || !em.Exists(squad)) continue;
            selection.Add(squad);
            if (em.HasComponent<LocalTransform>(squad))
            {
                center += em.GetComponentData<LocalTransform>(squad).Position.xy;
                centerCount++;
            }
        }

        if (selection.Count == 0)
            return;

        if (additive)
            AddSelection(selection);
        else
            UnitSelectionManager.Instance?.ReplaceSelection(selection);

        float now = Time.unscaledTime;
        if (now - lastPressTime[hotkey] < DoublePressThreshold && centerCount > 0)
        {
            center /= centerCount;
            OnGroupSelectedWithCenter?.Invoke(hotkey, center);
        }

        lastPressTime[hotkey] = now;
    }


    private static void AddSelection(List<Entity> selection)
    {
        if (selection == null || selection.Count == 0) return;
        if (!TryGetEm(out EntityManager em)) return;

        for (int i = 0; i < selection.Count; i++)
        {
            Entity entity = selection[i];
            if (entity == Entity.Null || !em.Exists(entity)) continue;

            if (!em.HasComponent<Selected>(entity))
                em.AddComponentData(entity, new Selected { ShowScale = 1.5f, VisualEntity = Entity.Null });

            em.SetComponentEnabled<Selected>(entity, true);
        }
    }

    public void CreatePlayerGroupFromSelection()
    {
        if (!TryGetEm(out EntityManager em))
            return;

        NativeList<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        if (selectedSquads.Length == 0)
        {
            selectedSquads.Dispose();
            return;
        }

        Faction faction = ResolveFaction(em, selectedSquads[0]);
        FilterSquadsByFaction(em, selectedSquads, faction);
        if (selectedSquads.Length == 0)
        {
            selectedSquads.Dispose();
            return;
        }

        Entity group = CreatePlayerGroup(em, selectedSquads, faction);
        selectedSquads.Dispose();
        Debug.Log($"[PlayerStrikeGroup] created group={Format(group)}");
    }

    public void DetachSelectedGroups()
    {
        if (!TryGetEm(out EntityManager em))
            return;

        NativeList<Entity> selectedSquads = CollectSelectedSquads(em, Allocator.Temp);
        for (int i = 0; i < selectedSquads.Length; i++)
            SquadConfigurator.DetachSquadFromStrikeGroup(em, selectedSquads[i]);
        selectedSquads.Dispose();

        for (int i = 1; i <= 9; i++)
            OnGroupChanged?.Invoke(i);
    }

    private static Entity GetOrCreateSelectedPlayerGroup(EntityManager em, NativeList<Entity> selectedSquads, Faction faction)
    {
        for (int i = 0; i < selectedSquads.Length; i++)
        {
            Entity squad = selectedSquads[i];
            if (!em.Exists(squad) || !em.HasComponent<StrikeGroupMember>(squad)) continue;

            StrikeGroupMember member = em.GetComponentData<StrikeGroupMember>(squad);
            if (IsValidPlayerGroup(em, member.groupEntity) && em.GetComponentData<StrikeGroupData>(member.groupEntity).faction == faction)
            {
                for (int s = 0; s < selectedSquads.Length; s++)
                    SquadConfigurator.AttachSquadToStrikeGroup(em, member.groupEntity, selectedSquads[s]);
                return member.groupEntity;
            }
        }

        return CreatePlayerGroup(em, selectedSquads, faction);
    }

    private static Entity CreatePlayerGroup(EntityManager em, NativeList<Entity> selectedSquads, Faction faction)
    {
        float2 center = float2.zero;
        int count = 0;
        for (int i = 0; i < selectedSquads.Length; i++)
        {
            Entity squad = selectedSquads[i];
            if (em.Exists(squad) && em.HasComponent<LocalTransform>(squad))
            {
                center += em.GetComponentData<LocalTransform>(squad).Position.xy;
                count++;
            }
        }

        if (count > 0)
            center /= count;

        int groupId = LevelSpawnApi.ReserveGroupId();
        Entity group = SquadConfigurator.CreateStrikeGroup(
            em,
            faction,
            groupId,
            Tactics.Neutral,
            center,
            StrikeGroupOwnership.Player,
            Entity.Null);

        for (int i = 0; i < selectedSquads.Length; i++)
            SquadConfigurator.AttachSquadToStrikeGroup(em, group, selectedSquads[i]);

        return group;
    }

    private static NativeList<Entity> CollectSelectedSquads(EntityManager em, Allocator allocator)
    {
        NativeList<Entity> result = new NativeList<Entity>(allocator);
        NativeHashSet<Entity> unique = new NativeHashSet<Entity>(32, allocator);

        EntityQuery squadQuery = em.CreateEntityQuery(ComponentType.ReadOnly<SquadronTag>(), ComponentType.ReadOnly<Selected>());
        NativeArray<Entity> squads = squadQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < squads.Length; i++)
        {
            Entity squad = squads[i];
            if (em.Exists(squad) && em.IsComponentEnabled<Selected>(squad) && unique.Add(squad))
                result.Add(squad);
        }
        squads.Dispose();
        squadQuery.Dispose();

        EntityQuery shipQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ShipSquadRef>(), ComponentType.ReadOnly<Selected>());
        NativeArray<Entity> ships = shipQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < ships.Length; i++)
        {
            Entity ship = ships[i];
            if (!em.Exists(ship) || !em.IsComponentEnabled<Selected>(ship)) continue;
            ShipSquadRef squadRef = em.GetComponentData<ShipSquadRef>(ship);
            if (squadRef.squad != Entity.Null && em.Exists(squadRef.squad) && unique.Add(squadRef.squad))
                result.Add(squadRef.squad);
        }
        ships.Dispose();
        shipQuery.Dispose();
        unique.Dispose();
        return result;
    }

    private static void CleanupGroupBuffer(EntityManager em, Entity group)
    {
        if (group == Entity.Null || !em.Exists(group) || !em.HasBuffer<StrikeGroupSquadElement>(group)) return;
        DynamicBuffer<StrikeGroupSquadElement> squads = em.GetBuffer<StrikeGroupSquadElement>(group);
        for (int i = squads.Length - 1; i >= 0; i--)
        {
            Entity squad = squads[i].squadEntity;
            if (squad == Entity.Null || !em.Exists(squad))
                squads.RemoveAt(i);
        }
    }

    private static bool IsValidPlayerGroup(EntityManager em, Entity group)
    {
        if (group == Entity.Null || !em.Exists(group) || !em.HasComponent<StrikeGroupData>(group)) return false;
        return em.GetComponentData<StrikeGroupData>(group).ownership == StrikeGroupOwnership.Player;
    }

    private static void FilterSquadsByFaction(EntityManager em, NativeList<Entity> selectedSquads, Faction faction)
    {
        for (int i = selectedSquads.Length - 1; i >= 0; i--)
        {
            Entity squad = selectedSquads[i];
            if (squad == Entity.Null || !em.Exists(squad) || ResolveFaction(em, squad) != faction)
                selectedSquads.RemoveAtSwapBack(i);
        }
    }

    private static Faction ResolveFaction(EntityManager em, Entity squad)
    {
        if (squad != Entity.Null && em.Exists(squad) && em.HasComponent<SquadComponent>(squad))
            return em.GetComponentData<SquadComponent>(squad).faction;
        return Faction.Friendly;
    }

    private static bool TryGetEm(out EntityManager em)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            em = default;
            return false;
        }

        em = world.EntityManager;
        return true;
    }

    private static string Format(Entity e) => e == Entity.Null ? "Null" : $"{e.Index}:{e.Version}";
}
