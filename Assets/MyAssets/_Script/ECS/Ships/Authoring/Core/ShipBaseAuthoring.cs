using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ShipBaseAuthoring : MonoBehaviour
{
    [Header("Movement")]
    public float followDistance = GameConstants.DefaultFollowDistance;
    public float maxSpeed;
    public float acceleration;
    public float rotationSpeed;

    [Header("Identity")]
    public Faction Faction;
    public ShipSize ShipSize;
    public ShipType ShipType;
    public int shipId = -1;

    [Header("Health")]
    public float health;
    public float healthMax;
    public bool destroyOnZeroHealth = true;

    [Header("Boid")]
    public float separation;
    public float alignment;
    public float cohesion;
    public float flowFieldWeight;
    public float neighborRadius;

    [Header("Combat State Defaults")]
    public FireMode initialFireMode = FireMode.FireAtWill;
    public MoveMode initialMoveMode = MoveMode.MoveAndEngage;

    [Header("Weapon Targeting")]
    [Tooltip("If enabled, weapons on this ship ignore target pressure. They can all focus the same best target instead of spreading by load.")]
    public bool disableWeaponTargetDistribution = false;

    [Header("Combat Movement")]
    public FightLogicType FightLogicType;
    public OrbitDirection orbitDirection = OrbitDirection.Right;

    [Tooltip("Base combat distance. 0 = WeaponShipSummarySystem can fill it from weapon max range.")]
    public float idealDistance = 0f;

    [Header("Target Size Patterns")]
    public bool TargetSizeMater = false;
    public FightLogicType smallTarget = FightLogicType.Dogfight;
    public FightLogicType mediumTarget = FightLogicType.Orbit;
    public FightLogicType bigTarget = FightLogicType.AttackRun;

    [Header("Target Size Distance Multipliers")]
    [Range(0.2f, 2f)] public float smallRangeX = 0.85f;
    [Range(0.2f, 2f)] public float mediumRangeX = 1f;
    [Range(0.2f, 2.5f)] public float bigRangeX = 1.15f;

    [Header("Hold Distance Pattern")]
    [Range(0.5f, 1f)] public float tooCloseDist = 0.88f;
    [Range(1f, 1.6f)] public float tooFarDist = 1.18f;
    [Range(0f, 0.5f)] public float DriftSpeed = 0.15f;
    [Range(0.5f, 6f)] public float DriftPeriod = 2.5f;

    [Header("Orbit Pattern")]
    [Range(0f, 0.25f)] public float orbitJitter = 0.08f;
    [Range(0.5f, 6f)] public float JitterPeriod = 1.8f;
    [Range(0f, 0.5f)] public float SpeedJitter = 0.25f;

    [Header("Attack Run / Interceptor Pass Pattern")]
    [Range(0.2f, 0.8f)] public float attackRunFireRange = 0.55f;
    [Range(0.3f, 4f)] public float runFireTime = 1.2f;
    [Range(0.5f, 4f)] public float runBreakTime = 1.5f;
    [Range(0.5f, 5f)] public float runRepositionTime = 2.0f;
    [Range(0f, 180f)] public float runSpread = 70f;
    [Range(1f, 3f)] public float runRepositionDist = 1.6f;

    [Header("Interceptor Pass (lead)")]
    [Range(1f, 50f)] public float interceptorMinOwnSpeed = 5f;
    [Range(0.1f, 5f)] public float interceptorMaxLeadTime = 1.5f;

    [Header("Missile Attack Run Pattern")]
    [Range(0.5f, 2f)] public float missileLaunchDistanceMul = 1.0f;
    [Range(0.2f, 5f)] public float missileLaunchTime = 1.5f;
    [Range(1.2f, 4f)] public float missileRetreatDistanceMul = 1.8f;
    [Range(0.5f, 5f)] public float missileRetreatTime = 2.0f;
    [Range(0.5f, 8f)] public float missileReloadTime = 2.5f;

    [Header("Strafe Pattern")]
    [Range(0.5f, 3f)] public float strafeLength = 1.4f;
    [Range(0.2f, 0.9f)] public float strafeMinRange = 0.6f;

    [Header("Swarm Pattern")]
    [Range(2, 32)] public int swarmSlotsPerCircle = 8;
    [Range(-90f, 90f)] public float swarmRotationDegPerSec = 25f;

    [Header("AI Demo Debug")]
    [Tooltip("Draw combat pattern lines for this ship in AiTest visualization.")]
    public bool drawFightPatternDebug = false;
    [Tooltip("Draw combat HUD label for this ship when AiDemoShipDebugHud filters by marker.")]
    public bool drawFightPatternHud = false;

    [Header("AI Demo Swarm Debug")]
    [Tooltip("When Draw Fight Pattern is enabled and the active pattern is Swarm, draw sector guides around the target.")]
    public bool debugDrawSwarmSectors = true;
    [Tooltip("Draw all Swarm sector borders. If disabled, only this ship's own sector is highlighted.")]
    public bool debugDrawAllSwarmSectors = false;
    [Tooltip("Draw an extra line/cross for this ship's current Swarm slot point.")]
    public bool debugDrawSwarmSlotPoint = true;

    [Header("AI Demo HUD Fields")]
    public bool hudShowShipSize = true;
    public bool hudShowShipState = true;
    public bool hudShowMoveMode = true;
    public bool hudShowFireMode = true;
    public bool hudShowFightPattern = true;
    public bool hudShowFightPhase = true;
    public bool hudShowSquad = true;
    public bool hudShowTarget = true;

    [Header("Visibility")]
    public float spottedTimer;

    [Header("Fog of War")]
    public bool useFogOfWar;

    [Header("Searchlight")]
    [Tooltip("Adds Searchlight and SearchlightState to this same ship entity. Do not add SearchlightAuthoring on the same root when this is enabled.")]
    public bool addSelfSearchlight = false;
    [Min(0f)] public float searchlightRange = 20f;
    [Range(1f, 360f)] public float searchlightConeAngle = 360f;
    [Range(0f, 1f)] public float searchlightOpacity = 0.1f;
    [Min(0f)] public float searchlightKeepVisibleSeconds = 0.1f;
    [Min(0f)] public float searchlightScanInterval = 0.02f;

    [Header("Searchlight Gizmos")]
    public bool drawSearchlightGizmo = true;
    public Color searchlightGizmoColor = new Color(1f, 0.92f, 0f, 0.9f);

    [Header("Collision")]
    public float2 collisionRadius = new float2(1f, 1f);

    [Header("Pathfinding")]
    public bool usePathSizeOverride = false;
    public PathfindingSizeClass overridePathSize = PathfindingSizeClass.Medium;

    [Header("Collision Gizmos")]
    public bool drawAlways = true;
    public bool drawOnlySelected = false;
    public Color gizmoColor = new Color(0f, 1f, 1f, 0.9f);

    [Header("Agro")]
    public float agroDetectionTime;
    public bool agroNeedDistance;
    public float agroDetectionRadius;

    class Baker : Baker<ShipBaseAuthoring>
    {
        public override void Bake(ShipBaseAuthoring a)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Unit
            {
                faction = a.Faction,
                shipSize = (byte)a.ShipSize,
            });

            PathfindingSizeClass pathSize = a.usePathSizeOverride
                ? a.overridePathSize
                : MapShipSizeToPathfindingSize(a.ShipSize);

            AddComponent(entity, new PathfindingSizeClassComponent
            {
                Value = pathSize,
            });

            AddComponent(entity, new ShipTypeIndex
            {
                Value = (int)a.ShipType,
            });

            if (a.shipId >= 0)
            {
                AddComponent(entity, new ShipCatalogId
                {
                    Value = a.shipId,
                });
            }

            if (a.Faction == Faction.Friendly)
            {
                AddComponent(entity, new Friendly());
            }
            else
            {
                AddComponent(entity, new Enemy());
            }

            AddComponent(entity, new UnitMover
            {
                maxSpeed = a.maxSpeed,
                acceleration = a.acceleration,
                rotationSpeed = a.rotationSpeed,
                followDistance = a.followDistance > 0f ? a.followDistance : GameConstants.DefaultFollowDistance,
            });

            AddComponent(entity, new Health
            {
                healthAmount = a.health,
                healthAmountMax = a.healthMax,
                onHealthChanged = true,
                destroyOnZeroHealth = a.destroyOnZeroHealth,
            });

            AddDisabledEmbeddedActionStatuses(entity);

            AddComponent(entity, new Boid
            {
                separationWeight = a.separation,
                alignmentWeight = a.alignment,
                cohesionWeight = a.cohesion,
                flowFieldWeight = a.flowFieldWeight,
                neighborRadius = a.neighborRadius,
            });

            AddComponent(entity, new FightLogic
            {
                movementType = a.FightLogicType,
                orbitDirection = (float)a.orbitDirection,
                idealDistance = a.idealDistance,

                useTargetSizePatterns = a.TargetSizeMater ? (byte)1 : (byte)0,
                smallTargetPattern = a.smallTarget,
                mediumTargetPattern = a.mediumTarget,
                bigTargetPattern = a.bigTarget,
                smallTargetIdealDistanceMultiplier = a.smallRangeX,
                mediumTargetIdealDistanceMultiplier = a.mediumRangeX,
                bigTargetIdealDistanceMultiplier = a.bigRangeX,

                holdHysteresisInner = a.tooCloseDist,
                holdHysteresisOuter = a.tooFarDist,
                holdDriftSpeed = a.DriftSpeed,
                holdDriftPeriod = math.max(0.1f, a.DriftPeriod),

                orbitRadiusJitter = a.orbitJitter,
                orbitJitterPeriod = math.max(0.1f, a.JitterPeriod),
                orbitSpeedJitter = a.SpeedJitter,

                attackRunFireRange = a.attackRunFireRange,
                attackRunFiringDuration = a.runFireTime,
                attackRunBreakawayDuration = a.runBreakTime,
                attackRunRepositionDuration = a.runRepositionTime,
                attackRunRepositionSpread = math.radians(a.runSpread),
                attackRunRepositionDistance = a.runRepositionDist,

                interceptorMinOwnSpeedForLead = math.max(0.1f, a.interceptorMinOwnSpeed),
                interceptorMaxLeadTime = math.max(0.05f, a.interceptorMaxLeadTime),

                missileLaunchDistance = math.max(0.1f, a.missileLaunchDistanceMul),
                missileLaunchDuration = math.max(0.05f, a.missileLaunchTime),
                missileRetreatDistance = math.max(a.missileLaunchDistanceMul + 0.1f, a.missileRetreatDistanceMul),
                missileRetreatDuration = math.max(0.05f, a.missileRetreatTime),
                missileReloadDuration = math.max(0f, a.missileReloadTime),

                strafeLegLength = a.strafeLength,
                strafeMinDistance = a.strafeMinRange,

                swarmSlotsPerCircle = math.max(2, a.swarmSlotsPerCircle),
                swarmRotationDegPerSec = a.swarmRotationDegPerSec,
            });

            AddComponent(entity, new FightPatternState
            {
                activePattern = (FightLogicType)byte.MaxValue,
                rngState = 0u,
            });

            if (a.drawFightPatternDebug || a.drawFightPatternHud)
            {
                AddComponent(entity, new FightPatternDebugMarker
                {
                    DrawPattern = a.drawFightPatternDebug ? (byte)1 : (byte)0,
                    DrawHud = a.drawFightPatternHud ? (byte)1 : (byte)0,
                    HudShowShipSize = a.hudShowShipSize ? (byte)1 : (byte)0,
                    HudShowShipState = a.hudShowShipState ? (byte)1 : (byte)0,
                    HudShowMoveMode = a.hudShowMoveMode ? (byte)1 : (byte)0,
                    HudShowFireMode = a.hudShowFireMode ? (byte)1 : (byte)0,
                    HudShowFightPattern = a.hudShowFightPattern ? (byte)1 : (byte)0,
                    HudShowFightPhase = a.hudShowFightPhase ? (byte)1 : (byte)0,
                    HudShowSquad = a.hudShowSquad ? (byte)1 : (byte)0,
                    HudShowTarget = a.hudShowTarget ? (byte)1 : (byte)0,
                    DrawSwarmSectors = a.debugDrawSwarmSectors ? (byte)1 : (byte)0,
                    DrawAllSwarmSectors = a.debugDrawAllSwarmSectors ? (byte)1 : (byte)0,
                    DrawSwarmSlotPoint = a.debugDrawSwarmSlotPoint ? (byte)1 : (byte)0,
                });
            }

            AddComponent(entity, new Visibility
            {
                visibleToFriendlyTimer = a.spottedTimer,
                visibleToEnemyTimer = 0f,
            });
            SetComponentEnabled<Visibility>(entity, a.spottedTimer > 0f);

            AddComponent(entity, new LastKnownTarget
            {
                target = Entity.Null,
                lastKnownPosition = default,
                searchTimer = 0f,
            });
            SetComponentEnabled<LastKnownTarget>(entity, false);

            if (a.useFogOfWar)
            {
                AddComponent<UseFogOfWar>(entity);
            }

            if (a.addSelfSearchlight)
            {
                SearchlightAuthoring existingSearchlightAuthoring = a.GetComponent<SearchlightAuthoring>();
                if (existingSearchlightAuthoring != null)
                {
                    Debug.LogError($"ShipBaseAuthoring '{a.name}': Add Self Searchlight is enabled, but SearchlightAuthoring is also present on the same GameObject. Use only one source.", a);
                }
                else
                {
                    AddComponent(entity, new Searchlight
                    {
                        range = math.max(0f, a.searchlightRange),
                        coneAngle = math.clamp(a.searchlightConeAngle, 1f, 360f),
                        opacity = math.saturate(a.searchlightOpacity),
                        keepVisibleSeconds = math.max(0f, a.searchlightKeepVisibleSeconds),
                        scanInterval = math.max(0f, a.searchlightScanInterval),
                        scansFaction = VisibilityUtility.Opposite(a.Faction),
                        observerFaction = a.Faction,
                    });

                    AddComponent(entity, new SearchlightState
                    {
                        ScanTimer = 0f,
                    });

                    AddComponent<SelfSearchlight>(entity);
                }
            }

            AddComponent(entity, new UnitCollisionRadius
            {
                collisionRadius = a.collisionRadius,
            });

            AddComponent(entity, new ShipAgro
            {
                detectionRadius = a.agroDetectionRadius,
                needDistance = a.agroNeedDistance,
                detectionTime = a.agroDetectionTime,
                attackRangeMin = int.MaxValue,
                attackRangeMax = 0,
            });
            SetComponentEnabled<ShipAgro>(entity, false);

            AddComponent(entity, new UnitGroup());
            AddComponent(entity, new ShipToGrid());
            AddComponent(entity, new Velocity());
            AddComponent(entity, new GroupManagerComponent());

            AddComponent(entity, new ShipStateComponent
            {
                mode = a.initialFireMode,
                moveMode = a.initialMoveMode,
                currentState = ShipState.Idle,
                previousState = ShipState.Idle,
                forcedTarget = Entity.Null,
            });

            if (a.disableWeaponTargetDistribution)
            {
                AddComponent<DisableWeaponTargetDistribution>(entity);
            }

            AddBuffer<CommandQueueElement>(entity);
        }

        private void AddDisabledEmbeddedActionStatuses(Entity entity)
        {
            AddComponent(entity, new EmpStatus
            {
                timer = 0f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
                disableWeapons = false,
            });
            SetComponentEnabled<EmpStatus>(entity, false);

            AddComponent(entity, new EmbeddedActionBuffStatus
            {
                timer = 0f,
                effectMultiplier = 1f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
            });
            SetComponentEnabled<EmbeddedActionBuffStatus>(entity, false);

            AddComponent(entity, new EmbeddedActionDebuffStatus
            {
                timer = 0f,
                effectMultiplier = 1f,
                moveSpeedMultiplier = 1f,
                accelerationMultiplier = 1f,
                disableWeapons = false,
            });
            SetComponentEnabled<EmbeddedActionDebuffStatus>(entity, false);
        }

        private static PathfindingSizeClass MapShipSizeToPathfindingSize(ShipSize shipSize)
        {
            return shipSize switch
            {
                ShipSize.Small => PathfindingSizeClass.Small,
                ShipSize.RocketSmall => PathfindingSizeClass.Small,
                ShipSize.Medium => PathfindingSizeClass.Medium,
                ShipSize.Big => PathfindingSizeClass.Large,
                ShipSize.RocketBig => PathfindingSizeClass.Large,
                _ => PathfindingSizeClass.Medium,
            };
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawAlways) return;
        if (drawOnlySelected) return;

        DrawRadiusGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (drawOnlySelected || drawAlways)
        {
            DrawRadiusGizmo();
        }

        DrawSearchlightGizmo();
    }

    private void DrawSearchlightGizmo()
    {
        if (!addSelfSearchlight || !drawSearchlightGizmo) return;
        if (searchlightRange <= 0f) return;

        Handles.color = searchlightGizmoColor;

        Vector3 pos = transform.position;
        Vector3 normal = Vector3.forward;
        float angle = Mathf.Clamp(searchlightConeAngle, 1f, 360f);

        if (angle >= 359.9f)
        {
            Handles.DrawWireDisc(pos, normal, searchlightRange);
            return;
        }

        Vector3 forward = transform.up;
        Vector3 from = Quaternion.AngleAxis(-angle * 0.5f, normal) * forward;
        Handles.DrawWireArc(pos, normal, from, angle, searchlightRange);
        Handles.DrawLine(pos, pos + from.normalized * searchlightRange);
        Handles.DrawLine(pos, pos + (Quaternion.AngleAxis(angle, normal) * from).normalized * searchlightRange);
    }

    private void DrawRadiusGizmo()
    {
        float rx = Mathf.Max(0f, Mathf.Abs(collisionRadius.x));
        float ry = Mathf.Max(0f, Mathf.Abs(collisionRadius.y));

        if (rx <= 0f && ry <= 0f) return;

        Handles.color = gizmoColor;

        Matrix4x4 old = Handles.matrix;
        Handles.matrix = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(rx, ry, 1f));
        Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f);
        Handles.matrix = old;
    }
#endif
}
