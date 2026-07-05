using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateAfter(typeof(GridSystemInit))]
partial struct ShipToGridSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        RefRW<GridData> gridData = SystemAPI.GetSingletonRW<GridData>();

        gridData.ValueRW.EnemyEntityMap.Clear();
        gridData.ValueRW.FriendlyEntityMap.Clear();
        gridData.ValueRW.EnemyEntityBigMap.Clear();
        gridData.ValueRW.FriendlyEntityBigMap.Clear();

        foreach ((
            RefRO<ShipToGrid> shipToGrid,
            RefRO<LocalTransform> localTransform,
            RefRO<UnitCollisionRadius> unitCollisionRadius,
            RefRO<Velocity> velocity,
            RefRO<Enemy> enemy,
            RefRO<Unit> unit,
            Entity entity)
            in SystemAPI.Query<
                RefRO<ShipToGrid>,
                RefRO<LocalTransform>,
                RefRO<UnitCollisionRadius>,
                RefRO<Velocity>,
                RefRO<Enemy>,
                RefRO<Unit>>().WithAbsent<SquadronTag>().WithEntityAccess())
        {
            float2 actualPosition = localTransform.ValueRO.Position.xy;
            float2 collisionRadius = unitCollisionRadius.ValueRO.collisionRadius;

            Grid grid = new Grid
            {
                Entity = entity,
                Position = actualPosition,
                CollisionRadius = collisionRadius,
                Heading = math.normalizesafe(velocity.ValueRO.velocity),
                ShipSize = unit.ValueRO.shipSize,
            };

            if (collisionRadius.x > GameConstants.SmallGridCellSize || collisionRadius.y > GameConstants.SmallGridCellSize)
            {
                int2 posMax = GridUtility.WorldToSmallCell(actualPosition + collisionRadius);
                int2 posMin = GridUtility.WorldToSmallCell(actualPosition - collisionRadius);

                for (int i = posMin.x; i <= posMax.x; i++)
                {
                    for (int io = posMin.y; io <= posMax.y; io++)
                    {
                        gridData.ValueRW.EnemyEntityMap.Add(new int2(i, io), grid);
                    }
                }
            }
            else
            {
                gridData.ValueRW.EnemyEntityMap.Add(GridUtility.WorldToSmallCell(actualPosition), grid);
            }

            gridData.ValueRW.EnemyEntityBigMap.Add(GridUtility.WorldToBigCell(actualPosition), grid);
        }

        foreach ((
            RefRO<ShipToGrid> shipToGrid,
            RefRO<LocalTransform> localTransform,
            RefRO<UnitCollisionRadius> unitCollisionRadius,
            RefRO<Velocity> velocity,
            RefRO<Friendly> friendly,
            RefRO<Unit> unit,
            Entity entity)
            in SystemAPI.Query<
                RefRO<ShipToGrid>,
                RefRO<LocalTransform>,
                RefRO<UnitCollisionRadius>,
                RefRO<Velocity>,
                RefRO<Friendly>,
                RefRO<Unit>>().WithAbsent<SquadronTag>().WithEntityAccess())
        {
            float2 actualPosition = localTransform.ValueRO.Position.xy;
            float2 collisionRadius = unitCollisionRadius.ValueRO.collisionRadius;

            Grid grid = new Grid
            {
                Entity = entity,
                Position = actualPosition,
                CollisionRadius = collisionRadius,
                Heading = math.normalizesafe(velocity.ValueRO.velocity),
                ShipSize = unit.ValueRO.shipSize,
            };

            if (collisionRadius.x > GameConstants.SmallGridCellSize || collisionRadius.y > GameConstants.SmallGridCellSize)
            {
                int2 posMax = GridUtility.WorldToSmallCell(actualPosition + collisionRadius);
                int2 posMin = GridUtility.WorldToSmallCell(actualPosition - collisionRadius);

                for (int i = posMin.x; i <= posMax.x; i++)
                {
                    for (int io = posMin.y; io <= posMax.y; io++)
                    {
                        gridData.ValueRW.FriendlyEntityMap.Add(new int2(i, io), grid);
                    }
                }
            }
            else
            {
                gridData.ValueRW.FriendlyEntityMap.Add(GridUtility.WorldToSmallCell(actualPosition), grid);
            }

            gridData.ValueRW.FriendlyEntityBigMap.Add(GridUtility.WorldToBigCell(actualPosition), grid);
        }
    }
}
