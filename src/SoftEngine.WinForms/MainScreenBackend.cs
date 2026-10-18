using SoftEngine.Gpu;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private void InitializeBackendMenu()
    {
        mnuRenderCpu.Click += (s, e) => SelectBackend(RenderBackend.Cpu);
        mnuRenderGpu.Click += (s, e) => SelectBackend(RenderBackend.Gpu);
        mnuRenderTrace.Click += (s, e) => SelectBackend(RenderBackend.Trace);

        panel3D1.BackendChanged += (s, e) => UpdateBackendMenu();

        InitializeAdapterMenu();

        panel3D1.FrameRendered += (s, e) =>
        {
            if (panel3D1.Backend == RenderBackend.Trace)
            {
                lblBackendStatus.Text = panel3D1.BackendDescription;
            }
        };

        RestoreAdapterPreference();

        RestoreBackend();

        UpdateBackendMenu();
    }

    private sealed record AdapterChoice(GpuPreference Preference, ToolStripMenuItem Item);

    private readonly List<AdapterChoice> _adapterChoices = [];

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

    private void RestoreAdapterPreference()
    {
        if (_adapterChoices.Count == 0 ||
            _settings.GpuPreference == GpuPreference.Automatic ||
            _settings.GpuPreference == GpuPreferences.Current)
        {
            UpdateAdapterMenu();
            return;
        }

        GpuPreferences.TryApply(_settings.GpuPreference, out _);

        UpdateAdapterMenu();
    }

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

    private void RestoreBackend()
    {
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

        lblBackendStatus.ToolTipText = backend switch
        {
            RenderBackend.Gpu when panel3D1.Adapter is { } adapter =>
                $"{adapter.Vendor} · {adapter.Renderer}\nOpenGL {adapter.Version}",
            RenderBackend.Trace =>
                "Light traced through the scene on the CPU, refining for as long as nothing moves.",
            _ => "Every triangle rasterized on the CPU by this engine's own scanline filler.",
        };

        if (panel3D1.BackendFallback is { } fallback)
        {
            lblBackendStatus.ToolTipText = $"{fallback}\n\n{lblBackendStatus.ToolTipText}";
        }
    }

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
