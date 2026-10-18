namespace SoftEngine.Gpu;

public static class GpuAvailability
{
    private static readonly Lock Gate = new();

    private static bool _probed;
    private static GpuAdapter? _adapter;
    private static string? _error;

    public static GpuAdapter? Probe()
    {
        Probe(out var adapter, out _);
        return adapter;
    }

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

    public static bool IsAvailable => Probe() is not null;
}
