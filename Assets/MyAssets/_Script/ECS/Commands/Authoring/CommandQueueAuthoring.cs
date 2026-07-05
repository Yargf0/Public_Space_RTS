using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CommandQueueAuthoring : MonoBehaviour
{
}

public struct CommandQueueElement : IBufferElementData
{
    public CommandType type;
    public float2 targetPosition;
    public Entity targetEntity;
    public MoveMode moveMode;
}