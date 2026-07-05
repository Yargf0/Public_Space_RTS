using UnityEngine;

// data-only material variant marker, put on turret mesh / visual root
// does NOT touch materials in OnValidate/OnEnable, so baking and shooting are safe
[DisallowMultipleComponent]
public class FactionMaterialVariantAuthoring : MonoBehaviour
{
    public Material friendlyMaterial;
    public Material enemyMaterial;
}
