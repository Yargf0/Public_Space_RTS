using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AiDemoShipRuntimeConfigAuthoring : MonoBehaviour
{
    [Header("Apply")]
    [Tooltip("If true, config is applied once and then removed from entity.")]
    public bool applyOnce = true;

    [Tooltip("If this entity is a squad entity, also apply config to its members.")]
    public bool applyToSquadMembers = true;

    [Tooltip("Clear existing command buffers so old player/test commands do not override this demo setup.")]
    public bool clearCommandQueue = true;

    [Header("State")]
    public bool overrideState = true;
    public ShipState state = ShipState.MovingToTarget;

    [Header("Move Mode")]
    public bool overrideMoveMode = true;
    public MoveMode moveMode = MoveMode.MoveAndEngage;

    [Header("Fire Mode")]
    public bool overrideFireMode = true;
    public FireMode fireMode = FireMode.FireAtWill;

    [Header("Combat Movement Pattern")]
    public bool overrideMovementType = true;
    public FightLogicType movementType = FightLogicType.HoldDistance;
    public OrbitDirection orbitDirection = OrbitDirection.Right;

    public bool overrideIdealDistance = false;
    public float idealDistance = 18f;

    [Header("Target Position")]
    public bool overrideTargetPosition = false;

    [Tooltip("Optional scene object used only as position source.")]
    public Transform targetPoint;

    public Vector2 targetPosition;

    [Header("Forced Target")]
    public bool overrideForcedTarget = false;
    public GameObject forcedTarget;

    [Tooltip("If no forced target is assigned, this clears old forcedTarget.")]
    public bool clearForcedTarget = true;

    [Header("Ship Agro")]
    public bool overrideAgroEnabled = true;
    public bool agroEnabled = true;

    public bool overrideAgroSettings = false;
    public bool agroNeedDistance = false;
    public float agroDetectionRadius = 60f;
    public float agroDetectionTime = 0.1f;
    public float attackRangeMin = 0f;
    public float attackRangeMax = 60f;

    private class Baker : Baker<AiDemoShipRuntimeConfigAuthoring>
    {
        public override void Bake(AiDemoShipRuntimeConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            Entity forcedTargetEntity = Entity.Null;
            if (authoring.forcedTarget != null)
            {
                forcedTargetEntity = GetEntity(authoring.forcedTarget, TransformUsageFlags.Dynamic);
            }

            Vector2 finalTargetPosition = authoring.targetPosition;
            if (authoring.targetPoint != null)
            {
                Vector3 pos = authoring.targetPoint.position;
                finalTargetPosition = new Vector2(pos.x, pos.y);
            }

            AddComponent(entity, new AiDemoShipRuntimeConfig
            {
                applyOnce = authoring.applyOnce,
                applyToSquadMembers = authoring.applyToSquadMembers,
                clearCommandQueue = authoring.clearCommandQueue,

                overrideState = authoring.overrideState,
                state = authoring.state,

                overrideMoveMode = authoring.overrideMoveMode,
                moveMode = authoring.moveMode,

                overrideFireMode = authoring.overrideFireMode,
                fireMode = authoring.fireMode,

                overrideMovementType = authoring.overrideMovementType,
                movementType = authoring.movementType,
                orbitDirection = (float)authoring.orbitDirection,

                overrideIdealDistance = authoring.overrideIdealDistance,
                idealDistance = authoring.idealDistance,

                overrideTargetPosition = authoring.overrideTargetPosition,
                targetPosition = finalTargetPosition,

                overrideForcedTarget = authoring.overrideForcedTarget,
                forcedTarget = forcedTargetEntity,
                clearForcedTarget = authoring.clearForcedTarget,

                overrideAgroEnabled = authoring.overrideAgroEnabled,
                agroEnabled = authoring.agroEnabled,

                overrideAgroSettings = authoring.overrideAgroSettings,
                agroNeedDistance = authoring.agroNeedDistance,
                agroDetectionRadius = authoring.agroDetectionRadius,
                agroDetectionTime = authoring.agroDetectionTime,
                attackRangeMin = authoring.attackRangeMin,
                attackRangeMax = authoring.attackRangeMax,
            });
        }
    }
}

public struct AiDemoShipRuntimeConfig : IComponentData
{
    public bool applyOnce;
    public bool applyToSquadMembers;
    public bool clearCommandQueue;

    public bool overrideState;
    public ShipState state;

    public bool overrideMoveMode;
    public MoveMode moveMode;

    public bool overrideFireMode;
    public FireMode fireMode;

    public bool overrideMovementType;
    public FightLogicType movementType;
    public float orbitDirection;

    public bool overrideIdealDistance;
    public float idealDistance;

    public bool overrideTargetPosition;
    public float2 targetPosition;

    public bool overrideForcedTarget;
    public Entity forcedTarget;
    public bool clearForcedTarget;

    public bool overrideAgroEnabled;
    public bool agroEnabled;

    public bool overrideAgroSettings;
    public bool agroNeedDistance;
    public float agroDetectionRadius;
    public float agroDetectionTime;
    public float attackRangeMin;
    public float attackRangeMax;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SquadSpawnSystem))]
public partial struct AiDemoCreateSquadCommandConfigSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        NativeList<Entity> removeList = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<AiDemoShipRuntimeConfig> config, RefRW<CreateSquadCommand> command, Entity entity)
            in SystemAPI.Query<RefRO<AiDemoShipRuntimeConfig>, RefRW<CreateSquadCommand>>()
                .WithEntityAccess())
        {
            AiDemoShipRuntimeConfig c = config.ValueRO;

            if (c.overrideMoveMode)
            {
                command.ValueRW.defaultMoveMode = c.moveMode;
            }

            if (c.overrideFireMode)
            {
                command.ValueRW.defaultFireMode = c.fireMode;
            }

            if (c.overrideTargetPosition)
            {
                command.ValueRW.spawnAnchor = c.targetPosition;
            }

            if (c.applyOnce)
            {
                removeList.Add(entity);
            }
        }

        for (int i = 0; i < removeList.Length; i++)
        {
            Entity entity = removeList[i];
            if (em.Exists(entity) && em.HasComponent<AiDemoShipRuntimeConfig>(entity))
            {
                em.RemoveComponent<AiDemoShipRuntimeConfig>(entity);
            }
        }

        removeList.Dispose();
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SquadDefaultsSystem))]
[UpdateBefore(typeof(ShipAgroSystem))]
public partial struct AiDemoShipRuntimeConfigApplySystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        NativeList<Entity> removeList = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<AiDemoShipRuntimeConfig> config, Entity entity)
            in SystemAPI.Query<RefRO<AiDemoShipRuntimeConfig>>()
                .WithEntityAccess())
        {
            if (!em.Exists(entity))
                continue;

            // CreateSquadCommand is handled before SquadSpawnSystem.
            if (em.HasComponent<CreateSquadCommand>(entity))
                continue;

            AiDemoShipRuntimeConfig c = config.ValueRO;

            ApplyToEntity(em, entity, c);

            if (c.applyToSquadMembers && em.HasBuffer<SquadMember>(entity))
            {
                DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(entity);

                for (int i = 0; i < members.Length; i++)
                {
                    Entity ship = members[i].ship;
                    if (ship != Entity.Null && em.Exists(ship))
                    {
                        ApplyToEntity(em, ship, c);
                    }
                }
            }

            if (c.applyOnce)
            {
                removeList.Add(entity);
            }
        }

        for (int i = 0; i < removeList.Length; i++)
        {
            Entity entity = removeList[i];
            if (em.Exists(entity) && em.HasComponent<AiDemoShipRuntimeConfig>(entity))
            {
                em.RemoveComponent<AiDemoShipRuntimeConfig>(entity);
            }
        }

        removeList.Dispose();
    }

    private static void ApplyToEntity(EntityManager em, Entity entity, in AiDemoShipRuntimeConfig c)
    {
        ApplySquadDefaults(em, entity, c);
        ApplyShipState(em, entity, c);
        ApplyUnitMover(em, entity, c);
        ApplyFightLogic(em, entity, c);
        ApplyShipAgro(em, entity, c);
        ClearCommands(em, entity, c);
    }

    private static void ApplySquadDefaults(EntityManager em, Entity entity, in AiDemoShipRuntimeConfig c)
    {
        if (!em.HasComponent<SquadComponent>(entity))
            return;

        SquadComponent squad = em.GetComponentData<SquadComponent>(entity);

        if (c.overrideMoveMode)
        {
            squad.defaultMoveMode = c.moveMode;
        }

        if (c.overrideFireMode)
        {
            squad.defaultFireMode = c.fireMode;
        }

        if (c.overrideTargetPosition)
        {
            squad.anchorPosition = c.targetPosition;
        }

        if (c.overrideForcedTarget)
        {
            squad.priorityTarget = ResolveExistingTarget(em, c.forcedTarget);
        }
        else if (c.clearForcedTarget)
        {
            squad.priorityTarget = Entity.Null;
        }

        em.SetComponentData(entity, squad);
    }

    private static void ApplyShipState(EntityManager em, Entity entity, in AiDemoShipRuntimeConfig c)
    {
        if (!em.HasComponent<ShipStateComponent>(entity))
            return;

        ShipStateComponent shipState = em.GetComponentData<ShipStateComponent>(entity);

        if (c.overrideState && shipState.currentState != c.state)
        {
            shipState.previousState = shipState.currentState;
            shipState.currentState = c.state;
        }

        if (c.overrideMoveMode)
        {
            shipState.moveMode = c.moveMode;
        }

        if (c.overrideFireMode)
        {
            shipState.mode = c.fireMode;
        }

        if (c.overrideForcedTarget)
        {
            shipState.forcedTarget = ResolveExistingTarget(em, c.forcedTarget);
        }
        else if (c.clearForcedTarget)
        {
            shipState.forcedTarget = Entity.Null;
        }

        em.SetComponentData(entity, shipState);
    }

    private static void ApplyUnitMover(EntityManager em, Entity entity, in AiDemoShipRuntimeConfig c)
    {
        if (!c.overrideTargetPosition)
            return;

        if (!em.HasComponent<UnitMover>(entity))
            return;

        UnitMover mover = em.GetComponentData<UnitMover>(entity);
        mover.targetPos = c.targetPosition;
        mover.fightTarget = c.targetPosition;
        em.SetComponentData(entity, mover);
    }

    private static void ApplyFightLogic(EntityManager em, Entity entity, in AiDemoShipRuntimeConfig c)
    {
        if (!em.HasComponent<FightLogic>(entity))
            return;

        FightLogic fightLogic = em.GetComponentData<FightLogic>(entity);

        if (c.overrideMovementType)
        {
            fightLogic.movementType = c.movementType;
            fightLogic.orbitDirection = c.orbitDirection;
        }

        if (c.overrideIdealDistance)
        {
            fightLogic.idealDistance = c.idealDistance;
        }

        em.SetComponentData(entity, fightLogic);
    }

    private static void ApplyShipAgro(EntityManager em, Entity entity, in AiDemoShipRuntimeConfig c)
    {
        if (!em.HasComponent<ShipAgro>(entity))
            return;

        ShipAgro agro = em.GetComponentData<ShipAgro>(entity);

        if (c.overrideAgroSettings)
        {
            agro.needDistance = c.agroNeedDistance;
            agro.detectionRadius = c.agroDetectionRadius;
            agro.detectionTime = c.agroDetectionTime;
            agro.attackRangeMin = c.attackRangeMin;
            agro.attackRangeMax = c.attackRangeMax;
        }

        if (c.overrideTargetPosition)
        {
            agro.targetPosition = c.targetPosition;
        }

        if (c.overrideForcedTarget)
        {
            agro.targetEntity = ResolveExistingTarget(em, c.forcedTarget);
        }
        else if (c.clearForcedTarget)
        {
            agro.targetEntity = Entity.Null;
        }

        em.SetComponentData(entity, agro);

        if (c.overrideAgroEnabled)
        {
            em.SetComponentEnabled<ShipAgro>(entity, c.agroEnabled);
        }
    }

    private static void ClearCommands(EntityManager em, Entity entity, in AiDemoShipRuntimeConfig c)
    {
        if (!c.clearCommandQueue)
            return;

        if (em.HasBuffer<CommandQueueElement>(entity))
        {
            em.GetBuffer<CommandQueueElement>(entity).Clear();
        }

        if (em.HasBuffer<SquadCommandElement>(entity))
        {
            em.GetBuffer<SquadCommandElement>(entity).Clear();
        }
    }

    private static Entity ResolveExistingTarget(EntityManager em, Entity target)
    {
        if (target == Entity.Null)
            return Entity.Null;

        if (!em.Exists(target))
            return Entity.Null;

        return target;
    }
}