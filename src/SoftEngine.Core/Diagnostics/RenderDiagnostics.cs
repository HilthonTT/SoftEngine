namespace SoftEngine.Core.Diagnostics;

public sealed class RenderDiagnostics
{
    public GraphicsEventLog Events { get; } = new();

    public bool CaptureEvents
    {
        get => Events.IsEnabled;
        set => Events.IsEnabled = value;
    }

    public int ProbeX { get; private set; } = -1;

    public int ProbeY { get; private set; } = -1;

    public bool IsProbing => ProbeX >= 0 && ProbeY >= 0;

    public PixelHistory? PixelHistory { get; internal set; }

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

    #region Frame history

    private readonly List<FrameCapture> _frames = [];

    public int HistoryCapacity
    {
        get => _historyCapacity;
        set
        {
            _historyCapacity = System.Math.Max(0, value);

            Trim();
        }
    }

    private int _historyCapacity;

    public IReadOnlyList<FrameCapture> Frames => _frames;

    public event EventHandler? FrameCaptured;

    public void ClearHistory()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        _frames.Clear();

        FrameCaptured?.Invoke(this, EventArgs.Empty);
    }

    internal void CaptureFrame(RenderStats stats)
    {
        if (_historyCapacity <= 0)
        {
            return;
        }

        _frames.Add(new FrameCapture(
            FrameNumber,
            Events.AsSpan().ToArray(),
            PixelHistory,
            FrameStats.Of(stats)));

        Trim();

        FrameCaptured?.Invoke(this, EventArgs.Empty);
    }

    private void Trim()
    {
        while (_frames.Count > _historyCapacity)
        {
            _frames.RemoveAt(0);
        }
    }

    #endregion
}
