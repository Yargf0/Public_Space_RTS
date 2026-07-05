using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CriticalShipAuthoring : MonoBehaviour
{
    public CriticalShipType criticalShipType = CriticalShipType.MainShip;
    public bool includeInAllAliveCheck = true;
    public bool showInHud = true;

    class Baker : Baker<CriticalShipAuthoring>
    {
        public override void Bake(CriticalShipAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new CriticalShipRole
            {
                type = (byte)authoring.criticalShipType,
                includeInAllAliveCheck = authoring.includeInAllAliveCheck,
                showInHud = authoring.showInHud,
            });

            AddComponent(entity, new CriticalShipStatus
            {
                position = float3.zero,
                healthNormalized = 1f,
                isAlive = true,
            });
        }
    }
}

public enum CriticalShipType : byte
{
    MainShip,
    KeyShip,
    Station,
    Other,
}

public struct CriticalShipRole : IComponentData
{
    public byte type;
    public bool includeInAllAliveCheck;
    public bool showInHud;
}

public struct CriticalShipStatus : IComponentData
{
    public float3 position;
    public float healthNormalized;
    public bool isAlive;
}

public struct CriticalObjectivesState : IComponentData
{
    public int totalCount;
    public int aliveCount;
    public bool allAlive;
}