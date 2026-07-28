namespace SoftEngine.Gpu;

/// <summary>
/// Whether this machine has a GPU worth rendering on, answered once and remembered.
///
/// The only way to find out is to create a context and read the driver's own account of
/// itself, which costs enough that a menu cannot ask per repaint — and the answer cannot
/// change while the process runs, short of a driver being swapped underneath it. The probe
/// therefore runs at most once, and its context is torn down immediately: a front-end that
/// goes on to render creates its own, and one that only wanted to know whether to offer the
/// choice is left holding nothing.
/// </summary>
public static class GpuAvailability
{
    private static readonly Lock Gate = new();

    private static bool _probed;
    private static GpuAdapter? _adapter;
    private static string? _error;

    /// <summary>
    /// The device a GPU render would run on, or null when there is none. Probes on the first
    /// call. Must be called from a thread that may create a window — the UI thread, in a
    /// windowed front-end.
    /// </summary>
    public static GpuAdapter? Probe()
    {
        Probe(out var adapter, out _);
        return adapter;
    }

    /// <summary>
    /// As <see cref="Probe()"/>, and also why there is no device when there isn't one.
    /// <paramref name="error"/> is null exactly when <paramref name="adapter"/> is not.
    /// </summary>
    public static bool Probe(out GpuAdapter? adapter, out string? error)
    {
        lock (Gate)
        {
            if (!_probed)
            {
                _probed = true;

                if (GpuContext.TryCreate(out var context, out _error))
                {
                    _adapter = context!.Adapter;
                    context.Dispose();
                }
            }

            adapter = _adapter;
            error = _error;

            return adapter is not null;
        }
    }

    /// <summary>Whether a hardware GPU backend can be created here.</summary>
    public static bool IsAvailable => Probe() is not null;
}
