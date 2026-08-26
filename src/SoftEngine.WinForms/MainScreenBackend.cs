using SoftEngine.Gpu;

namespace SoftEngine.WinForms;

/// <summary>
/// Which machine fills the viewport — this engine's own rasterizer, an OpenGL adapter, or the
/// path tracer — and the menu, status readout and saved preference that go with the choice.
///
/// <para>
/// Kept apart from <c>MainScreen.cs</c> because it is the one setting that decides whether the
/// rest of that file's wiring is doing anything at all: everything else in the viewer configures
/// a renderer, and this picks which renderer is being configured.
/// </para>
///
/// <para>
/// Named <c>MainScreenBackend.cs</c> rather than <c>MainScreen.Backend.cs</c> for the reason
/// spelled out in <c>MainScreenWorkspace.cs</c>: a dotted partial of a <see cref="Form"/> invites
/// Visual Studio to generate a <c>.resx</c> whose resource name collides with the form's own.
/// </para>
/// </summary>
public sealed partial class MainScreen
{
    /// <summary>
    /// Wires View → Rendered by, and the status readout that says which one won.
    ///
    /// <para>
    /// The GPU item is offered whether or not there is a GPU, and reports what happened
    /// afterwards rather than being greyed out beforehand. Finding out costs an OpenGL
    /// context, and a menu item that is simply missing on a machine with a graphics card in
    /// it — because a driver is out of date, say — tells you nothing about why.
    /// </para>
    /// </summary>
    private void InitializeBackendMenu()
    {
        mnuRenderCpu.Click += (s, e) => SelectBackend(RenderBackend.Cpu);
        mnuRenderGpu.Click += (s, e) => SelectBackend(RenderBackend.Gpu);
        mnuRenderTrace.Click += (s, e) => SelectBackend(RenderBackend.Trace);

        panel3D1.BackendChanged += (s, e) => UpdateBackendMenu();

        // The tracer's sample count climbs frame by frame while it refines, and the status bar is
        // where anyone would look to see whether it is still working.
        panel3D1.FrameRendered += (s, e) =>
        {
            if (panel3D1.Backend == RenderBackend.Trace)
            {
                lblBackendStatus.Text = panel3D1.BackendDescription;
            }
        };

        RestoreBackend();

        UpdateBackendMenu();
    }

    private void SelectBackend(RenderBackend backend)
    {
        if (panel3D1.Backend == backend && panel3D1.BackendFallback is null)
        {
            return;
        }

        using (new WaitCursorScope(this))
        {
            panel3D1.Backend = backend;
        }

        RememberBackend();

        if (panel3D1.BackendFallback is { } fallback)
        {
            MessageBox.Show(
                this,
                $"{fallback}\n\nThe viewport is still being rendered on the CPU.",
                "No graphics adapter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// Puts the viewport back on the backend it was left on.
    ///
    /// <para>
    /// A request that falls back says so through the status bar rather than through a dialog. A
    /// modal box in front of a window that has not appeared yet is a poor way to find out that a
    /// driver is missing, and unlike a menu click nobody has just asked a question that is waiting
    /// for an answer.
    /// </para>
    /// </summary>
    private void RestoreBackend()
    {
        // The panel is already on the CPU, and building the renderer it is already using would cost
        // a rebuild to arrive back where it started.
        if (_settings.Backend == RenderBackend.Cpu)
        {
            return;
        }

        using (new WaitCursorScope(this))
        {
            panel3D1.Backend = _settings.Backend;
        }

        RememberBackend();
    }

    /// <summary>
    /// Records the backend that <em>settled</em>, which is not always the one that was asked for.
    ///
    /// Saving the request instead would leave a machine whose graphics driver has gone missing
    /// probing for an OpenGL context on every launch, and the file claiming a backend the menu is
    /// not showing as ticked. What is remembered is what is on screen.
    /// </summary>
    private void RememberBackend()
    {
        if (_settings.Backend == panel3D1.Backend)
        {
            return;
        }

        _settings.Backend = panel3D1.Backend;
        _settings.Save();
    }

    private void UpdateBackendMenu()
    {
        var backend = panel3D1.Backend;

        mnuRenderCpu.Checked = backend == RenderBackend.Cpu;
        mnuRenderGpu.Checked = backend == RenderBackend.Gpu;
        mnuRenderTrace.Checked = backend == RenderBackend.Trace;

        lblBackendStatus.Text = panel3D1.BackendDescription;

        // The adapter's own name, which is the only way to tell an integrated part from the
        // discrete one a laptop may also have.
        lblBackendStatus.ToolTipText = backend switch
        {
            RenderBackend.Gpu when panel3D1.Adapter is { } adapter =>
                $"{adapter.Vendor} · {adapter.Renderer}\nOpenGL {adapter.Version}",
            RenderBackend.Trace =>
                "Light traced through the scene on the CPU, refining for as long as nothing moves.",
            _ => "Every triangle rasterized on the CPU by this engine's own scanline filler.",
        };

        // A request that fell back explains itself here. It is the only account a restored choice
        // gets — nobody clicked anything at startup, so there is no dialog to have answered.
        if (panel3D1.BackendFallback is { } fallback)
        {
            lblBackendStatus.ToolTipText = $"{fallback}\n\n{lblBackendStatus.ToolTipText}";
        }
    }

    /// <summary>An hourglass for as long as it is held. Building a GL context is not instant.</summary>
    private readonly struct WaitCursorScope : IDisposable
    {
        private readonly Control _control;
        private readonly Cursor _previous;

        public WaitCursorScope(Control control)
        {
            _control = control;
            _previous = control.Cursor;
            control.Cursor = Cursors.WaitCursor;
        }

        public void Dispose() => _control.Cursor = _previous;
    }
}
