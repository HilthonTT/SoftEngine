using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Controls;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen : Form
{
    private sealed record DemoItem(string Display, string Id);

    private sealed record WorldSetup(SimpleWorld World, Vector3 CameraPosition, PerspectiveProjection? Projection);

    private readonly Label lblLoading;
    private readonly FlatProgressBar prgLoading;

    public MainScreen()
    {
        InitializeComponent();
        ApplyTheme();

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

        lstDemos.DrawMode = DrawMode.OwnerDrawFixed;
        lstDemos.ItemHeight = 34;
        lstDemos.DrawItem += DrawDemoItem;

        lstDemos.DisplayMember = nameof(DemoItem.Display);
        lstDemos.ValueMember = nameof(DemoItem.Id);
        lstDemos.DataSource = new DemoItem[]
        {
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
        };

        lstDemos.DoubleClick += LstDemos_DoubleClick;

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

        _ = PrepareWorldAsync("skull");
    }

    private async void LstDemos_DoubleClick(object? sender, EventArgs e)
    {
        if (lstDemos.SelectedValue is string id)
        {
            await PrepareWorldAsync(id);
        }
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;

        tlpSidebar.BackColor = Theme.Surface;
        lblTitle.ForeColor = Theme.TextPrimary;
        lblWorldsHeader.ForeColor = Theme.TextSecondary;
        lblDisplayHeader.ForeColor = Theme.TextSecondary;
        lblShadingHeader.ForeColor = Theme.TextSecondary;

        lstDemos.BackColor = Theme.Surface;
        lstDemos.ForeColor = Theme.TextPrimary;

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
    }

    private void DrawDemoItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= lstDemos.Items.Count)
        {
            return;
        }

        var item = (DemoItem)lstDemos.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;

        using var back = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(back, e.Bounds);

        var bounds = Rectangle.Inflate(e.Bounds, -2, -2);
        if (selected)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Theme.Selection);
            using var path = Theme.RoundedRect(bounds, 6);
            e.Graphics.FillPath(fill, path);

            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, bounds.Left + 2, bounds.Top + 8, 3, bounds.Height - 16);
        }

        TextRenderer.DrawText(
            e.Graphics,
            item.Display,
            lstDemos.Font,
            new Rectangle(bounds.Left + 14, bounds.Top, bounds.Width - 14, bounds.Height),
            selected ? Theme.TextPrimary : Theme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Places the progress bar just below the centered "Loading…" text.</summary>
    private void CenterLoadingProgress() =>
        prgLoading.Location = new Point(
            (lblLoading.ClientSize.Width - prgLoading.Width) / 2,
            lblLoading.ClientSize.Height / 2 + 40);

    private async Task PrepareWorldAsync(string id)
    {
        lstDemos.Enabled = false;
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

            var setup = await Task.Run(() => BuildWorld(id, progress));

            // Start every demo from the canonical view — without this, a previous
            // arc-ball drag stays baked into the camera orbit.
            if (panel3D1.Scene?.Camera is ArcBallCamera arcBall)
            {
                arcBall.Rotation = Quaternion.Identity;
            }
            panel3D1.Scene?.Camera.Position = setup.CameraPosition;
            if (setup.Projection is not null)
            {
                panel3D1.Scene?.Projection = setup.Projection;
            }
            panel3D1.Scene?.World = setup.World;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load '{id}': {ex.Message}", "Load error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            lblLoading.Visible = false;
            lstDemos.Enabled = true;
            panel3D1.Invalidate();
        }
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
}
