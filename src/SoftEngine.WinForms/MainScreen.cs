using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.WinForms.Cameras;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen : Form
{
    private sealed record DemoItem(string Display, string Id);

    private sealed record WorldSetup(SimpleWorld World, Vector3 CameraPosition, PerspectiveProjection? Projection);

    private readonly Label lblLoading;

    public MainScreen()
    {
        InitializeComponent();

        lblLoading = new Label
        {
            Text = "Loading…",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            ForeColor = Color.Blue,
            BackColor = panel3D1.BackColor,
            Visible = false,
        };
        panel3D1.Controls.Add(lblLoading);

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
            new("Empty", "empty"),
        };

        lstDemos.DoubleClick += LstDemos_DoubleClick;

        rdbNoneShading.Checked = panel3D1.Painter is null;
        rdbClassicShading.Checked = panel3D1.Painter is ClassicPainter;
        rdbFlatShading.Checked = panel3D1.Painter is FlatPainter;
        rdbGouraudShading.Checked = panel3D1.Painter is GouraudPainter;

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

    private async Task PrepareWorldAsync(string id)
    {
        lstDemos.Enabled = false;
        lblLoading.Visible = true;
        lblLoading.BringToFront();
        UseWaitCursor = true;

        try
        {
            var setup = await Task.Run(() => BuildWorld(id));

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

    private static WorldSetup BuildWorld(string id)
    {
        var world = new SimpleWorld();
        var cameraPosition = new Vector3(0, 0, -60);
        PerspectiveProjection? projection = null;

        switch (id)
        {
            case "skull":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\skull.dae"));
                cameraPosition = new Vector3(0, 0, -5);
                break;

            case "parrot":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\parrot.dae"));
                cameraPosition = new Vector3(0, 0, -500);
                break;

            case "teapot":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\teapot.dae"));
                break;

            case "elefant":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\elefant.dae"));
                cameraPosition = new Vector3(0, 0, -1500);
                projection = new PerspectiveProjection(40f * (float)Math.PI / 180f, .01f, 65535f);
                break;

            case "Juliet":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\Juliet.dae"));
                cameraPosition = new Vector3(0, 0, -500);
                break;

            case "empty":
                break;

            case "town":
            {
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
                }
                break;
            }

            case "littletown":
            {
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
                }
                break;
            }

            case "bigtown":
            {
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
                }
                break;
            }

            case "cube":
                world.Meshes.Add(new Cube());
                break;

            case "bigcube":
                world.Meshes.Add(new Cube() { Scale = new Vector3(100, 100, 100) });
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
                }
                break;
            }
        }

        return new WorldSetup(world, cameraPosition, projection);
    }
}
