using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.WinForms.Demos;

internal static class GeometryDemos
{
    public static WorldSetup SingleCube(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.Add(new Cube());

        return new WorldSetup(world, DemoDefaults.CameraPosition, null);
    }

    public static WorldSetup BigCube(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.Add(new Cube() { Scale = new Vector3(100, 100, 100) });

        return new WorldSetup(world, DemoDefaults.CameraPosition, null);
    }

    public static WorldSetup TexturedCubeScene(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.Add(new TexturedCube
        {
            Scale = new Vector3(20, 20, 20),
            Rotation = new Rotation3D(25, 35, 0).ToRad(),
        });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });

        return new WorldSetup(world, DemoDefaults.CameraPosition, null);
    }

    public static WorldSetup LittleTown(IProgress<float>? progress) => BuildTown(10, progress);

    public static WorldSetup Town(IProgress<float>? progress) => BuildTown(50, progress);

    public static WorldSetup BigTown(IProgress<float>? progress) => BuildTown(200, progress);

    private static WorldSetup BuildTown(int extent, IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });

        var d = extent;
        var s = 2;

        for (var x = -d; x <= d; x += s)
        {
            for (var z = -d; z <= d; z += s)
            {
                world.Meshes.Add(new Cube()
                {
                    Position = new Vector3(x, 0, z),
                });
            }
            progress?.Report((x + d) / (float)(2 * d));
        }

        return new WorldSetup(world, DemoDefaults.CameraPosition, null);
    }

    public static WorldSetup Spheres(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

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

        return new WorldSetup(world, DemoDefaults.CameraPosition, null);
    }

    public static WorldSetup Cubes(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        var d = 20;
        var s = 2;
        var r = new Random();

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

        return new WorldSetup(world, DemoDefaults.CameraPosition, null);
    }
}
