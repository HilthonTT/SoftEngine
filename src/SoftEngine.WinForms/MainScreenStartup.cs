using SoftEngine.Gpu;

namespace SoftEngine.WinForms;

/// <summary>What the window does once it is on screen, as its command line asks.</summary>
public sealed partial class MainScreen
{
    private async Task StartAsync()
    {
        // Fit before anything is loaded, so a size this display cannot give fails now — not after
        // the window has spent minutes rendering frames at whatever size it could reach.
        if (_arguments.Viewport is { } viewport && !FitViewport(viewport) && _arguments.Screenshot is not null)
        {
            FailViewport(viewport);
            return;
        }

        if (_arguments.Path is { } path)
        {
            await OpenPathAsync(path);
        }
        else
        {
            await PrepareWorldAsync("skull");
        }

        if (_arguments.Workspace is { } workspace)
        {
            workspaceTabs.Select(workspace);
        }

        if (_arguments.Time is { } time && Players.Count > 0)
        {
            chkAnimate.Checked = false;
            PoseAt(time);
        }

        if (_arguments.Select is { } index && panel3D1.Scene?.World is { } world && index < world.Meshes.Count)
        {
            panel3D1.SelectMesh(world.Meshes[index]);
            properties.Select("Object");
        }

        // Loading can bring the sidebar or the debugger's panels in; size once more after it.
        if (_arguments.Viewport is { } settled && !FitViewport(settled) && _arguments.Screenshot is not null)
        {
            FailViewport(settled);
            return;
        }

        if (_arguments.Screenshot is { } output)
        {
            await SaveScreenshotAndQuitAsync(output);
        }
    }

    /// <summary>
    /// A picture of the wrong size is worse than none — whatever asked for this size would carry
    /// on as if it had it — so a screenshot run that cannot have its viewport quits with an error.
    /// </summary>
    private void FailViewport(Size target)
    {
        Console.Error.WriteLine(
            $"The viewport could not be made {target.Width}x{target.Height} on this display; " +
            $"it is {panel3D1.ClientSize.Width}x{panel3D1.ClientSize.Height}.");

        Environment.ExitCode = 1;
        Close();
    }

    /// <summary>
    /// Grows or shrinks the window by exactly what the viewport is short of or over, and says
    /// whether it got there — a display too small for the window it would take can stop it.
    /// </summary>
    private bool FitViewport(Size target)
    {
        WindowState = FormWindowState.Normal;

        // The minimum window size is there for people dragging an edge; a script that asks for
        // a small viewport means it.
        MinimumSize = Size.Empty;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = panel3D1.ClientSize;
            var delta = new Size(target.Width - current.Width, target.Height - current.Height);

            if (delta.IsEmpty)
            {
                return true;
            }

            Size += delta;
            PerformLayout();
            Application.DoEvents();
        }

        return panel3D1.ClientSize == target;
    }

    private async Task SaveScreenshotAndQuitAsync(string output)
    {
        // A single frame is the picture for most settings; temporal anti-aliasing and the path
        // tracer both refine over successive frames of an unmoving view, so they get more.
        var frames = panel3D1.Backend == RenderBackend.Trace ? 96
            : panel3D1.RendererSettings.TemporalAntiAliasing ? 24
            : 2;

        for (var i = 0; i < frames; i++)
        {
            panel3D1.Invalidate();
            panel3D1.Update();
            await Task.Yield();
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(output));

            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            if (!panel3D1.SaveScreenshot(output))
            {
                Environment.ExitCode = 1;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(exception.Message);
            Environment.ExitCode = 1;
        }

        Close();
    }
}
