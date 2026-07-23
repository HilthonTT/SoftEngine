namespace SoftEngine.Core.Diagnostics;

/// <summary>
/// The graphics events recorded for the current frame. Backed by a growable array that
/// is reused frame after frame, so a steady-state capture allocates nothing.
/// </summary>
public sealed class GraphicsEventLog
{
    private GraphicsEvent[] _events = new GraphicsEvent[256];
    private int _count;

    /// <summary>When false, <see cref="Add"/> does nothing and returns -1.</summary>
    public bool IsEnabled { get; set; }

    public int Count => _count;

    public GraphicsEvent this[int index] =>
        (uint)index < (uint)_count ? _events[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public ReadOnlySpan<GraphicsEvent> AsSpan() => _events.AsSpan(0, _count);

    public void Clear() => _count = 0;

    /// <summary>Appends an event and returns its index, or -1 when recording is off.</summary>
    public int Add(GraphicsEventKind kind, int objectId = -1, float a = 0f, float b = 0f, float c = 0f)
    {
        if (!IsEnabled)
        {
            return -1;
        }

        if (_count == _events.Length)
        {
            Array.Resize(ref _events, _events.Length * 2);
        }

        _events[_count] = new GraphicsEvent(kind, objectId, a, b, c);
        return _count++;
    }
}
