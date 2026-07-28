using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace SoftEngine.Gpu;

/// <summary>
/// An OpenGL context and the device behind it.
///
/// <para>
/// The window it is built on is one pixel across and never shown. Rendering goes to an
/// off-screen framebuffer and comes back as pixels, so nothing here needs a surface anyone
/// can see — but every desktop OpenGL implementation still insists on a window to hang a
/// context off, and creating an invisible one is the portable way to ask for a context
/// without also asking for a place to put it. That is also what lets the same backend serve
/// the WinForms viewer, which presents through a bitmap it already owns, and the
/// command-line renderer, which has no window at all.
/// </para>
///
/// <para>
/// A context belongs to the thread that made it current. Everything on <see cref="GpuRenderer"/>
/// therefore has to be called from the thread that created the context — the UI thread in the
/// viewer, the main thread in the CLI — and <see cref="MakeCurrent"/> exists for the case
/// where something else has bound a context in between.
/// </para>
/// </summary>
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

    /// <summary>The device the context turned out to be running on.</summary>
    public GpuAdapter Adapter { get; }

    /// <summary>
    /// Creates a context, or explains why it could not. <paramref name="error"/> is null on
    /// success and a message fit to show a user on failure — a missing driver and a machine
    /// with no display are both ordinary situations here, not bugs.
    /// </summary>
    /// <param name="requireHardware">
    /// When true (the default) a context served by a CPU implementation of OpenGL is rejected
    /// rather than returned. Choosing "GPU" and getting a software rasterizer behind a driver
    /// is strictly worse than choosing "CPU" and getting this engine's own, so the honest
    /// answer is that no GPU is available.
    /// </param>
    public static bool TryCreate(out GpuContext? context, out string? error, bool requireHardware = true)
    {
        context = null;
        error = null;

        IWindow? window = null;

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

                // 3.3 core is the floor for everything this backend uses — framebuffer
                // objects, floating-point colour targets, texture arrays, timer queries —
                // and is old enough that asking for it excludes nothing that could run the
                // shaders anyway.
                API = new GraphicsAPI(
                    ContextAPI.OpenGL,
                    ContextProfile.Core,
                    ContextFlags.ForwardCompatible,
                    new APIVersion(3, 3)),
            };

            window = Window.Create(options);
            window.Initialize();

            var gl = GL.GetApi(window);

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

                gl.Dispose();
                window.Dispose();
                return false;
            }

            context = new GpuContext(window, gl, adapter);
            return true;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
            PlatformNotSupportedException or InvalidOperationException or NotSupportedException)
        {
            // No driver, no display, no windowing backend for this platform. All of them mean
            // the same thing to a caller: there is no GPU to render on here.
            error = $"No OpenGL context could be created: {exception.Message}";

            try
            {
                window?.Dispose();
            }
            catch (Exception disposeFailure) when (disposeFailure is InvalidOperationException or NotSupportedException)
            {
                // Tearing down a window that never finished initializing is best-effort.
            }

            return false;
        }
    }

    /// <summary>Binds this context to the calling thread.</summary>
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
