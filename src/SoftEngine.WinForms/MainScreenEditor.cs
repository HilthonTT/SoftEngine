using SoftEngine.Core.Animation;
using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Controls;
using System.Numerics;

namespace SoftEngine.WinForms;

/// <summary>
/// The editor chrome around the viewport: the shading switch and overlays in its header, the
/// tool shelf, the outliner, the Object tab's transform fields, the timeline, and the keys a
/// 3D suite's hands expect — N, T, H, Alt+G/R/S, Space.
/// </summary>
public sealed partial class MainScreen
{
    private const string DefaultHints =
        "LMB  Select · Orbit     RMB  Pan     Wheel  Zoom     Shift+A  Add     G R S  Transform     X  Delete     Space  Play";

    private enum ShadingMode
    {
        Wireframe,
        Solid,
        MaterialPreview,
        Rendered,
    }

    /// <summary>Whether the wireframe shading mode turned the triangle overlay on, and so should turn it off.</summary>
    private bool _wireframeAddedTriangles;

    private bool _syncingObject;

    private IMesh? _fieldMesh;
    private TransformState _fieldStart;

    private bool _playingBeforeScrub;

    private (IWorld? World, int Meshes, int Lights, int Visible) _outlinerSignature;

    private readonly Dictionary<IMesh, string> _meshNames = new(ReferenceEqualityComparer.Instance);

    private void InitializeEditor()
    {
        lblCurrentModel.TextChanged += (s, e) => lblSceneModel.Text = lblCurrentModel.Text;
        lblSceneModel.Text = lblCurrentModel.Text;
        lblHintStatus.Text = DefaultHints;

        InitializeShadingSwitch();
        InitializeOverlays();
        InitializeToolShelf();
        InitializeViewportMenus();
        InitializeOutliner();
        InitializeObjectProperties();
        InitializeTimeline();

        tmrDebugRefresh.Tick += (s, e) => PollEditor();
        panel3D1.PickedChanged += (s, e) => SyncSelection();

        PollEditor();
    }

    /// <summary>Keeps the outliner, the Object tab and the timeline up with the scene.</summary>
    private void PollEditor()
    {
        var world = panel3D1.Scene?.World;
        var signature = (world, world?.Meshes.Count ?? 0, world?.Lights.Count ?? 0,
            world?.Meshes.Count(mesh => mesh.Visible) ?? 0);

        if (signature != _outlinerSignature)
        {
            _outlinerSignature = signature;
            RebuildOutliner();
            UpdateEditorStatus();
        }

        UpdateObjectPanel();
        UpdateTimeline();
    }

    #region Shading switch

    private void InitializeShadingSwitch()
    {
        btnShadeWireframe.Click += (s, e) => SetShadingMode(ShadingMode.Wireframe);
        btnShadeSolid.Click += (s, e) => SetShadingMode(ShadingMode.Solid);
        btnShadeMaterial.Click += (s, e) => SetShadingMode(ShadingMode.MaterialPreview);
        btnShadeRendered.Click += (s, e) => SetShadingMode(ShadingMode.Rendered);

        foreach (var radio in new[] { rdbNoneShading, rdbClassicShading, rdbFlatShading, rdbGouraudShading, rdbPhongShading, rdbTexturedShading, rdbMaterialShading, rdbPbrShading })
        {
            radio.CheckedChanged += (s, e) =>
            {
                if (s is RadioButton { Checked: true })
                {
                    OnShadingChanged();
                }
            };
        }

        UpdateShadingButtons();
    }

    private ShadingMode CurrentShading =>
        rdbNoneShading.Checked ? ShadingMode.Wireframe
        : rdbPbrShading.Checked ? ShadingMode.Rendered
        : rdbTexturedShading.Checked || rdbMaterialShading.Checked ? ShadingMode.MaterialPreview
        : ShadingMode.Solid;

    /// <summary>
    /// The four-way switch over the eight shading models: each button picks a representative,
    /// and keeps whichever model of its group is already chosen — Solid leaves Phong as Phong.
    /// </summary>
    private void SetShadingMode(ShadingMode mode)
    {
        if (mode == CurrentShading)
        {
            return;
        }

        switch (mode)
        {
            case ShadingMode.Wireframe:
                if (!chkShowTriangles.Checked)
                {
                    chkShowTriangles.Checked = true;
                    _wireframeAddedTriangles = true;
                }

                rdbNoneShading.Checked = true;
                break;

            case ShadingMode.Solid:
                rdbGouraudShading.Checked = true;
                break;

            case ShadingMode.MaterialPreview:
                rdbMaterialShading.Checked = true;
                break;

            case ShadingMode.Rendered:
                rdbPbrShading.Checked = true;
                break;
        }

        panel3D1.Focus();
    }

    private void OnShadingChanged()
    {
        if (CurrentShading != ShadingMode.Wireframe && _wireframeAddedTriangles)
        {
            _wireframeAddedTriangles = false;
            chkShowTriangles.Checked = false;
        }

        UpdateShadingButtons();
    }

    private void UpdateShadingButtons()
    {
        var mode = CurrentShading;

        btnShadeWireframe.Checked = mode == ShadingMode.Wireframe;
        btnShadeSolid.Checked = mode == ShadingMode.Solid;
        btnShadeMaterial.Checked = mode == ShadingMode.MaterialPreview;
        btnShadeRendered.Checked = mode == ShadingMode.Rendered;
    }

    #endregion

    #region Overlays and snapping

    private void InitializeOverlays()
    {
        (string Text, Func<bool> Get, Action<bool> Set)[] overlays =
        [
            ("Floor Grid", () => chkShowXZGrid.Checked, on => chkShowXZGrid.Checked = on),
            ("Axes", () => chkShowAxes.Checked, on => chkShowAxes.Checked = on),
            ("Wireframe", () => chkShowTriangles.Checked, on => chkShowTriangles.Checked = on),
            ("Skeleton", () => chkShowSkeleton.Checked, on => chkShowSkeleton.Checked = on),
            ("Statistics", () => mnuStatsOverlay.Checked, on => mnuStatsOverlay.Checked = on),
        ];

        foreach (var (text, get, set) in overlays)
        {
            var item = new ToolStripMenuItem(text) { CheckOnClick = true };
            item.CheckedChanged += (s, e) =>
            {
                if (get() != item.Checked)
                {
                    set(item.Checked);
                }
            };

            overlaysMenu.Items.Add(item);
            overlaysMenu.Opening += (s, e) => item.Checked = get();
        }

        btnOverlays.Click += (s, e) => overlaysMenu.Show(btnOverlays, new Point(0, btnOverlays.Height + 2));

        btnSnap.CheckedChanged += (s, e) => ApplySnapping(btnSnap.Checked);
    }

    #endregion

    #region Tool shelf

    private void InitializeToolShelf()
    {
        btnToolSelect.Click += (s, e) => SelectTool(GizmoMode.Off);
        btnToolMove.Click += (s, e) => SelectTool(GizmoMode.Translate);
        btnToolRotate.Click += (s, e) => SelectTool(GizmoMode.Rotate);
        btnToolScale.Click += (s, e) => SelectTool(GizmoMode.Scale);

        cboGizmo.SelectedIndexChanged += (s, e) => UpdateToolButtons();

        UpdateToolButtons();
    }

    private void SelectTool(GizmoMode mode)
    {
        for (var i = 0; i < cboGizmo.Items.Count; i++)
        {
            if (cboGizmo.Items[i] is GizmoChoice choice && choice.Mode == mode)
            {
                cboGizmo.SelectedIndex = i;
                break;
            }
        }

        panel3D1.Focus();
    }

    private void UpdateToolButtons()
    {
        var mode = (cboGizmo.SelectedItem as GizmoChoice)?.Mode ?? GizmoMode.Off;

        btnToolSelect.Checked = mode == GizmoMode.Off;
        btnToolMove.Checked = mode == GizmoMode.Translate;
        btnToolRotate.Checked = mode == GizmoMode.Rotate;
        btnToolScale.Checked = mode == GizmoMode.Scale;
    }

    #endregion

    #region Viewport header menus

    private void InitializeViewportMenus()
    {
        var view = new ToolStripMenuItem("View");

        ToolStripMenuItem Do(string text, string keys, Action action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys };
            item.Click += (s, e) =>
            {
                action();
                panel3D1.Focus();
            };
            return item;
        }

        var viewpoint = new ToolStripMenuItem("Viewpoint");
        viewpoint.DropDownItems.AddRange([
            Do("Front", "Numpad 1", () => panel3D1.LookAlong(AxisView.Front)),
            Do("Back", "Ctrl+Numpad 1", () => panel3D1.LookAlong(AxisView.Back)),
            Do("Right", "Numpad 3", () => panel3D1.LookAlong(AxisView.Right)),
            Do("Left", "Ctrl+Numpad 3", () => panel3D1.LookAlong(AxisView.Left)),
            Do("Top", "Numpad 7", () => panel3D1.LookAlong(AxisView.Top)),
            Do("Bottom", "Ctrl+Numpad 7", () => panel3D1.LookAlong(AxisView.Bottom)),
        ]);

        view.DropDownItems.AddRange([
            Do("Toolbar", "T", () => mnuToggleToolShelf.Checked = !mnuToggleToolShelf.Checked),
            Do("Sidebar", "N", () => mnuToggleSidebar.Checked = !mnuToggleSidebar.Checked),
            new ToolStripSeparator(),
            Do("Frame All", "Home", panel3D1.ZoomActualSize),
            viewpoint,
            new ToolStripSeparator(),
            Do("Toggle Maximize Area", "Ctrl+Space", () => mnuFocusViewport.Checked = !mnuFocusViewport.Checked),
        ]);

        var select = new ToolStripMenuItem("Select");
        select.DropDownItems.AddRange([
            Do("None", "Esc", panel3D1.ClearPick),
            new ToolStripSeparator(),
            Do("Next Object", "Tab", () => CycleSelection(+1)),
            Do("Previous Object", "Shift+Tab", () => CycleSelection(-1)),
        ]);

        var add = new ToolStripMenuItem("Add");
        var mesh = new ToolStripMenuItem("Mesh") { DropDownDirection = ToolStripDropDownDirection.Right };
        mesh.DropDownItems.AddRange(BuildAddItems());
        add.DropDownItems.Add(mesh);

        var objectMenu = new ToolStripMenuItem("Object");
        var transform = new ToolStripMenuItem("Transform");
        transform.DropDownItems.AddRange([
            Do("Move Tool", "", () => SelectTool(GizmoMode.Translate)),
            Do("Rotate Tool", "", () => SelectTool(GizmoMode.Rotate)),
            Do("Scale Tool", "", () => SelectTool(GizmoMode.Scale)),
        ]);

        var clear = new ToolStripMenuItem("Clear");
        clear.DropDownItems.AddRange([
            Do("Location", "Alt+G", () => ClearTransform(TransformPart.Location)),
            Do("Rotation", "Alt+R", () => ClearTransform(TransformPart.Rotation)),
            Do("Scale", "Alt+S", () => ClearTransform(TransformPart.Scale)),
        ]);

        var visibility = new ToolStripMenuItem("Show/Hide");
        visibility.DropDownItems.AddRange([
            Do("Hide Selected", "H", HideSelected),
            Do("Show Hidden", "Alt+H", ShowAllHidden),
        ]);

        objectMenu.DropDownItems.AddRange([
            transform, clear,
            new ToolStripSeparator(), Do("Snap", "Ctrl+G", () => ApplySnapping(!_gizmo.Snap.Enabled)),
            new ToolStripSeparator(), visibility,
            new ToolStripSeparator(), Do("Delete", "X", DeleteSelected),
        ]);

        viewportMenu.Items.AddRange([view, select, add, objectMenu]);

        foreach (var item in viewportMenu.Items.OfType<ToolStripMenuItem>())
        {
            item.ForeColor = Theme.TextPrimary;
            item.DropDown.BackColor = Theme.Background;
        }
    }

    private enum TransformPart
    {
        Location,
        Rotation,
        Scale,
    }

    /// <summary>Alt+G, Alt+R, Alt+S: put one part of the selected mesh's transform back to rest, undoably.</summary>
    private void ClearTransform(TransformPart part)
    {
        if (panel3D1.Picked?.Mesh is not { } mesh)
        {
            return;
        }

        var before = TransformState.Of(mesh);

        switch (part)
        {
            case TransformPart.Location:
                mesh.Position = Vector3.Zero;
                break;

            case TransformPart.Rotation:
                mesh.Rotation = new Rotation3D(0, 0, 0);
                break;

            case TransformPart.Scale:
                mesh.Scale = Vector3.One;
                break;
        }

        _history.Push(TransformEdit.Between(mesh, before, $"Clear {part}"));

        panel3D1.Invalidate();
        UpdateStatus();
    }

    private void HideSelected()
    {
        if (panel3D1.Picked?.Mesh is Mesh mesh)
        {
            mesh.Visible = false;
            panel3D1.ClearPick();
            OnVisibilityChanged();
        }
    }

    private void ShowAllHidden()
    {
        if (panel3D1.Scene?.World is not { } world)
        {
            return;
        }

        foreach (var mesh in world.Meshes.OfType<Mesh>())
        {
            mesh.Visible = true;
        }

        OnVisibilityChanged();
    }

    private void OnVisibilityChanged()
    {
        panel3D1.ResetTemporalHistory();
        panel3D1.Invalidate();
        PollEditor();
    }

    private void DeleteSelected()
    {
        if (panel3D1.Picked?.Mesh is { } mesh)
        {
            DeleteMesh(mesh);
        }
    }

    private void CycleSelection(int direction)
    {
        if (panel3D1.Scene?.World is not { Meshes.Count: > 0 } world)
        {
            return;
        }

        var meshes = world.Meshes.Where(mesh => mesh.Visible).ToList();

        if (meshes.Count == 0)
        {
            return;
        }

        var current = panel3D1.Picked?.Mesh is { } picked ? meshes.IndexOf(picked) : -1;
        var next = current < 0
            ? (direction > 0 ? 0 : meshes.Count - 1)
            : ((current + direction) % meshes.Count + meshes.Count) % meshes.Count;

        panel3D1.SelectMesh(meshes[next]);
    }

    #endregion

    #region Outliner

    private void InitializeOutliner()
    {
        outliner.ItemActivated += (s, item) =>
        {
            if (item.Tag is IMesh mesh)
            {
                panel3D1.SelectMesh(mesh);
            }
        };

        outliner.ItemDoubleClicked += (s, item) =>
        {
            if (item.Tag is IMesh)
            {
                properties.Select("Object");
            }
        };

        outliner.VisibilityToggled += (s, item) =>
        {
            if (item.Tag is not Mesh mesh)
            {
                return;
            }

            mesh.Visible = !mesh.Visible;

            if (!mesh.Visible && ReferenceEquals(panel3D1.Picked?.Mesh, mesh))
            {
                panel3D1.ClearPick();
            }

            OnVisibilityChanged();
        };

        objectTablePanel.ActiveChanged += (s, e) => PollEditor();
    }

    /// <summary>A mesh's name as the outliner shows it, numbered apart from others of its kind.</summary>
    private string OutlinerName(IMesh mesh) =>
        _meshNames.TryGetValue(mesh, out var name) ? name : BaseName(mesh);

    private static string BaseName(IMesh mesh)
    {
        if (mesh.Parent is { Name.Length: > 0 } node)
        {
            return node.Name;
        }

        return mesh switch
        {
            Box or Cube or TexturedCube => "Cube",
            PlaneMesh => "Plane",
            UvSphere => "Sphere",
            IcoSphere => "Icosphere",
            Cylinder => "Cylinder",
            Cone => "Cone",
            Torus => "Torus",
            _ => mesh.GetType().Name is var type && type.EndsWith("Mesh", StringComparison.Ordinal) && type.Length > 4
                ? type[..^4]
                : mesh.GetType().Name,
        };
    }

    private void RebuildOutliner()
    {
        _meshNames.Clear();

        var items = new List<OutlinerItem>
        {
            new("Scene Collection", Glyph.Collection, 0, null) { Selectable = false },
        };

        if (panel3D1.Scene is { } scene)
        {
            items.Add(new OutlinerItem("Camera", Glyph.Camera, 1, null)
            {
                Selectable = false,
                GlyphColor = Theme.TextSecondary,
                Detail = scene.Camera is ArcBallCamera ? "orbit" : null,
            });
        }

        if (panel3D1.Scene?.World is { } world)
        {
            var lightCounts = new Dictionary<string, int>();

            foreach (var light in world.Lights)
            {
                var kind = light switch
                {
                    DirectionalLight => "Sun",
                    SpotLight => "Spot",
                    PointLight => "Point",
                    _ => "Light",
                };

                items.Add(new OutlinerItem(Numbered(kind, lightCounts), Glyph.Light, 1, null)
                {
                    Selectable = false,
                    GlyphColor = Color.FromArgb(240, 200, 90),
                });
            }

            var counts = new Dictionary<string, int>();

            foreach (var mesh in world.Meshes)
            {
                var name = Numbered(BaseName(mesh), counts);
                _meshNames[mesh] = name;

                items.Add(new OutlinerItem(name, Glyph.Mesh, 1, mesh)
                {
                    Visible = mesh is Mesh ? mesh.Visible : null,
                    GlyphColor = Color.FromArgb(255, 170, 80),
                    Detail = $"{mesh.Triangles.Length:N0} tris",
                });
            }
        }

        outliner.SetItems(items);
        outliner.Select(panel3D1.Picked?.Mesh);

        static string Numbered(string name, Dictionary<string, int> seen)
        {
            var count = seen.GetValueOrDefault(name);
            seen[name] = count + 1;

            return count == 0 ? name : $"{name}.{count:000}";
        }
    }

    private void SyncSelection()
    {
        CancelFieldTyping();
        outliner.Select(panel3D1.Picked?.Mesh);
        UpdateObjectPanel();
        UpdateEditorStatus();
    }

    #endregion

    #region Object properties

    private void InitializeObjectProperties()
    {
        foreach (var vector in new[] { vecLocation, vecRotation, vecScale })
        {
            foreach (var field in vector.Fields)
            {
                field.EditStarted += (s, e) => BeginFieldEdit();
                field.ValueChanged += (s, e) => ApplyFields();
                field.EditCommitted += (s, e) => CommitFieldEdit();
                field.EditCancelled += (s, e) => CancelFieldEdit();
            }
        }

        chkObjectVisible.CheckedChanged += (s, e) =>
        {
            if (_syncingObject || panel3D1.Picked?.Mesh is not Mesh mesh || mesh.Visible == chkObjectVisible.Checked)
            {
                return;
            }

            mesh.Visible = chkObjectVisible.Checked;
            OnVisibilityChanged();
        };

        UpdateObjectPanel();
    }

    /// <summary>
    /// A number half-typed into a transform field belongs to the object it was typed against;
    /// once the selection or the history moves on, committing it would land on the wrong one.
    /// </summary>
    private void CancelFieldTyping()
    {
        vecLocation.CancelTyping();
        vecRotation.CancelTyping();
        vecScale.CancelTyping();
    }

    private void BeginFieldEdit()
    {
        if (panel3D1.Picked?.Mesh is not { } mesh)
        {
            return;
        }

        _fieldMesh = mesh;
        _fieldStart = TransformState.Of(mesh);
    }

    private void ApplyFields()
    {
        if (_syncingObject || _fieldMesh is not { } mesh)
        {
            return;
        }

        var rotation = vecRotation.Value * (MathF.PI / 180f);

        mesh.Position = vecLocation.Value;
        mesh.Rotation = new Rotation3D(rotation.X, rotation.Y, rotation.Z);
        mesh.Scale = vecScale.Value;

        panel3D1.Invalidate();
    }

    private void CommitFieldEdit()
    {
        if (_fieldMesh is not { } mesh)
        {
            return;
        }

        _fieldMesh = null;
        _history.Push(TransformEdit.Between(mesh, _fieldStart, "Transform"));

        UpdateStatus();
    }

    private void CancelFieldEdit()
    {
        if (_fieldMesh is not { } mesh)
        {
            return;
        }

        _fieldMesh = null;
        _fieldStart.ApplyTo(mesh);
        panel3D1.Invalidate();
        UpdateObjectPanel();
    }

    private void UpdateObjectPanel()
    {
        var mesh = panel3D1.Picked?.Mesh;
        var has = mesh is not null;

        lblObjectHint.Visible = !has;
        pnlObject.Visible = has;
        pnlTransform.Visible = has;
        pnlVisibility.Visible = has;

        if (mesh is null)
        {
            return;
        }

        _syncingObject = true;

        try
        {
            var name = $"{OutlinerName(mesh)}   ·   {mesh.GetType().Name}, {mesh.Vertices.Length:N0} verts, {mesh.Triangles.Length:N0} tris";

            if (lblObjectName.Text != name)
            {
                lblObjectName.Text = name;
            }

            if (_fieldMesh is null)
            {
                const float toDegrees = 180f / MathF.PI;

                vecLocation.Value = mesh.Position;
                vecRotation.Value = new Vector3(mesh.Rotation.XPitch, mesh.Rotation.YYaw, mesh.Rotation.ZRoll) * toDegrees;
                vecScale.Value = mesh.Scale;
            }

            chkObjectVisible.Enabled = mesh is Mesh;
            chkObjectVisible.Checked = mesh.Visible;
        }
        finally
        {
            _syncingObject = false;
        }
    }

    #endregion

    #region Timeline

    private void InitializeTimeline()
    {
        btnPlay.Click += (s, e) => TogglePlayback();
        btnJumpStart.Click += (s, e) => JumpTo(0f);
        btnJumpEnd.Click += (s, e) => JumpTo(timeline.Duration);

        chkAnimate.CheckedChanged += (s, e) => UpdatePlayButton();

        timeline.ScrubStarted += (s, e) =>
        {
            _playingBeforeScrub = chkAnimate.Checked;
            chkAnimate.Checked = false;
        };

        timeline.Scrubbed += (s, time) => PoseAt(time);

        timeline.ScrubEnded += (s, e) => chkAnimate.Checked = _playingBeforeScrub;

        panel3D1.FrameRendered += (s, e) => UpdateTimeline();

        UpdatePlayButton();
    }

    private IReadOnlyList<AnimationPlayer> Players =>
        panel3D1.Scene?.World?.Animations ?? [];

    private void TogglePlayback()
    {
        if (Players.Count == 0)
        {
            return;
        }

        chkAnimate.Checked = !chkAnimate.Checked;
    }

    private void JumpTo(float time)
    {
        if (Players.Count == 0)
        {
            return;
        }

        PoseAt(time);
        timeline.Time = time;
    }

    /// <summary>
    /// Puts every player at an absolute time and poses the world there — the world's own Update
    /// takes a delta, which is the wrong thing to hand it a time on the ruler.
    /// </summary>
    private void PoseAt(float time)
    {
        if (panel3D1.Scene?.World is not { } world)
        {
            return;
        }

        foreach (var player in world.Animations)
        {
            // Just short of the end, so a looping clip shows its last frame rather than its first.
            player.Time = player.Duration > 0f ? MathF.Min(time, player.Duration - 1e-4f) : 0f;
        }

        world.Update(0f);

        panel3D1.ResetTemporalHistory();
        panel3D1.Invalidate();
        UpdateTimeline();
    }

    private void UpdatePlayButton()
    {
        var playing = chkAnimate.Checked && Players.Count > 0;
        btnPlay.Glyph = playing ? Glyph.Pause : Glyph.Play;
        btnPlay.Checked = playing;
        btnPlay.Invalidate();
    }

    private void UpdateTimeline()
    {
        var players = Players;
        var duration = players.Count == 0 ? 0f : players.Max(player => player.Duration);

        timeline.Duration = duration;

        if (!timeline.IsScrubbing && players.Count > 0)
        {
            // Every clip plays at once and each wraps at its own length; the ruler spans the
            // longest, so the playhead follows that one — the first could be shorter.
            timeline.Time = players.MaxBy(player => player.Duration)!.Time;
        }

        var enabled = players.Count > 0;
        btnPlay.Enabled = enabled;
        btnJumpStart.Enabled = enabled;
        btnJumpEnd.Enabled = enabled;

        var text = enabled
            ? $"Frame {MathF.Round(timeline.Time * TimelineRuler.FramesPerSecond):0} / {MathF.Ceiling(duration * TimelineRuler.FramesPerSecond):0}   ·   {TimelineRuler.FramesPerSecond:0} fps"
            : string.Empty;

        if (lblTimelineFrame.Text != text)
        {
            lblTimelineFrame.Text = text;
        }

        UpdatePlayButton();
    }

    #endregion

    #region Status and keys

    private void UpdateEditorStatus()
    {
        if (panel3D1.Scene?.World is not { } world)
        {
            lblSceneStats.Text = "Scene";
            return;
        }

        var visible = world.Meshes.Where(mesh => mesh.Visible).ToList();
        var vertices = visible.Sum(mesh => (long)mesh.Vertices.Length);
        var triangles = visible.Sum(mesh => (long)mesh.Triangles.Length);

        var active = panel3D1.Picked?.Mesh is { } mesh ? $"{OutlinerName(mesh)}  |  " : string.Empty;
        var selected = panel3D1.Picked is null ? 0 : 1;

        lblSceneStats.Text =
            $"{active}Objects {selected}/{world.Meshes.Count}  |  Verts {vertices:N0}  |  Tris {triangles:N0}";
    }

    /// <summary>
    /// The suite's single-letter keys, taken only while the viewport has the keyboard — a text
    /// box or a list elsewhere in the window keeps its own keys.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (panel3D1.Focused && !_transform.IsActive && HandleEditorKey(keyData))
        {
            return true;
        }

        // Redo moved to Ctrl+Shift+Z with the rest of the suite's keys; Ctrl+Y still works for the
        // hands that learned it first.
        if (keyData == (Keys.Control | Keys.Y) && mnuRedo.Enabled)
        {
            mnuRedo.PerformClick();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Space))
        {
            mnuFocusViewport.Checked = !mnuFocusViewport.Checked;
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool HandleEditorKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Space:
                TogglePlayback();
                return Players.Count > 0;

            case Keys.Shift | Keys.Left:
                JumpTo(0f);
                return Players.Count > 0;

            case Keys.Shift | Keys.Right:
                JumpTo(timeline.Duration);
                return Players.Count > 0;

            case Keys.N:
                mnuToggleSidebar.Checked = !mnuToggleSidebar.Checked;
                return true;

            case Keys.T:
                mnuToggleToolShelf.Checked = !mnuToggleToolShelf.Checked;
                return true;

            case Keys.H:
                HideSelected();
                return true;

            case Keys.Alt | Keys.H:
                ShowAllHidden();
                return true;

            case Keys.Alt | Keys.G:
                ClearTransform(TransformPart.Location);
                return true;

            case Keys.Alt | Keys.R:
                ClearTransform(TransformPart.Rotation);
                return true;

            case Keys.Alt | Keys.S:
                ClearTransform(TransformPart.Scale);
                return true;

            case Keys.Tab:
                CycleSelection(+1);
                return true;

            case Keys.Shift | Keys.Tab:
                CycleSelection(-1);
                return true;

            default:
                return false;
        }
    }

    #endregion
}
