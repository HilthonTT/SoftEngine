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

    public MainScreen()
    {
        InitializeComponent();

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

        PrepareWorld("skull");
    }

    private void LstDemos_DoubleClick(object? sender, EventArgs e)
    {
        if (lstDemos.SelectedValue is string id)
        {
            PrepareWorld(id);
        }
    }

    private void PrepareWorld(string id)
    {
        var world = new SimpleWorld();

        panel3D1.Scene?.Camera.Position = new Vector3(0, 0, -60);

        switch (id)
        {
            case "skull":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\skull.dae"));
                panel3D1.Scene?.Camera.Position = new Vector3(0, 0, -5);
                break;

            case "parrot":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\parrot.dae"));
                panel3D1.Scene?.Camera.Position = new Vector3(0, 0, -500);
                break;

            case "teapot":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\teapot.dae"));
                break;

            case "elefant":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\elefant.dae"));
                panel3D1.Scene?.Camera.Position = new Vector3(0, 0, -1500);
                panel3D1.Scene?.Projection = new PerspectiveProjection(40f * (float)Math.PI / 180f, .01f, 65535f);
                break;

            case "Juliet":
                world.Meshes.AddRange(MeshFactory.HackyImportCollada(@"models\Juliet.dae"));
                panel3D1.Scene?.Camera.Position = new Vector3(0, 0, -500);
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
                var d = 5; var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var y = -d; y <= d; y += s)
                    {
                        for (var z = -d; z <= d; z += s)
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

        panel3D1.Scene?.World = world;

        panel3D1.Invalidate();
    }
}
