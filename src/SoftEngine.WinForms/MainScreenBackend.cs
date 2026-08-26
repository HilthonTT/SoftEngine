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

        InitializeAdapterMenu();

        // The tracer's sample count climbs frame by frame while it refines, and the status bar is
        // where anyone would look to see whether it is still working.
        panel3D1.FrameRendered += (s, e) =>
        {
            if (panel3D1.Backend == RenderBackend.Trace)
            {
                lblBackendStatus.Text = panel3D1.BackendDescription;
            }
        };

        // Before the backend is restored, because restoring it onto the GPU creates a context
        // and the preference is only read while there is not one yet.
        RestoreAdapterPreference();

        RestoreBackend();

        UpdateBackendMenu();
    }

    /// <summary>What one entry of the adapter submenu stands for.</summary>
    private sealed record AdapterChoice(GpuPreference Preference, ToolStripMenuItem Item);

    /// <summary>The adapter entries, empty on a machine where there is nothing to choose between.</summary>
    private readonly List<AdapterChoice> _adapterChoices = [];

    /// <summary>
    /// Adds "Graphics adapter" under View ▸ Rendered by, on a machine that has more than one
    /// adapter to be given.
    ///
    /// <para>
    /// Built here rather than in the designer because what it lists depends on the machine: the
    /// entries are named after the devices actually installed, so the choice reads as "the
    /// GeForce or the Intel" rather than as two words about power management. A desktop with one
    /// card gets no submenu at all — a control whose every setting does the same thing is worse
    /// than no control, because it invites somebody to go looking for the difference.
    /// </para>
    /// </summary>
    private void InitializeAdapterMenu()
    {
        if (!GpuPreferences.IsSupported || !GpuDevices.HasChoice)
        {
            return;
        }

        var adapters = new ToolStripMenuItem("&Graphics adapter")
        {
            Name = "mnuGpuAdapter",
            ToolTipText =
                "Which adapter the GPU backend is given. The driver hands an application the " +
                "integrated one unless it is told otherwise, which on this machine is not the fast one.",
        };

        foreach (var preference in (GpuPreference[])[GpuPreference.Automatic, GpuPreference.HighPerformance, GpuPreference.PowerSaving])
        {
            var choice = preference;

            var item = new ToolStripMenuItem(GpuPreferences.Describe(choice))
            {
                Name = $"mnuGpuAdapter{choice}",
            };

            item.Click += (s, e) => SelectAdapter(choice);

            adapters.DropDownItems.Add(item);
            _adapterChoices.Add(new AdapterChoice(choice, item));
        }

        mnuRenderedBy.DropDownItems.Add(new ToolStripSeparator());
        mnuRenderedBy.DropDownItems.Add(adapters);
    }

    /// <summary>
    /// Puts the saved preference back before anything has created a context, which is the only
    /// moment it can be put back at.
    ///
    /// <para>
    /// Written to the machine on every launch rather than only when it changes, because the
    /// setting Windows holds is keyed by the executable's path: a build that has moved, or a
    /// second copy of the viewer, has no preference recorded against it even though the person
    /// running it chose one. The saved value is the intent; the registry value is where that
    /// intent has to be repeated for the driver to see it.
    /// </para>
    ///
    /// <para>
    /// <see cref="GpuPreference.Automatic"/> is the one value not repeated, because in this file
    /// it means "never chose" rather than "chose the default". Somebody who set this application
    /// to High performance in Windows' own graphics settings and never opened this menu would
    /// otherwise have that undone on every launch by a viewer that has no opinion — and undoing
    /// a setting is not a thing to do on the strength of having no opinion. Picking Automatic
    /// from the menu is a different statement, and <see cref="SelectAdapter"/> does honour it.
    /// </para>
    /// </summary>
    private void RestoreAdapterPreference()
    {
        if (_adapterChoices.Count == 0 ||
            _settings.GpuPreference == GpuPreference.Automatic ||
            _settings.GpuPreference == GpuPreferences.Current)
        {
            UpdateAdapterMenu();
            return;
        }

        // A preference that cannot be written is not worth a dialog nobody asked for: the menu
        // will show what is actually in force, which is the honest answer either way.
        GpuPreferences.TryApply(_settings.GpuPreference, out _);

        UpdateAdapterMenu();
    }

    /// <summary>
    /// Records a new adapter choice, and says whether it is about to take effect or has to wait
    /// for a restart.
    ///
    /// <para>
    /// It is the driver's OpenGL implementation that gets loaded, and it gets loaded once. Before
    /// the viewport has ever been on the GPU there is nothing loaded, so the choice applies to the
    /// next GPU render — which is why the message is not simply "restart to apply".
    /// </para>
    /// </summary>
    private void SelectAdapter(GpuPreference preference)
    {
        if (preference == _settings.GpuPreference && preference == GpuPreferences.Current)
        {
            return;
        }

        if (!GpuPreferences.TryApply(preference, out var error))
        {
            MessageBox.Show(this, error, "Graphics adapter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateAdapterMenu();
            return;
        }

        _settings.GpuPreference = preference;
        _settings.Save();

        UpdateAdapterMenu();

        if (GpuPreferences.TakesEffectImmediately)
        {
            return;
        }

        MessageBox.Show(
            this,
            $"{GpuPreferences.Describe(preference)}.\n\n" +
            "The graphics driver is loaded once per session, so this takes effect the next time " +
            "the viewer starts.",
            "Graphics adapter",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>Ticks whichever preference the machine is actually holding, not whichever was clicked.</summary>
    private void UpdateAdapterMenu()
    {
        var current = GpuPreferences.Current;

        foreach (var (preference, item) in _adapterChoices)
        {
            item.Checked = preference == current;
        }
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
