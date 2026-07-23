using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Controls;
using SoftEngine.WinForms.Debugging;
using SoftEngine.WinForms.Dialogs;
using SoftEngine.WinForms.Interop;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen : Form
{
    /// <summary>The bundled worlds offered by the model picker.</summary>
    private static readonly DemoEntry[] Demos =
    [
        new("Skull", "skull"),
        new("Parrot", "parrot"),
        new("Elefant", "elefant"),
        new("Teapot", "teapot"),
        new("Juliet", "Juliet"),
        new("Cubes", "cubes"),
        new("Spheres", "spheres"),
        new("Little town", "littletown"),
        new("Town", "town"),
        new("Big town", "bigtown"),
        new("Cube", "cube"),
        new("Big cube", "bigcube"),
        new("Textured cube", "texturedcube"),
        new("Empty", "empty"),
    ];

    private sealed record WorldSetup(SimpleWorld World, Vector3 CameraPosition, PerspectiveProjection? Projection);

    private readonly Label lblLoading;
    private readonly FlatProgressBar prgLoading;

    /// <summary>Set by every rendered frame, cleared when the debugger panels have caught up.</summary>
    private bool _frameDirty;

    private SceneObjectCatalog _catalog = SceneObjectCatalog.Empty;

    /// <summary>Id of the bundled world on screen, so the picker reopens on it.</summary>
    private string _currentDemoId = "skull";

    public MainScreen()
    {
        InitializeComponent();
        ApplyTheme();

        CenterToScreen();

        lblLoading = new Label
        {
            Text = "Loading…",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = panel3D1.BackColor,
            Visible = false,
        };
        panel3D1.Controls.Add(lblLoading);

        prgLoading = new FlatProgressBar
        {
            Size = new Size(280, 6),
            Maximum = 1000,
        };
        lblLoading.Controls.Add(prgLoading);
        lblLoading.Resize += (s, e) => CenterLoadingProgress();
        CenterLoadingProgress();

        btnLoadModel.Click += async (s, e) => await ShowModelPickerAsync();

        rdbNoneShading.Checked = panel3D1.Painter is null;
        rdbClassicShading.Checked = panel3D1.Painter is ClassicPainter;
        rdbFlatShading.Checked = panel3D1.Painter is FlatPainter;
        rdbGouraudShading.Checked = panel3D1.Painter is GouraudPainter;
        rdbPhongShading.Checked = panel3D1.Painter is PhongPainter;
        rdbTexturedShading.Checked = panel3D1.Painter is TexturedPainter;

        rdbNoneShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = null;
            panel3D1.Invalidate();
        };
        rdbClassicShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new ClassicPainter(); 
            panel3D1.Invalidate();
        };
        rdbFlatShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new FlatPainter(); 
            panel3D1.Invalidate();
        };
        rdbGouraudShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new GouraudPainter();
            panel3D1.Invalidate();
        };
        rdbPhongShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new PhongPainter();
            panel3D1.Invalidate();
        };
        rdbTexturedShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new TexturedPainter();
            panel3D1.Invalidate();
        };

        chkShowTriangles.Checked = panel3D1.RendererSettings.ShowTriangles;
        chkShowBackFacesCulling.Checked = panel3D1.RendererSettings.BackFaceCulling;
        chkShowXZGrid.Checked = panel3D1.RendererSettings.ShowXZGrid;
        chkShowAxes.Checked = panel3D1.RendererSettings.ShowAxes;

        chkShowTriangles.CheckedChanged += (s, e) => { panel3D1.RendererSettings.ShowTriangles = chkShowTriangles.Checked; panel3D1.Invalidate(); };
        chkShowBackFacesCulling.CheckedChanged += (s, e) => { panel3D1.RendererSettings.BackFaceCulling = chkShowBackFacesCulling.Checked; panel3D1.Invalidate(); };
        chkShowXZGrid.CheckedChanged += (s, e) => { panel3D1.RendererSettings.ShowXZGrid = chkShowXZGrid.Checked; panel3D1.Invalidate(); };
        chkShowAxes.CheckedChanged += (s, e) => { panel3D1.RendererSettings.ShowAxes = chkShowAxes.Checked; panel3D1.Invalidate(); };

        panel3D1.Scene = new Scene()
        {
            Projection = new PerspectiveProjection(40f * (float)Math.PI / 180f, .01f, 500f),
            Camera = new ArcBallCamera(panel3D1) { Position = new Vector3(0, 0, -60) }
        };

        InitializeDebugger();

        _ = PrepareWorldAsync("skull");
    }

    #region Graphics debugger

    /// <summary>
    /// Wires the debugger panels to the viewport. The renderer records its event list every
    /// frame, but the panels only pull from it on a timer: a drag repaints far faster than a
    /// list view can usefully be rebuilt.
    /// </summary>
    private void InitializeDebugger()
    {
        panel3D1.Diagnostics.CaptureEvents = mnuRecordEvents.Checked;
        panel3D1.ShowStatsOverlay = mnuStatsOverlay.Checked;

        panel3D1.FrameRendered += (s, e) => _frameDirty = true;
        panel3D1.ZoomChanged += (s, e) => UpdateStatus();
        panel3D1.SelectedPixelChanged += (s, e) => UpdateStatus();

        tmrDebugRefresh.Tick += (s, e) => RefreshDebugPanels();
        tmrDebugRefresh.Start();

        objectTablePanel.ActiveChanged += (s, e) => panel3D1.Invalidate();

        // Clicking a write in the pixel history reveals the event and the object behind it.
        pixelHistoryPanel.WriteSelected += (s, write) =>
        {
            eventListPanel.SelectEvent(write.EventIndex);

            if (write.ObjectId >= 0)
            {
                objectTablePanel.SelectObject(write.ObjectId);
            }
        };

        eventListPanel.EventSelected += (s, graphicsEvent) =>
        {
            if (graphicsEvent.ObjectId >= 0)
            {
                objectTablePanel.SelectObject(graphicsEvent.ObjectId);
            }
        };

        mnuLoadModel.Click += async (s, e) => await ShowModelPickerAsync();
        mnuOpenModel.Click += async (s, e) => await OpenModelAsync();
        mnuExit.Click += (s, e) => Close();

        mnuPixelHistory.CheckedChanged += (s, e) => splitLeft.Panel2Collapsed = !mnuPixelHistory.Checked;
        mnuObjectTable.CheckedChanged += (s, e) => splitCenter.Panel2Collapsed = !mnuObjectTable.Checked;
        mnuEventList.CheckedChanged += (s, e) => splitRight.Panel2Collapsed = !mnuEventList.Checked;

        mnuStatsOverlay.CheckedChanged += (s, e) =>
        {
            panel3D1.ShowStatsOverlay = mnuStatsOverlay.Checked;
            panel3D1.Invalidate();
        };

        mnuRecordEvents.CheckedChanged += (s, e) =>
        {
            panel3D1.Diagnostics.CaptureEvents = mnuRecordEvents.Checked;
            panel3D1.Invalidate();
        };

        mnuZoomIn.Click += (s, e) => panel3D1.ZoomIn();
        mnuZoomOut.Click += (s, e) => panel3D1.ZoomOut();
        mnuZoomActual.Click += (s, e) => panel3D1.ZoomActualSize();
        mnuClearPixel.Click += (s, e) => panel3D1.ClearSelectedPixel();

        UpdateStatus();
    }

    /// <summary>Pulls the last frame's capture into the three panels — at most once per timer tick.</summary>
    private void RefreshDebugPanels()
    {
        if (!_frameDirty)
        {
            return;
        }

        _frameDirty = false;

        var scene = panel3D1.Scene;
        var signature = SceneObjectCatalog.SignatureOf(scene, panel3D1.Painter);

        if (_catalog.Signature != signature)
        {
            _catalog = SceneObjectCatalog.Build(scene, panel3D1.Painter);
            objectTablePanel.SetCatalog(_catalog);
        }

        if (!splitRight.Panel2Collapsed)
        {
            eventListPanel.SetEvents(panel3D1.Diagnostics.Events);
        }

        if (!splitLeft.Panel2Collapsed)
        {
            pixelHistoryPanel.SetHistory(panel3D1.Diagnostics.PixelHistory, _catalog);
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        // 100% is the framing the world loaded with; the wheel and W/S move away from it.
        var buffer = panel3D1.BufferSize;
        lblZoomStatus.Text = $"Zoom: {panel3D1.Zoom * 100f:0}%  ·  {buffer.Width} × {buffer.Height}";

        if (panel3D1.SelectedPixel is { } pixel && panel3D1.SelectedPixelNormalized is { } normalized)
        {
            lblPixelStatus.Text = $"Selected pixel X: {pixel.X} ({normalized.X:0.000}) Y: {pixel.Y} ({normalized.Y:0.000})";
        }
        else
        {
            lblPixelStatus.Text = "Selected pixel: none — click the viewport to probe one";
        }

        if (panel3D1.Scene?.Camera is { } camera)
        {
            var position = camera.Position;
            lblCameraStatus.Text = $"Camera: ({position.X:0.##}, {position.Y:0.##}, {position.Z:0.##})";
        }

        var stats = panel3D1.Stats;
        lblFrameStatus.Text = $"Frame #{panel3D1.Diagnostics.FrameNumber} · {stats.CalculationTimeMs + stats.PainterTimeMs} ms";
    }

    #endregion

    /// <summary>Opens the model picker: the bundled worlds, or a file from the machine.</summary>
    private async Task ShowModelPickerAsync()
    {
        using var dialog = new ModelPickerDialog(Demos, _currentDemoId);

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Choice is not { } choice)
        {
            return;
        }

        if (choice.FilePath is { } path)
        {
            await PrepareWorldFromFileAsync(path);
        }
        else if (choice.DemoId is { } id)
        {
            await PrepareWorldAsync(id);
        }
    }

    private async Task OpenModelAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open 3D model",
            Filter = "3D models (*.obj;*.dae)|*.obj;*.dae"
                   + "|Wavefront OBJ (*.obj)|*.obj"
                   + "|Collada (*.dae)|*.dae"
                   + "|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await PrepareWorldFromFileAsync(dialog.FileName);
        }
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;

        tlpSidebar.BackColor = Theme.Surface;
        lblTitle.ForeColor = Theme.TextPrimary;
        lblDisplayHeader.ForeColor = Theme.TextSecondary;
        lblShadingHeader.ForeColor = Theme.TextSecondary;

        lblModelHeader.ForeColor = Theme.TextSecondary;
        lblCurrentModel.ForeColor = Theme.TextPrimary;

        btnLoadModel.BackColor = Theme.Selection;
        btnLoadModel.ForeColor = Theme.TextPrimary;
        btnLoadModel.FlatAppearance.BorderColor = Theme.Accent;
        btnLoadModel.FlatAppearance.MouseOverBackColor = Theme.Accent;

        foreach (Control control in flpDisplay.Controls)
        {
            control.ForeColor = Theme.TextPrimary;
        }
        foreach (Control control in flpShading.Controls)
        {
            control.ForeColor = Theme.TextPrimary;
        }

        pnlViewport.BackColor = Theme.Background;
        panel3D1.BackColor = Theme.Viewport;

        menuStrip.BackColor = Theme.Surface;
        menuStrip.ForeColor = Theme.TextPrimary;

        statusStrip.BackColor = Theme.Surface;
        statusStrip.ForeColor = Theme.TextSecondary;

        foreach (var split in new[] { splitMain, splitLeft, splitRight, splitCenter })
        {
            split.BackColor = Theme.Background;
            split.Panel1.BackColor = Theme.Background;
            split.Panel2.BackColor = Theme.Background;
        }
    }

    /// <summary>Places the progress bar just below the centered "Loading…" text.</summary>
    private void CenterLoadingProgress() =>
        prgLoading.Location = new Point(
            (lblLoading.ClientSize.Width - prgLoading.Width) / 2,
            lblLoading.ClientSize.Height / 2 + 40);

    private Task PrepareWorldAsync(string id)
    {
        _currentDemoId = id;
        var label = Demos.FirstOrDefault(demo => demo.Id == id)?.Display ?? id;

        return PrepareWorldCoreAsync(progress => BuildWorld(id, progress), label);
    }

    private Task PrepareWorldFromFileAsync(string path)
    {
        _currentDemoId = string.Empty;

        return PrepareWorldCoreAsync(progress => BuildWorldFromFile(path, progress), Path.GetFileName(path));
    }

    private async Task PrepareWorldCoreAsync(Func<IProgress<float>?, WorldSetup> build, string label)
    {
        btnLoadModel.Enabled = false;
        mnuLoadModel.Enabled = false;
        mnuOpenModel.Enabled = false;
        prgLoading.Value = 0;
        lblLoading.Visible = true;
        lblLoading.BringToFront();
        UseWaitCursor = true;

        try
        {
            // Progress<T> is created on the UI thread, so reports from the worker
            // are marshalled back here automatically.
            var progress = new Progress<float>(f =>
                prgLoading.Value = Math.Clamp((int)(f * prgLoading.Maximum), 0, prgLoading.Maximum));

            var setup = await Task.Run(() => build(progress));

            // Start every demo from the canonical view — without this, a previous
            // arc-ball drag stays baked into the camera orbit.
            if (panel3D1.Scene?.Camera is ArcBallCamera arcBall)
            {
                arcBall.Rotation = Quaternion.Identity;
            }
            panel3D1.Scene?.Camera.Position = setup.CameraPosition;

            // The distance a world is framed from is what the zoom readout calls 100%.
            panel3D1.ReferenceDistance = setup.CameraPosition.Length();

            // Every load sets a projection: either the demo's own, or one whose far plane
            // is derived from the world's extent — a far plane closer than the world's
            // farthest geometry visibly slices models while they are orbited, and the
            // previous world's projection must not leak into this one.
            panel3D1.Scene?.Projection = setup.Projection ?? ProjectionFor(setup);
            panel3D1.Scene?.World = setup.World;

            lblCurrentModel.Text = label;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load '{label}': {ex.Message}", "Load error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            lblLoading.Visible = false;
            btnLoadModel.Enabled = true;
            mnuLoadModel.Enabled = true;
            mnuOpenModel.Enabled = true;

            // The world changed under any selected pixel, and its history with it.
            panel3D1.ClearSelectedPixel();
            panel3D1.Invalidate();
        }
    }

    /// <summary>
    /// A projection whose far plane contains the whole world from anywhere on the camera's
    /// orbit: the camera distance plus the world's farthest geometry, with headroom so
    /// dollying out a little doesn't immediately clip.
    /// </summary>
    private static PerspectiveProjection ProjectionFor(WorldSetup setup)
    {
        var worldRadius = 0f;
        foreach (var mesh in setup.World.Meshes)
        {
            var scale = Math.Max(Math.Abs(mesh.Scale.X), Math.Max(Math.Abs(mesh.Scale.Y), Math.Abs(mesh.Scale.Z)));
            var reach = mesh.Position.Length() + mesh.BoundingRadius * scale;

            if (!float.IsNaN(reach) && !float.IsInfinity(reach))
            {
                worldRadius = Math.Max(worldRadius, reach);
            }
        }

        var far = Math.Max(500f, (setup.CameraPosition.Length() + worldRadius) * 2f);

        return new PerspectiveProjection(40f * (float)Math.PI / 180f, .01f, far);
    }

    private static WorldSetup BuildWorld(string id, IProgress<float>? progress)
    {
        var world = new SimpleWorld();
        var cameraPosition = new Vector3(0, 0, -60);
        PerspectiveProjection? projection = null;

        switch (id)
        {
            case "skull":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\skull.dae", progress));
                cameraPosition = new Vector3(0, 0, -5);
                break;

            case "parrot":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\parrot.dae", progress));
                cameraPosition = new Vector3(0, 0, -500);
                world.Lights.Add(new PointLight { Position = new Vector3(150, 200, 400) });
                break;

            case "teapot":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\teapot.dae", progress));
                break;

            case "elefant":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\elefant.dae", progress));
                cameraPosition = new Vector3(0, 0, -1500);
                projection = new PerspectiveProjection(40f * (float)Math.PI / 180f, .01f, 65535f);
                world.Lights.Add(new PointLight { Position = new Vector3(500, 800, 1200) });
                break;

            case "Juliet":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\Juliet.dae", progress));
                cameraPosition = new Vector3(0, 0, -500);
                world.Lights.Add(new PointLight { Position = new Vector3(150, 200, 400) });
                break;

            case "empty":
                break;

            case "town":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 50; var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "littletown":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 10; var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "bigtown":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 200; var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "cube":
                world.Meshes.Add(new Cube());
                break;

            case "bigcube":
                world.Meshes.Add(new Cube() { Scale = new Vector3(100, 100, 100) });
                break;

            case "texturedcube":
                world.Meshes.Add(new TexturedCube
                {
                    Scale = new Vector3(20, 20, 20),
                    Rotation = new Rotation3D(25, 35, 0).ToRad(),
                });
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });
                break;

            case "spheres":
            {
                int d = 5;
                int s = 2;
                for (int x = -d; x <= d; x += s)
                {
                    for (int y = -d; y <= d; y += s)
                    {
                        for (int z = -d; z <= d; z += s)
                        {
                            world.Meshes.Add(new IcoSphere(2)
                            {
                                Position = new Vector3(x, y, z)
                            });
                        }
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "cubes":
            {
                var d = 20; var s = 2; var r = new Random();
                for (int x = -d; x <= d; x += s)
                {
                    for (int y = -d; y <= d; y += s)
                    {
                        for (int z = -d; z <= d; z += s)
                        {
                            world.Meshes.Add(new Cube()
                            {
                                Position = new Vector3(x, y, z),
                                Rotation = new Rotation3D(
                                    r.Next(-90, 90),
                                    r.Next(-90, 90),
                                    r.Next(-90, 90)).ToRad()
                            });
                        }
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }
        }

        return new WorldSetup(world, cameraPosition, projection);
    }

    /// <summary>
    /// Loads a model file (OBJ or Collada) into a fresh world, framing the camera and
    /// depth range from the model's own size so any scale of mesh shows up on load.
    /// </summary>
    private static WorldSetup BuildWorldFromFile(string path, IProgress<float>? progress)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        IMesh[] meshes = extension switch
        {
            ".obj" => ObjImporter.ImportObj(path, progress, ImageTexture.Load),
            ".dae" => MeshFactory.HackyImportCollada(path, progress),
            _ => throw new NotSupportedException($"Unsupported model format '{extension}'."),
        };

        var world = new SimpleWorld();
        world.Meshes.AddRange(meshes);
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });

        // Frame the model: pull the camera back proportional to its bounding radius and push
        // the far plane out far enough to contain it, whatever units the file uses.
        var radius = meshes.Length == 0 ? 1f : meshes.Max(m => m.BoundingRadius);
        if (radius <= 0f || float.IsInfinity(radius) || float.IsNaN(radius))
        {
            radius = 1f;
        }

        var cameraPosition = new Vector3(0, 0, -radius * 3f);
        var projection = new PerspectiveProjection(40f * (float)Math.PI / 180f, .01f, Math.Max(500f, radius * 20f));

        return new WorldSetup(world, cameraPosition, projection);
    }
}
