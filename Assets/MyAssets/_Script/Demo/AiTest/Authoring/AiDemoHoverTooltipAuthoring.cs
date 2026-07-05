using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AiDemoHoverTooltipAuthoring : MonoBehaviour
{
    [Header("Demo Tooltip")]
    [TextArea(3, 10)]
    public string tooltipText;

    private class Baker : Baker<AiDemoHoverTooltipAuthoring>
    {
        public override void Bake(AiDemoHoverTooltipAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new AiDemoHoverTooltip
            {
                text = new FixedString512Bytes(authoring.tooltipText == null ? string.Empty : authoring.tooltipText)
            });
        }
    }
}

public struct AiDemoHoverTooltip : IComponentData
{
    public FixedString512Bytes text;
}

