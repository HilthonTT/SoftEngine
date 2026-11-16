using SoftEngine.Gpu;
using SoftEngine.WinForms.Dialogs;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private (bool Sidebar, bool Timeline, bool PixelHistory, bool ObjectTable, bool EventList)? _beforeFocus;

    private string _workspace = "Layout";

    /// <summary>The engine the Rendering workspace switched away from, to hand back on leaving it.</summary>
    private RenderBackend? _backendBeforeRendering;

    private void InitializeWorkspace()
    {
        InitializeWorkspaces();
        InitializeAreaToggles();
        InitializeRecentFiles();
        InitializeFileDrop();
        InitializeHelpMenu();

        RestoreWindowPlacement();
    }

    #region Workspaces

    private void InitializeWorkspaces()
    {
        workspaceTabs.SelectedChanged += (s, e) => ApplyWorkspace(workspaceTabs.Selected);

        mnuWindow.DropDownOpening += (s, e) =>
        {
            foreach (var item in mnuWindow.DropDownItems.OfType<ToolStripMenuItem>())
            {
                if (item.Tag is string name)
                {
                    item.Checked = name == _workspace;
                }
            }
        };
    }

    /// <summary>
    /// A workspace is this one window arranged for a job. Layout is the plain editor; Shading
    /// previews materials; Rendering hands the frame to the path tracer; Debugging opens the
    /// graphics debugger's panels around the viewport.
    /// </summary>
    private void ApplyWorkspace(string name)
    {
        if (mnuFocusViewport.Checked)
        {
            mnuFocusViewport.Checked = false;
        }

        var leaving = _workspace;
        _workspace = name;

        if (leaving == "Rendering" && name != "Rendering" && _backendBeforeRendering is { } previous)
        {
            _backendBeforeRendering = null;
            SelectBackend(previous, remember: false);
        }

        var debugging = name == "Debugging";
        mnuPixelHistory.Checked = debugging;
        mnuObjectTable.Checked = debugging;
        mnuEventList.Checked = debugging;

        switch (name)
        {
            case "Shading":
                SetShadingMode(ShadingMode.MaterialPreview);
                properties.Select("Material");
                break;

            case "Rendering":
                if (panel3D1.Backend != RenderBackend.Trace)
                {
                    _backendBeforeRendering = panel3D1.Backend;
                    SelectBackend(RenderBackend.Trace, remember: false);
                }

                properties.Select("Render");
                break;

            case "Debugging":
                properties.Select("Debug");
                break;

            default:
                properties.Select(panel3D1.Picked is not null ? "Object" : "Render");
                break;
        }

        panel3D1.Focus();
    }

    #endregion

    #region Area toggles

    private void InitializeAreaToggles()
    {
        mnuFocusViewport.CheckedChanged += (s, e) => ApplyFocusMode(mnuFocusViewport.Checked);

        mnuToggleSidebar.CheckedChanged += (s, e) => splitMain.Panel2Collapsed = !mnuToggleSidebar.Checked;
        mnuToggleToolShelf.CheckedChanged += (s, e) =>
        {
            toolShelf.Visible = mnuToggleToolShelf.Checked;
            panel3D1.OverlayInset = toolShelf.Visible ? toolShelf.Right + 10 : 10;
            panel3D1.Invalidate();
        };

        panel3D1.OverlayInset = toolShelf.Right + 10;
    }

    /// <summary>Gives the whole window to the viewport, and puts every area back on the second press.</summary>
    private void ApplyFocusMode(bool focused)
    {
        if (focused)
        {
            _beforeFocus ??= (
                Sidebar: !splitMain.Panel2Collapsed,
                Timeline: !splitTimeline.Panel2Collapsed,
                PixelHistory: mnuPixelHistory.Checked,
                ObjectTable: mnuObjectTable.Checked,
                EventList: mnuEventList.Checked);

            splitMain.Panel2Collapsed = true;
            splitTimeline.Panel2Collapsed = true;
            mnuPixelHistory.Checked = false;
            mnuObjectTable.Checked = false;
            mnuEventList.Checked = false;
        }
        else if (_beforeFocus is { } previous)
        {
            splitMain.Panel2Collapsed = !previous.Sidebar;
            splitTimeline.Panel2Collapsed = !previous.Timeline;
            mnuPixelHistory.Checked = previous.PixelHistory;
            mnuObjectTable.Checked = previous.ObjectTable;
            mnuEventList.Checked = previous.EventList;

            _beforeFocus = null;
        }

        mnuToggleSidebar.Checked = !splitMain.Panel2Collapsed;

        panel3D1.Focus();
    }

    #endregion

    #region Recent files

    private void InitializeRecentFiles()
    {
        mnuClearRecent.Click += (s, e) =>
        {
            _settings.RecentFiles.Clear();
            _settings.Save();
            RebuildRecentMenu();
        };

        RebuildRecentMenu();
    }

    private void RememberRecentFile(string path)
    {
        if (!_settings.RememberRecentFile(path))
        {
            return;
        }

        _settings.Save();
        RebuildRecentMenu();
    }

    private void RebuildRecentMenu()
    {
        mnuOpenRecent.DropDownItems.Remove(mnuClearRecent);

        foreach (var stale in mnuOpenRecent.DropDownItems.Cast<ToolStripItem>().ToArray())
        {
            stale.Dispose();
        }

        mnuOpenRecent.DropDownItems.Clear();

        foreach (var (path, index) in _settings.RecentFiles.Select((path, index) => (path, index)))
        {
            var exists = SafeFileExists(path);

            var item = new ToolStripMenuItem
            {
                Text = $"&{(index + 1) % 10}  {Path.GetFileName(path).Replace("&", "&&", StringComparison.Ordinal)}",
                ToolTipText = exists ? path : $"{path}\n\nThis file is no longer there.",
                Enabled = exists,
                Tag = path,
            };

            item.Click += async (s, e) => await OpenRecentAsync(path);

            mnuOpenRecent.DropDownItems.Add(item);
        }

        if (_settings.RecentFiles.Count > 0)
        {
            mnuOpenRecent.DropDownItems.Add(new ToolStripSeparator());
        }

        mnuOpenRecent.DropDownItems.Add(mnuClearRecent);
        mnuClearRecent.Enabled = _settings.RecentFiles.Count > 0;
        mnuOpenRecent.Enabled = _settings.RecentFiles.Count > 0;
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private async Task OpenRecentAsync(string path)
    {
        if (!SafeFileExists(path))
        {
            MessageBox.Show(this, $"This file is no longer there:\n{path}", "Open recent",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await OpenPathAsync(path);
    }

    #endregion

    #region Dropped files

    private void InitializeFileDrop()
    {
        foreach (var target in new Control[] { this, panel3D1 })
        {
            target.AllowDrop = true;
            target.DragEnter += OnFileDragEnter;
            target.DragDrop += OnFileDragDrop;
        }
    }

    private void OnFileDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = DroppedPath(e) is not null && !_loading
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        if (DroppedPath(e) is not { } path)
        {
            return;
        }

        Activate();

        await OpenPathAsync(path);
    }

    private static string? DroppedPath(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return null;
        }

        return paths.FirstOrDefault(path => KindOf(path) != FileKind.Unknown);
    }

    #endregion

    #region Opening by path

    private enum FileKind
    {
        Unknown,
        Model,
        Scene,
        Panorama,
    }

    private static FileKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".obj" or ".dae" or ".gltf" or ".glb" => FileKind.Model,
        ".json" => FileKind.Scene,
        ".hdr" or ".pic" => FileKind.Panorama,
        ".png" or ".jpg" or ".jpeg" or ".bmp" => FileKind.Panorama,
        _ => FileKind.Unknown,
    };

    private async Task OpenPathAsync(string path)
    {
        switch (KindOf(path))
        {
            case FileKind.Model:
                await PrepareWorldFromFileAsync(path);
                break;

            case FileKind.Scene:
                await LoadSceneAsync(path);
                break;

            case FileKind.Panorama:
                await LoadPanoramaAsync(path, announceFailure: true);
                RememberRecentFile(path);
                break;

            default:
                MessageBox.Show(this,
                    $"Nothing here reads a {Path.GetExtension(path)} file.\n\n" +
                    "Models: .obj, .dae, .gltf, .glb\n" +
                    "Scenes: .scene.json\n" +
                    "Panoramas: .hdr, .pic, .png, .jpg, .bmp",
                    "Open", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
        }
    }

    #endregion

    #region Help

    private void InitializeHelpMenu()
    {
        mnuShortcuts.Click += (s, e) =>
        {
            using var dialog = new ShortcutsDialog();
            dialog.ShowDialog(this);
        };

        mnuProjectPage.Click += (s, e) => AboutDialog.OpenProjectPage();

        mnuAbout.Click += (s, e) =>
        {
            using var dialog = new AboutDialog(panel3D1.BackendDescription);
            dialog.ShowDialog(this);
        };
    }

    #endregion

    #region Persistence

    private void RestoreWindowPlacement()
    {
        if (_settings.Window is not { Width: > 0, Height: > 0 } placement)
        {
            StartPosition = FormStartPosition.CenterScreen;
            return;
        }

        var bounds = new Rectangle(placement.X, placement.Y, placement.Width, placement.Height);

        if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;

        if (placement.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        ApplyDefaultSplits();

        if (_settings.Workspace is not { } workspace)
        {
            return;
        }

        if (workspace.SidebarColumnWidth is { } column)
        {
            SetSplitter(splitMain, splitMain.Width - column);
        }

        SetSplitter(splitSidebar, workspace.OutlinerHeight);

        if (workspace.TimelineHeight is { } timelineHeight)
        {
            SetSplitter(splitTimeline, splitTimeline.Height - timelineHeight);
        }

        SetSplitter(splitLeft, workspace.PixelHistoryWidth);

        if (workspace.EventListWidth is { } eventList)
        {
            SetSplitter(splitRight, splitRight.Width - eventList);
        }

        if (workspace.ObjectTableHeight is { } objectTable)
        {
            SetSplitter(splitCenter, splitCenter.Height - objectTable);
        }

        if (workspace.ShowStatsOverlay is { } stats)
        {
            mnuStatsOverlay.Checked = stats;
        }

        if (workspace.ShowSidebar is { } sidebar)
        {
            mnuToggleSidebar.Checked = sidebar;
        }

        if (workspace.ShowToolShelf is { } shelf)
        {
            mnuToggleToolShelf.Checked = shelf;
        }

        // Rendering starts the path tracer, which is not what a window should do on opening.
        if (workspace.ActiveWorkspace is { } active && active != "Rendering" && Workspaces.Contains(active))
        {
            workspaceTabs.Select(active);
        }

        if (workspace.PropertiesTab is { } tab && properties.Tabs.Contains(tab))
        {
            properties.Select(tab);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

        if (bounds is { Width: > 0, Height: > 0 })
        {
            _settings.Window = new WindowPlacement
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == FormWindowState.Maximized,
            };
        }

        var sidebar = _beforeFocus?.Sidebar ?? !splitMain.Panel2Collapsed;

        _settings.Workspace = new WorkspaceLayout
        {
            ActiveWorkspace = _workspace,
            ShowStatsOverlay = mnuStatsOverlay.Checked,
            ShowSidebar = sidebar,
            ShowToolShelf = mnuToggleToolShelf.Checked,
            PropertiesTab = properties.SelectedTab,

            SidebarColumnWidth = splitMain.Panel2Collapsed ? null : splitMain.Width - splitMain.SplitterDistance,
            OutlinerHeight = splitSidebar.SplitterDistance,
            TimelineHeight = splitTimeline.Panel2Collapsed ? null : splitTimeline.Height - splitTimeline.SplitterDistance,
            PixelHistoryWidth = splitLeft.Panel1Collapsed ? null : splitLeft.SplitterDistance,
            EventListWidth = splitRight.Panel2Collapsed ? null : splitRight.Width - splitRight.SplitterDistance,
            ObjectTableHeight = splitCenter.Panel2Collapsed ? null : splitCenter.Height - splitCenter.SplitterDistance,
        };

        _settings.Save();
    }

    #endregion
}
