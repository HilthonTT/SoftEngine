using SoftEngine.WinForms.Controls;
using SoftEngine.WinForms.Dialogs;

namespace SoftEngine.WinForms;

/// <summary>
/// Everything about the viewer as an <em>application</em> rather than as a renderer: where its
/// window is, which panels are open, what it has opened lately, and how any of that is discovered.
///
/// <para>
/// Kept apart from <c>MainScreen.cs</c> because none of it is about drawing. That file wires
/// checkboxes to the pipeline; this one is the shell around it.
/// </para>
///
/// <para>
/// Named <c>MainScreenWorkspace.cs</c> rather than <c>MainScreen.Workspace.cs</c> on purpose.
/// <c>.Designer.cs</c> is special-cased by the SDK, but any other dotted partial of a
/// <see cref="Form"/> invites Visual Studio to generate a <c>.resx</c> beside it, and that file's
/// manifest resource name collides with <c>MainScreen.resx</c> — an MSB3577 build break in a file
/// nobody added and git never sees.
/// </para>
/// </summary>
public sealed partial class MainScreen
{
    private SidebarSection? _displaySection;
    private SidebarSection? _shadingSection;
    private SidebarSection? _postSection;

    /// <summary>
    /// What the workspace looked like before F11 hid it, or null when the viewport is not
    /// focused. Restoring the panels that were open — rather than opening all of them — is the
    /// difference between a mode you can leave and one that rearranges your desk on the way out.
    /// </summary>
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

        // The presets are shortcuts for the three checkboxes below them in the same menu, so the
        // tick that says which one you are on has to follow those rather than the last click.
        mnuView.DropDownOpening += (s, e) => UpdateLayoutMenu();
    }

    /// <summary>
    /// Opens or closes all three debugger panels at once.
    ///
    /// Driving the menu items rather than the split containers keeps one path to the panels:
    /// their <c>CheckedChanged</c> handlers are what actually collapse the panes, and are also
    /// what the saved workspace is read back through.
    /// </summary>
    private void ApplyLayout(bool panels)
    {
        // Leaving focus mode by choosing a layout, rather than making somebody press F11 first to
        // find out why the menu appears to do nothing.
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

    /// <summary>
    /// Hides everything that is not the picture, and puts it all back.
    ///
    /// <para>
    /// The sidebar goes with the panels. A "focus the viewport" that left a 290-pixel column of
    /// checkboxes standing would be answering a different question than the one F11 is pressed to
    /// ask.
    /// </para>
    /// </summary>
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

        // Whatever had the keyboard may have just been hidden, and the fly controls belong to the
        // viewport anyway.
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

    /// <summary>Records a file the user opened, and writes the list straight back to disk.</summary>
    private void RememberRecentFile(string path)
    {
        if (!_settings.RememberRecentFile(path))
        {
            return;
        }

        _settings.Save();
        RebuildRecentMenu();
    }

    /// <summary>
    /// Rebuilds the recent list.
    ///
    /// <para>
    /// A file that has since been moved or deleted stays on the list, greyed out and saying so.
    /// Silently dropping it would make the list appear to forget things at random; the entry is
    /// also the only remaining record of where the file used to be.
    /// </para>
    /// </summary>
    private void RebuildRecentMenu()
    {
        // The clear item is reused rather than rebuilt, so its handler is only ever wired once —
        // which is also why it has to be taken out before the rest are disposed rather than after.
        // Clear() detaches items without disposing them, so the entries built below would
        // otherwise accumulate a menu's worth of controls on every open.
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
                // &-prefixed ordinal, so the first nine are reachable by one key once the menu is
                // open. The path is escaped because a folder with an ampersand in it would
                // otherwise swallow the next character into an accelerator.
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

    /// <summary>
    /// <see cref="File.Exists(string?)"/> answers false for a malformed path, but a path on a
    /// disconnected network share can still throw on the way to finding out.
    /// </summary>
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

    /// <summary>
    /// Lets a model, a scene or a panorama be opened by dropping it on the window.
    ///
    /// <para>
    /// Both the form and the viewport accept the drop. Drag-and-drop targets whichever control is
    /// under the pointer, and the viewport covers most of the window — registering only the form
    /// would mean the drop failing everywhere it most obviously ought to work.
    /// </para>
    /// </summary>
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

        // The drop leaves the source application waiting on this handler, and loading a world is
        // seconds of work. Activating first also puts the window in front, which is what somebody
        // who just dropped something on it is expecting.
        Activate();

        await OpenPathAsync(path);
    }

    /// <summary>The first dropped file this viewer knows what to do with, or null.</summary>
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

    /// <summary>
    /// What a path is, by extension.
    ///
    /// <para>
    /// A scene is checked before anything else because <c>.scene.json</c> ends in <c>.json</c> and
    /// nothing else here claims that extension. The image formats are panoramas rather than
    /// textures because a texture has no meaning without a mesh to put it on, and a panorama is
    /// the only thing the viewer can do with an image on its own.
    /// </para>
    /// </summary>
    private static FileKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".obj" or ".dae" or ".gltf" or ".glb" => FileKind.Model,
        ".json" => FileKind.Scene,
        ".hdr" or ".pic" => FileKind.Panorama,
        ".png" or ".jpg" or ".jpeg" or ".bmp" => FileKind.Panorama,
        _ => FileKind.Unknown,
    };

    /// <summary>Opens a file as whatever its extension says it is.</summary>
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

    /// <summary>
    /// Puts the window back where it was, if it is still somewhere a person can see.
    ///
    /// <para>
    /// The bounds are checked against the screens that exist <em>now</em>. A window restored onto
    /// a monitor that has since been unplugged is not merely inconvenient — it opens somewhere
    /// with no way to drag it back, which is worse than ignoring the saved position entirely.
    /// </para>
    /// </summary>
    private void RestoreWindowPlacement()
    {
        if (_settings.Window is not { Width: > 0, Height: > 0 } placement)
        {
            return;
        }

        var bounds = new Rectangle(placement.X, placement.Y, placement.Width, placement.Height);

        // Some of it, not all: a window hanging a little off the right edge is where the user put
        // it, and moving it would be the surprising thing.
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

    /// <summary>
    /// Restores the workspace once the form has been laid out.
    ///
    /// <para>
    /// Not in the constructor: a <see cref="SplitContainer"/> rejects a splitter distance that
    /// does not fit inside it, and before the first layout it is still at its designer size rather
    /// than at the size the restored window just gave it.
    /// </para>
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_settings.Workspace is not { } workspace)
        {
            UpdateLayoutMenu();
            return;
        }

        // Distances first. Setting one on a collapsed pane is legal but pointless — the value is
        // kept and applied when the pane comes back — and doing it in this order means the panes
        // that are about to be shown are already the right size when they appear.
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

    /// <summary>
    /// Applies a saved splitter distance, if it fits.
    ///
    /// <para>
    /// A layout saved on a wide monitor and reopened on a narrow one can ask for a sidebar wider
    /// than the whole window, and <see cref="SplitContainer.SplitterDistance"/> answers that with
    /// an exception — so the value has to be checked either way.
    /// </para>
    ///
    /// <para>
    /// A value that does not fit is <em>dropped</em> rather than clamped to the nearest edge. It
    /// was measured against a geometry this window does not have, and pinning it to the boundary
    /// produces the worst arrangement available — a 9000-pixel sidebar clamped into a 1000-pixel
    /// window becomes a 658-pixel column of checkboxes next to a sliver of viewport. The designer's
    /// own distance is a considered number and is the better answer. Anything that merely fits
    /// differently is still applied exactly, which is the case that actually happens.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Writes the window and the workspace back on the way out.
    ///
    /// <para>
    /// <see cref="Form.RestoreBounds"/> rather than <see cref="Control.Bounds"/>: a maximized
    /// window's bounds are the screen's, and saving those would reopen a window that fills the
    /// display, believes it is a normal one, and has no smaller size to be restored to.
    /// </para>
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

        // A minimized window is not a placement anybody wants reopened, and RestoreBounds can be
        // empty before the window has ever been anywhere.
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

        // Focus mode is a temporary view of the workspace rather than a workspace of its own, so
        // what gets written is what F11 would put back.
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
