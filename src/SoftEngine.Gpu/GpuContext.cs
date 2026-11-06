using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace SoftEngine.Gpu;

public sealed class GpuContext : IDisposable
{
    private readonly IWindow _window;
    private bool _disposed;

    private GpuContext(IWindow window, GL gl, GpuAdapter adapter)
    {
        _window = window;
        Gl = gl;
        Adapter = adapter;
    }

    public GL Gl { get; }

    public GpuAdapter Adapter { get; }

    private static volatile bool _hasCreatedContext;

    public static bool HasCreatedContext => _hasCreatedContext;

    public static bool TryCreate(out GpuContext? context, out string? error, bool requireHardware = true)
    {
        context = null;
        error = null;

        IWindow? window = null;
        GL? gl = null;

        try
        {
            var options = WindowOptions.Default with
            {
                Size = new Vector2D<int>(1, 1),
                IsVisible = false,
                Title = "SoftEngine",
                ShouldSwapAutomatically = false,
                IsEventDriven = false,
                VSync = false,

                API = new GraphicsAPI(
                    ContextAPI.OpenGL,
                    ContextProfile.Core,
                    ContextFlags.ForwardCompatible,
                    new APIVersion(3, 3)),
            };

            window = Window.Create(options);
            window.Initialize();

            _hasCreatedContext = true;

            gl = GL.GetApi(window);

            var adapter = new GpuAdapter(
                gl.GetStringS(StringName.Vendor),
                gl.GetStringS(StringName.Renderer),
                gl.GetStringS(StringName.Version),
                gl.GetStringS(StringName.ShadingLanguageVersion));

            if (requireHardware && !adapter.IsHardwareAccelerated)
            {
                error =
                    $"OpenGL is being served by a software rasterizer ({adapter.Renderer}), not a " +
                    "graphics adapter. Install a graphics driver, or render on the CPU — which on " +
                    "this machine will be the faster of the two.";

                return false;
            }

            context = new GpuContext(window, gl, adapter);
            return true;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
            PlatformNotSupportedException or InvalidOperationException or NotSupportedException)
        {
            error = $"No OpenGL context could be created: {exception.Message}";
            return false;
        }
        finally
        {
            // Anything short of handing both to a context — a software adapter, a failed
            // query, an exception the filter above lets through — must release them here.
            if (context is null)
            {
                Release(gl, window);
            }
        }
    }

    private static void Release(GL? gl, IWindow? window)
    {
        try
        {
            gl?.Dispose();
            window?.Dispose();
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException)
        {
            // Tearing down a context that never fully came up can fail on some drivers;
            // the caller is already reporting the original failure, which is the useful one.
        }
    }

    public void MakeCurrent() => _window.MakeCurrent();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Gl.Dispose();
        _window.Dispose();
    }
}
