using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.WinForms.Demos;

internal static class ShowcaseDemos
{
    public static WorldSetup Primitives(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.8f, 0.4f) });

        var checker = Texture.Checkerboard(256, 8, new ColorRGB(225, 225, 230), new ColorRGB(98, 88, 158));

        world.Meshes.Add(new PlaneMesh(48f, 48f, 8, 8, uvScale: 12f)
        {
            Position = new Vector3(0, -3f, 0),
            Texture = checker,
        });

        world.Meshes.Add(new UvSphere(1.6f) { Position = new Vector3(-7.5f, -1.4f, 0), Texture = checker });
        world.Meshes.Add(new Cylinder(1.4f, 3.2f) { Position = new Vector3(-2.5f, -1.4f, 0), Texture = checker });
        world.Meshes.Add(new Cone(1.5f, 3.2f) { Position = new Vector3(2.5f, -1.4f, 0), Texture = checker });
        world.Meshes.Add(new Torus(1.5f, 0.5f)
        {
            Position = new Vector3(7.5f, -1f, 0),
            Rotation = new Rotation3D(70, 0, 0).ToRad(),
            Texture = checker,
        });

        return new WorldSetup(world, new Vector3(0, 2f, -16f), null);
    }

    public static WorldSetup Transparency(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.7f, -1f) });

        var floor = new Cube { Position = new Vector3(0, -3.5f, 0), Scale = new Vector3(14, 0.5f, 14) };
        Array.Fill(floor.TriangleColors, ColorRGB.Gray);
        world.Meshes.Add(floor);

        var solid = new IcoSphere(2) { Position = new Vector3(0, 0, 2.5f), Scale = new Vector3(1.5f, 1.5f, 1.5f) };
        Array.Fill(solid.TriangleColors, new ColorRGB(220, 60, 50));
        world.Meshes.Add(solid);

        var glass = new IcoSphere(2) { Position = new Vector3(-1.8f, 0, 0), Scale = new Vector3(2, 2, 2), Opacity = 0.55f };
        Array.Fill(glass.TriangleColors, new ColorRGB(70, 200, 120));
        world.Meshes.Add(glass);

        var mist = new IcoSphere(2) { Position = new Vector3(1.8f, 0, -1f), Scale = new Vector3(2, 2, 2), Opacity = 0.35f };
        Array.Fill(mist.TriangleColors, new ColorRGB(80, 140, 255));
        world.Meshes.Add(mist);

        return new WorldSetup(world, new Vector3(0, 0, -12), null);
    }

    public static WorldSetup Shadows(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.3f, -1f, 0.35f) });

        var ground = new Cube { Position = new Vector3(0, -4f, 0), Scale = new Vector3(26, 0.5f, 26) };
        Array.Fill(ground.TriangleColors, new ColorRGB(190, 188, 182));
        world.Meshes.Add(ground);

        var pillar = new Cube { Position = new Vector3(-5.5f, -1.2f, -1f), Scale = new Vector3(1.4f, 5f, 1.4f) };
        Array.Fill(pillar.TriangleColors, new ColorRGB(150, 120, 90));
        world.Meshes.Add(pillar);

        var beam = new Cube
        {
            Position = new Vector3(1f, 3f, -1f),
            Scale = new Vector3(9f, 0.5f, 0.6f),
            Rotation = new Rotation3D(0, 0, 10).ToRad(),
        };
        Array.Fill(beam.TriangleColors, new ColorRGB(150, 120, 90));
        world.Meshes.Add(beam);

        var ball = new IcoSphere(3) { Position = new Vector3(2f, 0.2f, 3f), Scale = new Vector3(1.8f, 1.8f, 1.8f) };
        Array.Fill(ball.TriangleColors, new ColorRGB(200, 70, 60));
        world.Meshes.Add(ball);

        var small = new IcoSphere(3) { Position = new Vector3(-2f, -0.8f, 1.5f) };
        Array.Fill(small.TriangleColors, new ColorRGB(70, 150, 210));
        world.Meshes.Add(small);

        return new WorldSetup(world, new Vector3(0, 0, -24), null);
    }

    public static WorldSetup CascadedShadows(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -1f, -0.15f) });

        world.Meshes.Add(ColoredBox(
            new Vector3(0, -6f, -150f),
            new Vector3(60f, 1f, 340f),
            new ColorRGB(190, 188, 182)));

        for (var i = 0; i < 24; i++)
        {
            var z = -8f - i * 13f;

            world.Meshes.Add(ColoredBox(
                new Vector3(-9f, -1.5f, z),
                new Vector3(2.4f, 8f, 2.4f),
                new ColorRGB(150, 120, 90)));

            world.Meshes.Add(ColoredBox(
                new Vector3(9f, -1.5f, z),
                new Vector3(2.4f, 8f, 2.4f),
                new ColorRGB(150, 120, 90)));

            world.Meshes.Add(ColoredBox(
                new Vector3(0f, 3f, z),
                new Vector3(22f, 1f, 1.6f),
                new ColorRGB(170, 140, 110)));
        }

        return new WorldSetup(world, new Vector3(0, -1f, 16f), null);
    }

    public static WorldSetup NormalMapping(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.35f, -1f) });

        var albedo = Texture.Checkerboard(256, 8, new ColorRGB(210, 205, 195), new ColorRGB(150, 145, 138));
        var normals = NormalMapBuilder.FromHeight(Texture.Bumps(256, 8), 3f);

        var bumpy = new TexturedCube
        {
            Position = new Vector3(-1.2f, 0, 0),
            Scale = new Vector3(18, 18, 18),
            Rotation = new Rotation3D(20, 30, 0).ToRad(),
        };
        bumpy.Material.DiffuseMap = albedo;
        bumpy.Material.NormalMap = normals;
        bumpy.Material.SpecularStrength = 0.5f;
        world.Meshes.Add(bumpy);

        var flat = new TexturedCube
        {
            Position = new Vector3(24f, 0, 0),
            Scale = new Vector3(18, 18, 18),
            Rotation = new Rotation3D(20, 30, 0).ToRad(),
        };
        flat.Material.DiffuseMap = albedo;
        flat.Material.SpecularStrength = 0.5f;
        world.Meshes.Add(flat);

        return new WorldSetup(world, new Vector3(-11f, 0, -70), null);
    }

    public static WorldSetup PbrSpheres(IProgress<float>? progress)
    {
        const int columns = 6;
        const int rows = 3;
        const float spacing = 2.6f;

        var world = new SimpleWorld();

        var albedo = new ColorRGB(222, 180, 140);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var sphere = new IcoSphere(3)
                {
                    Position = new Vector3(
                        (column - (columns - 1) / 2f) * spacing,
                        (row - (rows - 1) / 2f) * spacing,
                        0f),
                };

                sphere.Material.Diffuse = albedo;
                sphere.Material.Metallic = rows == 1 ? 0f : row / (float)(rows - 1);

                sphere.Material.Roughness = 0.06f + 0.94f * column / (columns - 1);

                world.Meshes.Add(sphere);
            }
        }

        world.Lights.Add(new DirectionalLight
        {
            Direction = new Vector3(-0.4f, -0.5f, 1f),
            Color = new ColorRGB(255, 244, 224),
        });
        world.Lights.Add(new PointLight
        {
            Position = new Vector3(-14f, 6f, -14f),
            Color = new ColorRGB(150, 185, 255),
            Intensity = 0.5f,
        });

        return new WorldSetup(world, new Vector3(0, 0, -24f), null);
    }

    private static Mesh ColoredBox(Vector3 position, Vector3 scale, ColorRGB color)
    {
        var source = new Cube();

        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, color);

        return new Mesh(source.Vertices, source.Triangles, source.NormVertices, colors)
        {
            Position = position,
            Scale = scale,
        };
    }
}
