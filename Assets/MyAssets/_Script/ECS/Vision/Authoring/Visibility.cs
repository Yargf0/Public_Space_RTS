using Unity.Entities;

public struct Visibility : IComponentData, IEnableableComponent
{
    public float visibleToFriendlyTimer;
    public float visibleToEnemyTimer;
}
