using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class AiDemoSearchlightDebugAuthoring : MonoBehaviour
{
    [FormerlySerializedAs("enabled")]
    public bool debugEnabled = true;

    private class Baker : Baker<AiDemoSearchlightDebugAuthoring>
    {
        public override void Bake(AiDemoSearchlightDebugAuthoring a)
        {
            Entity e = GetEntity(TransformUsageFlags.None);
            AddComponent(e, new AiDemoSearchlightDebugSettings
            {
                enabled = a.debugEnabled ? (byte)1 : (byte)0,
            });
        }
    }
}
