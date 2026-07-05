using System.Collections.Generic;
using UnityEngine;

// draws lines in game view through GL.Lines
// F2 toggle shared with AiDemoShipDebugHud
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(Camera))]
public class AiDemoLineRenderer : MonoBehaviour
{
    private struct LineSegment
    {
        public Vector3 from;
        public Vector3 to;
        public Color color;
    }

    private static AiDemoLineRenderer instance;
    private static readonly List<LineSegment> pendingLines = new List<LineSegment>(2048);
    private static readonly List<LineSegment> forcedLines = new List<LineSegment>(512);

    [Header("Global Toggle")]
    [SerializeField] private bool useGlobalToggle = true;
    [SerializeField] private KeyCode globalToggleKey = KeyCode.F2;

    private Material lineMaterial;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        EnsureMaterial();
    }

    private void Update()
    {
        AiDemoDebugGlobalToggle.Update(globalToggleKey, useGlobalToggle);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void EnsureMaterial()
    {
        if (lineMaterial != null) { return; }

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        lineMaterial = new Material(shader);
        lineMaterial.hideFlags = HideFlags.HideAndDontSave;
        lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
        lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    private void OnPostRender()
    {
        DrawAndClear();
    }

    private void OnEnable()
    {
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnEndCameraRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != GetComponent<Camera>()) { return; }
        DrawAndClear();
    }

    private void DrawAndClear()
    {
        bool drawDebug = AiDemoDebugGlobalToggle.Visible;
        bool hasForcedLines = forcedLines.Count > 0;

        if (!drawDebug && !hasForcedLines)
        {
            pendingLines.Clear();
            return;
        }

        if (drawDebug && pendingLines.Count == 0 && !hasForcedLines) { return; }
        EnsureMaterial();

        lineMaterial.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);
        GL.Begin(GL.LINES);

        if (drawDebug)
        {
            for (int i = 0; i < pendingLines.Count; i++)
            {
                LineSegment seg = pendingLines[i];
                GL.Color(seg.color);
                GL.Vertex(seg.from);
                GL.Vertex(seg.to);
            }
        }

        for (int i = 0; i < forcedLines.Count; i++)
        {
            LineSegment seg = forcedLines[i];
            GL.Color(seg.color);
            GL.Vertex(seg.from);
            GL.Vertex(seg.to);
        }

        GL.End();
        GL.PopMatrix();

        pendingLines.Clear();
        forcedLines.Clear();
    }

    public static bool IsAvailable => instance != null && AiDemoDebugGlobalToggle.Visible;

    public static bool CanDrawAlways => instance != null;

    public static void AddLine(Vector3 from, Vector3 to, Color color)
    {
        if (instance == null || !AiDemoDebugGlobalToggle.Visible) { return; }
        pendingLines.Add(new LineSegment { from = from, to = to, color = color });
    }

    public static void AddLineAlways(Vector3 from, Vector3 to, Color color)
    {
        if (instance == null) { return; }
        forcedLines.Add(new LineSegment { from = from, to = to, color = color });
    }

    public static void AddRing(Vector3 center, float radius, Color color, int segments = 32)
    {
        if (instance == null || !AiDemoDebugGlobalToggle.Visible || segments < 3) { return; }

        float step = (Mathf.PI * 2f) / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = step * i;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            pendingLines.Add(new LineSegment { from = prev, to = next, color = color });
            prev = next;
        }
    }

    public static void AddRingAlways(Vector3 center, float radius, Color color, int segments = 32)
    {
        if (instance == null || segments < 3) { return; }

        float step = (Mathf.PI * 2f) / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = step * i;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            forcedLines.Add(new LineSegment { from = prev, to = next, color = color });
            prev = next;
        }
    }

    public static void AddCone(Vector3 origin, Vector3 forward, float range, float halfAngleDeg, Color color, int arcSegments = 16)
    {
        if (instance == null || !AiDemoDebugGlobalToggle.Visible) { return; }

        Vector3 fwd = forward.sqrMagnitude < 0.0001f ? Vector3.right : forward.normalized;

        Quaternion left = Quaternion.AngleAxis(halfAngleDeg, Vector3.forward);
        Quaternion right = Quaternion.AngleAxis(-halfAngleDeg, Vector3.forward);

        Vector3 leftDir = left * fwd;
        Vector3 rightDir = right * fwd;

        Vector3 leftPoint = origin + leftDir * range;
        Vector3 rightPoint = origin + rightDir * range;

        pendingLines.Add(new LineSegment { from = origin, to = leftPoint, color = color });
        pendingLines.Add(new LineSegment { from = origin, to = rightPoint, color = color });

        if (arcSegments < 2) { arcSegments = 2; }
        Vector3 prev = leftPoint;
        for (int i = 1; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            float angle = Mathf.Lerp(halfAngleDeg, -halfAngleDeg, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.forward) * fwd;
            Vector3 next = origin + dir * range;
            pendingLines.Add(new LineSegment { from = prev, to = next, color = color });
            prev = next;
        }
    }

    public static void AddCross(Vector3 center, float size, Color color)
    {
        if (instance == null || !AiDemoDebugGlobalToggle.Visible) { return; }
        pendingLines.Add(new LineSegment { from = center + Vector3.right * size, to = center - Vector3.right * size, color = color });
        pendingLines.Add(new LineSegment { from = center + Vector3.up * size, to = center - Vector3.up * size, color = color });
    }

    public static void AddCrossAlways(Vector3 center, float size, Color color)
    {
        if (instance == null) { return; }
        forcedLines.Add(new LineSegment { from = center + Vector3.right * size, to = center - Vector3.right * size, color = color });
        forcedLines.Add(new LineSegment { from = center + Vector3.up * size, to = center - Vector3.up * size, color = color });
    }
}
