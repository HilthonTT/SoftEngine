using SoftEngine.Core.Baking;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Baking;

public class IrradianceBakerTests
{
    private sealed class FixedCamera(Vector3 position, Vector3 target) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, target, Vector3.UnitY);
    }

    private static Mesh Floor(float size, ColorRGB color)
    {
        var mesh = new Mesh(
            [
                new Vector3(-size, 0, -size),
                new Vector3(size, 0, -size),
                new Vector3(size, 0, size),
                new Vector3(-size, 0, size),
            ],
            [new Triangle(0, 1, 2), new Triangle(0, 2, 3)],
            [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY]);

        mesh.Material.Diffuse = color;
        mesh.Material.Roughness = 1f;
        mesh.Material.Metallic = 0f;

        return mesh;
    }

    private static SimpleWorld Lit(params IMesh[] meshes)
    {
        var world = new SimpleWorld();

        foreach (var mesh in meshes)
        {
            world.Meshes.Add(mesh);
        }

        world.Lights.Clear();
        world.Lights.Add(new DirectionalLight { Direction = -Vector3.UnitY, Intensity = 1f });

        return world;
    }

    [Fact]
    public void Bake_UnderAUniformSkyMeasuresTheSky()
    {
        var world = new SimpleWorld();
        world.Lights.Clear();

        LinearColor sky = new ColorRGB(128, 128, 128);

        var volume = IrradianceBaker.Bake(world, SkyBox.Uniform(new ColorRGB(128, 128, 128)), 1f,
            new BakeSettings { Resolution = 2, Rays = 64 });

        Assert.Equal(volume.Count, volume.ValidCount);

        for (var face = 0; face < 6; face++)
        {
            var measured = volume.Probe(0)[(CubeFace)face];

            Assert.Equal(sky.R, measured.R, 3);
        }
    }

    [Fact]
    public void Bake_TakesTheColourOfWhatTheLightBouncedOff()
    {
        var volume = IrradianceBaker.Bake(
            Lit(Floor(6f, new ColorRGB(230, 40, 40))),
            environment: null,
            skyIntensity: 0f,
            new BakeSettings { Resolution = 4, Rays = 96 });

        var point = new Vector3(0, 0.5f, 0);

        var fromBelow = volume.Evaluate(point, -Vector3.UnitY);
        var fromAbove = volume.Evaluate(point, Vector3.UnitY);

        Assert.True(fromBelow.R > 0.1f, $"a surface facing a lit floor should receive light, got {fromBelow.R}");
        Assert.True(fromBelow.R > 6f * fromBelow.G,
            $"the light off a red floor should be red: {fromBelow.R} red against {fromBelow.G} green");

        Assert.True(fromAbove.Luminance < 0.02f,
            $"there is nothing above the floor to be lit by, got {fromAbove.Luminance}");
    }

    [Fact]
    public void Bake_DropsProbesBuriedInGeometry()
    {
        var world = Lit(new Cube { Scale = new Vector3(4f, 4f, 4f) });

        var volume = IrradianceBaker.Bake(world, null, 0f, new BakeSettings { Resolution = 3, Rays = 32 });

        Assert.Equal(27, volume.Count);

        Assert.False(volume.IsValid(volume.IndexOf(1, 1, 1)), "the probe inside the cube should be dropped");
        Assert.True(volume.IsValid(volume.IndexOf(0, 0, 0)), "the probe outside the cube should be kept");
    }

    [Fact]
    public void Bake_IsReproducible()
    {
        static float[] Faces()
        {
            var volume = IrradianceBaker.Bake(
                Lit(Floor(6f, ColorRGB.White), new Cube { Position = new Vector3(0, 1f, 0) }),
                SkyBox.Gradient(-Vector3.UnitY),
                1f,
                new BakeSettings { Resolution = 3, Rays = 32 });

            var values = new List<float>();

            for (var probe = 0; probe < volume.Count; probe++)
            {
                for (var face = 0; face < 6; face++)
                {
                    values.Add(volume.Probe(probe)[(CubeFace)face].R);
                }
            }

            return [.. values];
        }

        Assert.Equal(Faces(), Faces());
    }

    [Fact]
    public void Bake_ScalesTheGridToTheShapeOfTheWorld()
    {
        var world = Lit(new Cube { Scale = new Vector3(40f, 4f, 4f) });

        var volume = IrradianceBaker.Bake(world, null, 0f, new BakeSettings { Resolution = 10, Rays = 8 });

        Assert.Equal(10, volume.CountX);
        Assert.True(volume.CountZ < volume.CountX, $"got {volume.CountX}×{volume.CountY}×{volume.CountZ}");
        Assert.True(volume.CountZ >= 2, "every axis needs two probes to interpolate between");
    }

    [Fact]
    public void Bake_OfAnEmptyWorldIsStillAVolume()
    {
        var world = new SimpleWorld();
        world.Lights.Clear();

        var volume = IrradianceBaker.Bake(world, null, 0f, new BakeSettings { Resolution = 2, Rays = 4 });

        Assert.True(volume.Count > 0);
        Assert.Equal(0f, volume.Evaluate(Vector3.Zero, Vector3.UnitY).Luminance, 5);
    }

    [Fact]
    public void Scene_LightsTheFrameWithTheBakeInsteadOfTheEnvironment()
    {
        static float Brightness(IrradianceVolume? volume)
        {
            var world = new SimpleWorld();
            world.Meshes.Add(Floor(20f, ColorRGB.White));
            world.Lights.Clear();
            world.Lights.Add(new DirectionalLight { Intensity = 0f });

            var scene = new Scene
            {
                World = world,
                Camera = new FixedCamera(new Vector3(0, 10f, 0.001f), Vector3.Zero),
                Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
                Surface = new FrameBuffer(24, 24) { Stats = new RenderStats() },
                GammaCorrect = true,
                HighDynamicRange = true,
                Environment = SkyBox.Uniform(ColorRGB.White),
                ShowSky = false,
                Irradiance = volume,
            };

            new Renderer().Render(scene, new PbrPainter(ambient: 0f));

            var hdr = scene.Surface.HdrColor;
            var pixels = scene.Surface.Width * scene.Surface.Height;

            var total = 0f;

            for (var i = 0; i < pixels; i++)
            {
                total += hdr[i * 3];
            }

            return total / pixels;
        }

        var dark = new IrradianceVolume(
            new Vector3(-100f), new Vector3(100f), 1, 1, 1,
            [new AmbientCube(LinearColor.Black)], [true], default);

        var fromEnvironment = Brightness(null);
        var fromBake = Brightness(dark);

        Assert.True(fromEnvironment > 0.05f, $"a white sky should light the floor, got {fromEnvironment}");
        Assert.True(fromBake < fromEnvironment * 0.05f,
            $"the bake should replace the environment's ambient, not join it: {fromEnvironment} → {fromBake}");
    }

    [Fact]
    public void Bake_AgreesWithWhatThePathTracerFindsAtTheSamePoint()
    {
        var albedo = new ColorRGB(230, 40, 40);

        var volume = IrradianceBaker.Bake(
            Lit(Floor(20f, albedo)),
            environment: null,
            skyIntensity: 0f,
            new BakeSettings { Resolution = 4, Rays = 256, Bounces = 0 });

        var above = volume.IndexOf(1, volume.CountY - 1, 1);

        Assert.True(volume.IsValid(above));
        Assert.True(volume.ProbePosition(above).Y > 0f, "the probe should be above the floor");

        var measured = volume.Probe(above)[CubeFace.NegativeY];

        LinearColor surface = albedo;

        Assert.True(measured.R > 0.6f * surface.R && measured.R < surface.R,
            $"expected a probe over the floor to see about {surface.R}, got {measured.R}");

        Assert.True(measured.R > 8f * measured.G, $"and to see it red, got {measured.R} against {measured.G}");
    }
}
