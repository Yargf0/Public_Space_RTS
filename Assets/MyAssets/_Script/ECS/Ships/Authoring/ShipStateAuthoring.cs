using Unity.Entities;
using UnityEngine;

public class ShipStateAuthoring : MonoBehaviour
{
    class Baker : Baker<ShipStateAuthoring>
    {
        public override void Bake(ShipStateAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ShipStateComponent
            {
                mode = FireMode.FireAtWill,
                moveMode = MoveMode.HoldPosition,
                currentState = ShipState.Idle,
                previousState = ShipState.Idle,
                forcedTarget = Entity.Null,
            });
        }
    }
}

public struct ShipStateComponent : IComponentData
{
    public FireMode mode;
    public MoveMode moveMode;
    public ShipState currentState;
    public ShipState previousState;
    public Entity forcedTarget;
}