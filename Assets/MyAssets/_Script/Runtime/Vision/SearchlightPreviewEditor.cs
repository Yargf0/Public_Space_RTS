using UnityEngine;

[ExecuteAlways]
public class SearchlightPreviewEditor : MonoBehaviour
{
    public Transform visual;
    public bool previewInEditor = true;

    public string isCircleProperty = "_IsCircle";
    public string opacityProperty = "_Opacity";

    private SearchlightAuthoring _authoring;
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    private void OnEnable()
    {
        _authoring = GetComponent<SearchlightAuthoring>();

        if (visual != null)
            _renderer = visual.GetComponent<Renderer>();

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        Apply();
    }

    private void OnValidate()
    {
        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        _authoring = GetComponent<SearchlightAuthoring>();

        if (visual != null)
            _renderer = visual.GetComponent<Renderer>();

        Apply();
    }

    private void Update()
    {
        if (!previewInEditor)
            return;

        // uncomment if this should work only in editor:
        // if (Application.isPlaying) return;

        Apply();
    }

    private void Apply()
    {
        if (!previewInEditor)
            return;

        if (_authoring == null || visual == null)
            return;

        float range = Mathf.Max(0.01f, _authoring.range);
        float angle = _authoring.coneAngle;

        bool circle = angle >= 359.9f;

        Vector3 localScale;

        if (circle)
        {
            float d = 2f * range;
            localScale = new Vector3(d, d, 1f);
        }
        else
        {
            float halfRad = Mathf.Deg2Rad * (angle * 0.5f);
            float width = 2f * range * Mathf.Tan(halfRad);
            localScale = new Vector3(width, range, 1f);
        }

        visual.localScale = localScale;

        // per-instance shader params
        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);

            if (_renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty(isCircleProperty))
                _mpb.SetFloat(isCircleProperty, circle ? 1f : 0f);

            // uncomment if authoring has opacity field:
            // if (_renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty(opacityProperty))
            //     _mpb.SetFloat(opacityProperty, Mathf.Clamp01(_authoring.opacity));

            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
