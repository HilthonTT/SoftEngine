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

    #region Frame history

    private readonly List<FrameCapture> _frames = [];

    /// <summary>
    /// How many finished frames to keep, newest last, or 0 to keep none.
    ///
    /// <para>
    /// Off by default and separately from <see cref="CaptureEvents"/>, because it is the one
    /// piece of instrumentation here that genuinely allocates. Recording the event list is a
    /// write into a buffer that is reused for ever; keeping a frame means copying that buffer,
    /// and a busy scene emits thousands of events per frame. So the cost is opt-in and bounded
    /// by a number the caller chose, rather than being paid quietly by everyone.
    /// </para>
    /// </summary>
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

    /// <summary>The frames kept so far, oldest first. Empty unless <see cref="HistoryCapacity"/> is set.</summary>
    public IReadOnlyList<FrameCapture> Frames => _frames;

    /// <summary>Raised after a frame is added to the history, so a front-end can follow it.</summary>
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

    /// <summary>
    /// Files the frame that has just finished. Called by the renderer once the event list is
    /// complete and the stats have stopped moving — anything earlier would capture a frame that
    /// is still being written.
    /// </summary>
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
