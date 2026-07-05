using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public static class StrategicIconKeys
{
    public const int EnemySmall = -1;
    public const int EnemyMedium = -2;
    public const int EnemyBig = -3;
    public const int Fallback = -100;

    public static int ResolveEnemyKey(byte shipSize)
    {
        ShipSize size = (ShipSize)shipSize;
        if ((size & (ShipSize.Big | ShipSize.RocketBig)) != 0)
        {
            return EnemyBig;
        }

        if ((size & ShipSize.Medium) != 0)
        {
            return EnemyMedium;
        }

        return EnemySmall;
    }
}

public struct StrategicIconOwner : IComponentData
{
    public Entity Icon;
}

public struct StrategicIcon : IComponentData
{
    public Entity Owner;
    public Faction Faction;
    public byte ShipSize;
    public int IconKey;
}

public struct StrategicIconSettings : IComponentData
{
    public float DefaultSizePixels;
    public float SmallSizePixels;
    public float MediumSizePixels;
    public float BigSizePixels;

    public float FadeStartOrthoSize;
    public float FadeFullOrthoSize;
    public float ScreenPaddingPixels;
    public float IconZ;

    public byte ShowFriendly;
    public byte ShowEnemy;
    public byte RespectFogOfWar;

    public float4 FriendlyLowHealthColor;
    public float4 FriendlyFullHealthColor;
    public float4 EnemyLowHealthColor;
    public float4 EnemyFullHealthColor;
}

public struct StrategicIconCameraData : IComponentData
{
    public float2 Center;
    public float2 HalfExtents;
    public float WorldUnitsPerPixel;
    public float ScreenPaddingWorld;
    public float ZoomAlpha;
    public byte IsValid;
}

public struct StrategicIconRenderData : IComponentData
{
    public Entity IconPrefab;
    public MaterialMeshInfo FallbackMaterial;
}

public struct StrategicIconMaterialEntry : IBufferElementData
{
    public int IconKey;
    public MaterialMeshInfo MaterialMeshInfo;
}
