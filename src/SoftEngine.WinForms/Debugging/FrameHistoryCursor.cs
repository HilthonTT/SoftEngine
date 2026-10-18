using SoftEngine.Core.Diagnostics;

namespace SoftEngine.WinForms.Debugging;

internal sealed class FrameHistoryCursor
{
    /// <summary>
    /// Which frame the panels are showing, by its own number, or -1 to follow whatever was
    /// rendered last.
    ///
    /// <para>
    /// The number rather than a position in the kept list, because the list is a window that
    /// slides: the viewport goes on rendering while a frame is pinned, and every new capture
    /// drops the oldest. An index would quietly come to mean a different frame each time that
    /// happened — the panels would creep forward through history while claiming to stand still,
    /// which is worse than either following or stopping.
    /// </para>
    /// </summary>
    public long PinnedFrameNumber { get; private set; } = -1;

    public bool IsPinned => PinnedFrameNumber >= 0;

    /// <summary>
    /// Where the pinned frame sits in the kept list, or -1 when the panels are following the
    /// renderer.
    /// </summary>
    /// <remarks>
    /// A pinned frame can age out of the window while it is being looked at. The oldest frame
    /// still kept is the closest thing to what was asked for, and the status bar names whichever
    /// frame is actually on screen — so the slip is visible rather than silent.
    /// </remarks>
    public int IndexIn(IReadOnlyList<FrameCapture> frames)
    {
        if (!IsPinned || frames.Count == 0)
        {
            return -1;
        }

        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (frames[i].FrameNumber == PinnedFrameNumber)
            {
                return i;
            }
        }

        return 0;
    }

    public FrameCapture? PinnedIn(IReadOnlyList<FrameCapture> frames)
    {
        var index = IndexIn(frames);

        return index >= 0 ? frames[index] : null;
    }

    public bool CanStepBack(IReadOnlyList<FrameCapture> frames) => frames.Count > 0 && IndexIn(frames) != 0;

    public bool CanStepForward(IReadOnlyList<FrameCapture> frames) => IndexIn(frames) >= 0;

    /// <summary>
    /// Moves the pin one frame. Stepping back from live starts at the newest kept frame, and
    /// stepping forward past it returns to following the renderer — so the two ends of the
    /// history behave the way a person expects rather than stopping dead.
    /// </summary>
    public bool Step(IReadOnlyList<FrameCapture> frames, int direction)
    {
        if (frames.Count == 0)
        {
            return false;
        }

        var index = IndexIn(frames);

        if (index < 0)
        {
            // Following the renderer. Back pins the newest frame captured; forward has nowhere
            // to go, since the newest frame is the one already on screen.
            if (direction >= 0)
            {
                return false;
            }

            Pin(frames[^1].FrameNumber);
            return true;
        }

        var next = index + direction;

        if (next >= frames.Count)
        {
            return Release();
        }

        Pin(frames[Math.Max(next, 0)].FrameNumber);
        return true;
    }

    public void Pin(long frameNumber) => PinnedFrameNumber = frameNumber;

    public bool Release()
    {
        if (!IsPinned)
        {
            return false;
        }

        PinnedFrameNumber = -1;
        return true;
    }
}
