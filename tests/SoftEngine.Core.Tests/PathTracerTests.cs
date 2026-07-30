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
using SoftEngine.Core.Tracing;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class PathTracerTests
{
    private sealed class FixedCamera(Vector3 position, Vector3 target) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, target, Vector3.UnitY);
    }

    /// <summary>
    /// A flat quad in the y = 0 plane, wide enough to fill the frame, with a chosen albedo.
    /// </summary>
    private static Mesh Floor(float size, ColorRGB color, float roughness = 1f)
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
        mesh.Material.Roughness = roughness;
        mesh.Material.Metallic = 0f;

        return mesh;
    }

    /// <summary>A camera looking straight down at the floor from above.</summary>
    private static Scene LookingDown(IWorld world, int size = 24)
    {
        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 10f, 0.001f), Vector3.Zero),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(size, size) { Stats = new RenderStats() },
            GammaCorrect = true,
            HighDynamicRange = true,
        };
    }

    /// <summary>The mean linear colour of the frame, read back out of the HDR target.</summary>
    private static LinearColor Mean(FrameBuffer surface)
    {
        float r = 0f, g = 0f, b = 0f;

        var pixels = surface.Width * surface.Height;
        var hdr = surface.HdrColor;

        for (var i = 0; i < pixels; i++)
        {
            r += hdr[i * 3];
            g += hdr[i * 3 + 1];
            b += hdr[i * 3 + 2];
        }

        return new LinearColor(r / pixels, g / pixels, b / pixels);
    }

    private static PathTracer Tracer(int samples = 8, int bounces = 0)
    {
        var tracer = new PathTracer();

        tracer.Trace.SamplesPerPixel = samples;
        tracer.Trace.MaxBounces = bounces;

        return tracer;
    }

    [Fact]
    public void Render_LightsASurfaceInProportionToTheLight()
    {
        static float Brightness(float intensity)
        {
            var world = new SimpleWorld();
            world.Meshes.Add(Floor(20f, ColorRGB.White));
            world.Lights.Clear();
            world.Lights.Add(new DirectionalLight { Direction = -Vector3.UnitY, Intensity = intensity });

            var scene = LookingDown(world);

            Tracer().Render(scene, null);

            return Mean(scene.Surface).Luminance;
        }

        var single = Brightness(1f);
        var doubled = Brightness(2f);

        Assert.True(single > 0.01f, $"a lit white floor should not be black, got {single}");

        // Radiance is linear in the light's intensity, and nothing in the path changes that.
        Assert.Equal(2f, doubled / single, 2);
    }

    [Fact]
    public void Render_ADirectionalLightAgreesWithTheAnalyticAnswer()
    {
        // A white Lambertian floor, lit straight down, seen straight down. Every term is known: the
        // BRDF is albedo/π, the cosine is 1, and the tracer scales direct light by π to match the
        // rasterizer's exposure — so the answer is the albedo times the intensity.
        var world = new SimpleWorld();
        world.Meshes.Add(Floor(40f, new ColorRGB(255, 255, 255)));
        world.Lights.Clear();
        world.Lights.Add(new DirectionalLight { Direction = -Vector3.UnitY, Intensity = 0.5f });

        var scene = LookingDown(world);

        var tracer = Tracer(samples: 4);
        tracer.Render(scene, null);

        var mean = Mean(scene.Surface);

        // Fresnel takes a few percent off the diffuse and puts it into a specular lobe that points
        // nowhere near the camera, so the answer is a little under the ideal 0.5.
        Assert.True(mean.R is > 0.42f and < 0.5f, $"expected ≈0.48, got {mean.R}");
    }

    [Fact]
    public void Render_ASurfaceFacingAwayFromTheLightIsUnlit()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(Floor(20f, ColorRGB.White));
        world.Lights.Clear();

        // Pointing up: the light travels away from the floor's front face.
        world.Lights.Add(new DirectionalLight { Direction = Vector3.UnitY, Intensity = 1f });

        var scene = LookingDown(world);
        Tracer().Render(scene, null);

        Assert.Equal(0f, Mean(scene.Surface).Luminance, 5);
    }

    [Fact]
    public void Render_CastsShadowsWithNoBiasToTune()
    {
        // The same frame with and without something between the floor and the light. A shadow map
        // would need a bias chosen for this scene's scale; a shadow ray needs nothing.
        static float Brightness(bool occluded)
        {
            var world = new SimpleWorld();
            world.Meshes.Add(Floor(20f, ColorRGB.White));

            if (occluded)
            {
                // A lid over the whole floor, so every pixel of it is shadowed rather than a patch
                // whose size the assertion would have to know.
                world.Meshes.Add(new Cube { Position = new Vector3(0, 3f, 0), Scale = new Vector3(60f, 0.5f, 60f) });
            }

            world.Lights.Clear();
            world.Lights.Add(new DirectionalLight { Direction = -Vector3.UnitY, Intensity = 1f });

            // From the side and low down, so the frame is mostly floor rather than the lid of the
            // slab that is doing the occluding.
            var scene = new Scene
            {
                World = world,
                Camera = new FixedCamera(new Vector3(0, 1.5f, 16f), new Vector3(0, 0.5f, 0)),
                Projection = new PerspectiveProjection(MathF.PI / 3f, 0.1f, 100f),
                Surface = new FrameBuffer(48, 48) { Stats = new RenderStats() },
                GammaCorrect = true,
                HighDynamicRange = true,
            };

            Tracer(samples: 4).Render(scene, null);

            return Mean(scene.Surface).Luminance;
        }

        var open = Brightness(occluded: false);
        var shadowed = Brightness(occluded: true);

        Assert.True(open > 0.2f, $"the open floor should be lit, got {open}");
        Assert.True(shadowed < open * 0.1f, $"shadowed {shadowed} should be well under open {open}");
    }

    [Fact]
    public void Render_TakesLightFromTheEnvironment()
    {
        // The sky as the only light: what the rasterizer approximates with an ambient cube and this
        // integrates properly. The light is present but switched off, because a world with no lights
        // at all gets the painters' default one — and that would be lighting the floor instead.
        var world = new SimpleWorld();
        world.Meshes.Add(Floor(40f, ColorRGB.White));
        world.Lights.Clear();
        world.Lights.Add(new DirectionalLight { Intensity = 0f });

        var scene = LookingDown(world);
        scene.Environment = SkyBox.Uniform(new ColorRGB(128, 128, 128));

        // One bounce, which is what it takes for a surface to see the sky at all.
        Tracer(samples: 64, bounces: 1).Render(scene, null);

        LinearColor sky = new ColorRGB(128, 128, 128);
        var mean = Mean(scene.Surface).R;

        // A white Lambertian surface under a uniform sky of radiance L reflects L, less the few
        // percent Fresnel keeps. The camera sees some sky directly too, at exactly L, so the whole
        // frame lands there.
        Assert.True(MathF.Abs(mean - sky.R) < 0.15f * sky.R, $"expected ≈{sky.R}, got {mean}");
    }

    [Fact]
    public void Render_WithoutTheEnvironmentTheBackgroundIsBlack()
    {
        var world = new SimpleWorld();
        world.Lights.Clear();

        var scene = LookingDown(world);
        scene.Environment = SkyBox.Uniform(ColorRGB.White);

        var tracer = Tracer();
        tracer.Trace.LightFromEnvironment = false;

        tracer.Render(scene, null);

        Assert.Equal(0f, Mean(scene.Surface).Luminance, 5);
    }

    [Fact]
    public void Render_IsReproducible()
    {
        static int[] Frame()
        {
            var world = new SimpleWorld();
            world.Meshes.Add(Floor(20f, ColorRGB.White));
            world.Meshes.Add(new Cube { Position = new Vector3(0, 1f, 0) });
            world.Lights.Clear();
            world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -1f, -0.3f) });

            var scene = LookingDown(world, 16);
            scene.Environment = SkyBox.Gradient(new Vector3(-0.4f, -1f, -0.3f));

            var tracer = Tracer(samples: 4, bounces: 2);
            tracer.Render(scene, null);

            return [.. scene.Surface.Screen];
        }

        // A stochastic renderer whose output depends on thread scheduling cannot be regression
        // tested at all, so the sampler is seeded per pixel rather than shared.
        Assert.Equal(Frame(), Frame());
    }

    [Fact]
    public void Render_IndirectLightReachesWhatDirectLightCannot()
    {
        // A white box open on one side, lit through the opening. The wall opposite the light gets
        // nothing directly; with a bounce it gets light reflected off the floor.
        static float Corner(int bounces)
        {
            var world = new SimpleWorld();

            world.Meshes.Add(Floor(6f, ColorRGB.White));

            // A vertical wall behind the floor's far edge, facing the camera.
            var wall = new Mesh(
                [
                    new Vector3(-6f, 0, -6f),
                    new Vector3(6f, 0, -6f),
                    new Vector3(6f, 6f, -6f),
                    new Vector3(-6f, 6f, -6f),
                ],
                [new Triangle(0, 1, 2), new Triangle(0, 2, 3)],
                [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);

            wall.Material.Diffuse = ColorRGB.White;

            world.Meshes.Add(wall);
            world.Lights.Clear();

            // Straight down: the floor is lit, the wall's own face is edge-on to it and gets none.
            world.Lights.Add(new DirectionalLight { Direction = -Vector3.UnitY, Intensity = 1f });

            var scene = new Scene
            {
                World = world,
                Camera = new FixedCamera(new Vector3(0, 3f, 10f), new Vector3(0, 3f, -6f)),
                Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
                Surface = new FrameBuffer(24, 24) { Stats = new RenderStats() },
                GammaCorrect = true,
                HighDynamicRange = true,
            };

            var tracer = Tracer(samples: 64, bounces: bounces);
            tracer.Render(scene, null);

            return Mean(scene.Surface).Luminance;
        }

        var direct = Corner(0);
        var bounced = Corner(2);

        Assert.True(bounced > direct * 1.2f,
            $"one bounce should brighten a wall the light cannot reach: {direct} → {bounced}");
    }

    [Fact]
    public void Render_AccumulatesAcrossCallsWhenAskedTo()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(Floor(20f, ColorRGB.White));
        world.Lights.Clear();
        world.Lights.Add(new DirectionalLight { Direction = -Vector3.UnitY });

        var scene = LookingDown(world, 16);

        var tracer = Tracer(samples: 4);
        tracer.Trace.Accumulate = true;

        tracer.Render(scene, null);
        Assert.Equal(4, tracer.AccumulatedSamples);

        tracer.Render(scene, null);
        Assert.Equal(8, tracer.AccumulatedSamples);

        // Moving something invalidates what was accumulated against where it used to be.
        world.Meshes[0].Position = new Vector3(0, 0.5f, 0);
        tracer.Render(scene, null);
        Assert.Equal(4, tracer.AccumulatedSamples);

        tracer.Reset();
        Assert.Equal(0, tracer.AccumulatedSamples);
    }

    [Fact]
    public void Render_FillsTheDepthBufferItNeverProjectedInto()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Scale = new Vector3(3f, 3f, 3f) });
        world.Lights.Clear();
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(0, -1f, -1f) });

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, 10f), Vector3.Zero),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(32, 32) { Stats = new RenderStats() },
            GammaCorrect = true,
        };

        Tracer(samples: 2).Render(scene, null);

        // The cube is nearer than the background, so the depth view and the depth-reading post
        // effects have something to work with even though no triangle was ever projected.
        Assert.False(scene.Surface.IsBackground(16, 16));
        Assert.True(scene.Surface.IsBackground(0, 0));
    }

    [Fact]
    public void Render_AgreesWithTheRasterizerOnDirectLight()
    {
        // The comparison the whole renderer exists for: one bounce of nothing, no environment, a
        // single directional light — the case the rasterizer computes exactly rather than
        // approximating. The two should land on the same brightness.
        static LinearColor Frame(IRenderer renderer, Rasterization.IPainter? painter)
        {
            var world = new SimpleWorld();
            world.Meshes.Add(Floor(40f, new ColorRGB(200, 160, 120)));
            world.Lights.Clear();
            world.Lights.Add(new DirectionalLight { Direction = -Vector3.UnitY, Intensity = 0.8f });

            var scene = LookingDown(world, 32);
            scene.AmbientFromEnvironment = false;

            renderer.Render(scene, painter);

            return Mean(scene.Surface);
        }

        var traced = Frame(Tracer(samples: 4), null);

        // The PBR painter with no environment and no ambient is the closest thing the rasterizer
        // has to "direct light only".
        var rasterized = Frame(new Renderer(), new PbrPainter(ambient: 0f));

        Assert.True(MathF.Abs(traced.R - rasterized.R) < 0.1f * rasterized.R,
            $"traced {traced.R} vs rasterized {rasterized.R}");
    }
}
