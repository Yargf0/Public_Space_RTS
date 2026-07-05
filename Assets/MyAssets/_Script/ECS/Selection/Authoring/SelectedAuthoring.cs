using Unity.Entities;
using UnityEngine;


public class SelectedAuthoring : MonoBehaviour
{
    public GameObject VisualGameObject;
    public float showScale;
    public class SelectedAuthoringBaker : Baker<SelectedAuthoring>
    {
        public override void Bake(SelectedAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Selected
            {
                VisualEntity = GetEntity(authoring.VisualGameObject, TransformUsageFlags.Dynamic),
                ShowScale = authoring.showScale,

            });
            SetComponentEnabled<Selected>(entity, false);
        }
    }
}

public struct Selected: IComponentData, IEnableableComponent
{
    public float ShowScale;
    public Entity VisualEntity;

    public bool OnSelected;
    public bool OnDeselected;
}
