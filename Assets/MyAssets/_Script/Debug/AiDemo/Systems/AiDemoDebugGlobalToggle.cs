using UnityEngine;

// global on/off for demo UI and debug overlays (F2)
public static class AiDemoDebugGlobalToggle
{
    public static bool Visible { get; private set; } = true;

    private static int lastToggleFrame = -1;

    public static void SetVisible(bool visible)
    {
        Visible = visible;
    }

    public static void Update(KeyCode toggleKey, bool inputEnabled)
    {
        if (!inputEnabled) { return; }
        if (!Input.GetKeyDown(toggleKey)) { return; }

        int frame = Time.frameCount;
        if (lastToggleFrame == frame) { return; }

        lastToggleFrame = frame;
        Visible = !Visible;
    }
}
