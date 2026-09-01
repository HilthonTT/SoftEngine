using SoftEngine.WinForms.Controls;
using SoftEngine.WinForms.Dialogs;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private SidebarSection? _displaySection;
    private SidebarSection? _shadingSection;
    private SidebarSection? _postSection;

    private (bool Sidebar, bool PixelHistory, bool ObjectTable, bool EventList)? _beforeFocus;

    private void InitializeWorkspace()
    {
        InitializeSidebarSections();
        InitializeLayoutMenu();
        InitializeRecentFiles();
        InitializeFileDrop();
        InitializeHelpMenu();

        RestoreWindowPlacement();
    }

    #region Sidebar sections

    private void InitializeSidebarSections()
    {
        _displaySection = new SidebarSection(lblDisplayHeader, flpDisplay, mnuSectionDisplay);
        _shadingSection = new SidebarSection(lblShadingHeader, flpShading, mnuSectionShading);
        _postSection = new SidebarSection(lblPostHeader, flpPost, mnuSectionPost);
    }

    #endregion

    #region Layout

    private void InitializeLayoutMenu()
    {
        mnuLayoutViewer.Click += (s, e) => ApplyLayout(panels: false);
        mnuLayoutDebugger.Click += (s, e) => ApplyLayout(panels: true);
        mnuFocusViewport.CheckedChanged += (s, e) => ApplyFocusMode(mnuFocusViewport.Checked);

        mnuView.DropDownOpening += (s, e) => UpdateLayoutMenu();
    }

    private void ApplyLayout(bool panels)
    {
        if (mnuFocusViewport.Checked)
        {
            mnuFocusViewport.Checked = false;
        }

        mnuPixelHistory.Checked = panels;
        mnuObjectTable.Checked = panels;
        mnuEventList.Checked = panels;

        UpdateLayoutMenu();
    }

    private void UpdateLayoutMenu()
    {
        var all = mnuPixelHistory.Checked && mnuObjectTable.Checked && mnuEventList.Checked;
        var none = !mnuPixelHistory.Checked && !mnuObjectTable.Checked && !mnuEventList.Checked;

        mnuLayoutViewer.Checked = none && !mnuFocusViewport.Checked;
        mnuLayoutDebugger.Checked = all && !mnuFocusViewport.Checked;
    }

    private void ApplyFocusMode(bool focused)
    {
        if (focused)
        {
            if (_beforeFocus is null)
            {
                _beforeFocus = (
                    Sidebar: !splitMain.Panel1Collapsed,
                    PixelHistory: mnuPixelHistory.Checked,
                    ObjectTable: mnuObjectTable.Checked,
                    EventList: mnuEventList.Checked);
            }

            splitMain.Panel1Collapsed = true;
            mnuPixelHistory.Checked = false;
            mnuObjectTable.Checked = false;
            mnuEventList.Checked = false;
        }
        else if (_beforeFocus is { } previous)
        {
            splitMain.Panel1Collapsed = !previous.Sidebar;
            mnuPixelHistory.Checked = previous.PixelHistory;
            mnuObjectTable.Checked = previous.ObjectTable;
            mnuEventList.Checked = previous.EventList;

            _beforeFocus = null;
        }

        UpdateLayoutMenu();

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

        if (_settings.Workspace is not { } workspace)
        {
            UpdateLayoutMenu();
            return;
        }

        SetSplitterDistance(splitMain, workspace.SidebarWidth);
        SetSplitterDistance(splitLeft, workspace.SidebarHeight);
        SetSplitterDistance(splitRight, workspace.ViewportWidth);
        SetSplitterDistance(splitCenter, workspace.ViewportHeight);

        if (workspace.ShowPixelHistory is { } pixelHistory)
        {
            mnuPixelHistory.Checked = pixelHistory;
        }

        if (workspace.ShowObjectTable is { } objectTable)
        {
            mnuObjectTable.Checked = objectTable;
        }

        if (workspace.ShowEventList is { } eventList)
        {
            mnuEventList.Checked = eventList;
        }

        if (workspace.ShowStatsOverlay is { } stats)
        {
            mnuStatsOverlay.Checked = stats;
        }

        if (workspace.DisplayExpanded is { } display && _displaySection is { } displaySection)
        {
            displaySection.Expanded = display;
        }

        if (workspace.ShadingExpanded is { } shading && _shadingSection is { } shadingSection)
        {
            shadingSection.Expanded = shading;
        }

        if (workspace.PostExpanded is { } post && _postSection is { } postSection)
        {
            postSection.Expanded = post;
        }

        UpdateLayoutMenu();
    }

    private static void SetSplitterDistance(SplitContainer split, int? distance)
    {
        if (distance is not { } value)
        {
            return;
        }

        var total = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var lowest = split.Panel1MinSize;
        var highest = total - split.Panel2MinSize - split.SplitterWidth;

        if (value < lowest || value > highest)
        {
            return;
        }

        split.SplitterDistance = value;
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

        var panels = _beforeFocus ?? (
            Sidebar: !splitMain.Panel1Collapsed,
            PixelHistory: mnuPixelHistory.Checked,
            ObjectTable: mnuObjectTable.Checked,
            EventList: mnuEventList.Checked);

        _settings.Workspace = new WorkspaceLayout
        {
            ShowPixelHistory = panels.PixelHistory,
            ShowObjectTable = panels.ObjectTable,
            ShowEventList = panels.EventList,
            ShowStatsOverlay = mnuStatsOverlay.Checked,

            SidebarWidth = splitMain.SplitterDistance,
            SidebarHeight = splitLeft.SplitterDistance,
            ViewportWidth = splitRight.SplitterDistance,
            ViewportHeight = splitCenter.SplitterDistance,

            DisplayExpanded = _displaySection?.Expanded,
            ShadingExpanded = _shadingSection?.Expanded,
            PostExpanded = _postSection?.Expanded,
        };

        _settings.Save();
    }

    #endregion
}
