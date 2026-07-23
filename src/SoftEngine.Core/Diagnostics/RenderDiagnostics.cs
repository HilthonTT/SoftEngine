namespace SoftEngine.Core.Diagnostics;

/// <summary>
/// The debugger-facing side of the renderer: the event list for the frame just rendered,
/// and an optional single-pixel probe that records every write attempt at one pixel.
/// Both are off until a front-end turns them on.
/// </summary>
public sealed class RenderDiagnostics
{
    public GraphicsEventLog Events { get; } = new();

    /// <summary>Records the graphics event list each frame. Off by default.</summary>
    public bool CaptureEvents
    {
        get => Events.IsEnabled;
        set => Events.IsEnabled = value;
    }

    public int ProbeX { get; private set; } = -1;

    public int ProbeY { get; private set; } = -1;

    public bool IsProbing => ProbeX >= 0 && ProbeY >= 0;

    /// <summary>The history captured for the probed pixel on the last rendered frame.</summary>
    public PixelHistory? PixelHistory { get; internal set; }

    /// <summary>Frames rendered since this renderer was created; the event list's frame number.</summary>
    public long FrameNumber { get; internal set; }

    public void SetProbe(int x, int y)
    {
        ProbeX = x;
        ProbeY = y;
    }

    public void ClearProbe()
    {
        ProbeX = -1;
        ProbeY = -1;
        PixelHistory = null;
    }
}
