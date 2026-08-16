using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class SkyTests
{
    private sealed class FixedCamera(Vector3 position, Vector3 target) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, target, Vector3.UnitY);
    }

    /// <summary>A cube map whose every face is one flat colour, so a sample is unambiguous.</summary>
    private static CubeMap Faces(
        ColorRGB positiveX, ColorRGB negativeX,
        ColorRGB positiveY, ColorRGB negativeY,
        ColorRGB positiveZ, ColorRGB negativeZ)
    {
        static Texture Flat(ColorRGB color) => new(2, 2, [.. Enumerable.Repeat(color.Color, 4)]);

        return new CubeMap([Flat(positiveX), Flat(negativeX), Flat(positiveY), Flat(negativeY), Flat(positiveZ), Flat(negativeZ)])
        {
            Filtering = TextureFiltering.Nearest,
        };
    }

    private static CubeMap AxisColored() => Faces(
        ColorRGB.Red, new ColorRGB(64, 0, 0),
        ColorRGB.Green, new ColorRGB(0, 64, 0),
        ColorRGB.Blue, new ColorRGB(0, 0, 64));

    [Fact]
    public void Sample_PicksTheFaceTheDirectionPointsAt()
    {
        var map = AxisColored();

        Assert.Equal(ColorRGB.Red.Color, map.Sample(Vector3.UnitX).Color);
        Assert.Equal(ColorRGB.Green.Color, map.Sample(Vector3.UnitY).Color);
        Assert.Equal(ColorRGB.Blue.Color, map.Sample(Vector3.UnitZ).Color);
        Assert.Equal(new ColorRGB(64, 0, 0).Color, map.Sample(-Vector3.UnitX).Color);
        Assert.Equal(new ColorRGB(0, 64, 0).Color, map.Sample(-Vector3.UnitY).Color);
        Assert.Equal(new ColorRGB(0, 0, 64).Color, map.Sample(-Vector3.UnitZ).Color);
    }

    [Fact]
    public void ProjectAndDirection_AreInverses()
    {
        for (var f = 0; f < 6; f++)
        {
            foreach (var u in new[] { 0.1f, 0.5f, 0.9f })
            {
                foreach (var v in new[] { 0.2f, 0.5f, 0.75f })
                {
                    var direction = CubeMap.Direction((CubeFace)f, u, v);
                    var (face, backU, backV) = CubeMap.Project(direction);

                    Assert.Equal((CubeFace)f, face);
                    Assert.Equal(u, backU, 4);
                    Assert.Equal(v, backV, 4);
                }
            }
        }
    }

    [Fact]
    public void AmbientCube_FromAUniformEnvironment_IsThatConstant()
    {
        var grey = new ColorRGB(128, 128, 128);
        var ambient = AmbientCube.FromEnvironment(SkyBox.Uniform(grey));

        LinearColor expected = grey;

        foreach (var normal in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.Normalize(new Vector3(1, 1, 1)) })
        {
            Assert.Equal(expected.R, ambient.Evaluate(normal).R, 4);
        }
    }

    [Fact]
    public void AmbientCube_LightsSurfacesByWhichWayTheyFace()
    {
        var ambient = AmbientCube.FromEnvironment(AxisColored());

        var up = ambient.Evaluate(Vector3.UnitY);
        var down = ambient.Evaluate(-Vector3.UnitY);

        // Bright green above, dark green below.
        Assert.True(up.G > down.G);
        Assert.True(up.G > up.R);
        Assert.True(ambient.Evaluate(Vector3.UnitX).R > up.R);
    }

    [Fact]
    public void AmbientCube_Weights_SumToOne()
    {
        // A uniform white environment must evaluate to exactly its constant in every
        // direction, which only holds if the three squared components sum to 1.
        var ambient = AmbientCube.FromEnvironment(SkyBox.Uniform(ColorRGB.White));

        foreach (var normal in new[]
        {
            Vector3.Normalize(new Vector3(1, 2, 3)),
            Vector3.Normalize(new Vector3(-4, 1, -0.5f)),
            Vector3.Normalize(new Vector3(0.1f, -0.2f, 0.9f)),
        })
        {
            Assert.Equal(1f, ambient.Evaluate(normal).R, 4);
        }
    }

    [Fact]
    public void Gradient_IsBrighterAboveTheHorizonThanBelow()
    {
        var sky = SkyBox.Gradient(new Vector3(0, -1, 0));

        LinearColor above = sky.Sample(new Vector3(0, 1, 0));
        LinearColor below = sky.Sample(new Vector3(0, -1, 0));

        Assert.True(above.Luminance > below.Luminance);
    }

    [Fact]
    public void Gradient_PutsTheSunWhereTheLightComesFrom()
    {
        var sunTravels = Vector3.Normalize(new Vector3(-0.4f, -0.8f, -0.3f));
        var sky = SkyBox.Gradient(sunTravels, sunAngularSize: 0.15f, resolution: 128);

        LinearColor towardSun = sky.Sample(-sunTravels);
        LinearColor away = sky.Sample(new Vector3(-sunTravels.X, sunTravels.Y, -sunTravels.Z));

        Assert.True(towardSun.Luminance > away.Luminance);
    }

    private static Scene SceneWithSky(CubeMap sky, params IMesh[] meshes)
    {
        var world = new SimpleWorld();
        world.Meshes.AddRange(meshes);

        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, 10f), Vector3.Zero),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(64, 64) { Stats = new RenderStats() },
            Environment = sky,
            GammaCorrect = true,
        };
    }

    [Fact]
    public void Render_FillsTheBackgroundAndLeavesGeometryAlone()
    {
        var scene = SceneWithSky(AxisColored(), new Cube { Scale = new Vector3(3, 3, 3) });

        new Renderer().Render(scene, new ClassicPainter());

        // The camera sits on +Z looking at the origin, so the background behind it is the
        // -Z face... and the pixel at the centre is the cube, which the sky must not touch.
        var corner = ColorRGB.FromPacked(scene.Surface.GetColor(1, 1));
        var centre = ColorRGB.FromPacked(scene.Surface.GetColor(32, 32));

        Assert.NotEqual(0, corner.Color);
        Assert.Equal(new ColorRGB(0, 0, 64).Color, corner.Color);
        Assert.NotEqual(corner.Color, centre.Color);
    }

    [Fact]
    public void Render_WithShowSkyOff_LeavesTheBackgroundCleared()
    {
        var scene = SceneWithSky(AxisColored());
        scene.ShowSky = false;

        new Renderer().Render(scene, new ClassicPainter());

        Assert.Equal(0, scene.Surface.GetColor(1, 1));
    }

    [Fact]
    public void Render_SkyDoesNotOverwriteTransparentGeometry()
    {
        // A pane of glass with nothing but sky behind it. The sky has to land behind the
        // blend, not on top of it — transparent geometry never writes depth, so the pixel
        // still looks like background to anything that only asks the depth buffer.
        var glass = new Cube { Scale = new Vector3(2, 2, 0.1f), Opacity = 0.5f };
        var scene = SceneWithSky(SkyBox.Uniform(ColorRGB.Red), glass);

        new Renderer().Render(scene, new ClassicPainter());

        var centre = ColorRGB.FromPacked(scene.Surface.GetColor(32, 32));

        Assert.NotEqual(ColorRGB.Red.Color, centre.Color);
        Assert.True(centre.R > 0);
    }

    [Fact]
    public void AmbientFromEnvironment_LightsTheSceneWithoutDrawingIt()
    {
        static ColorRGB Centre(bool fromEnvironment)
        {
            var scene = SceneWithSky(SkyBox.Uniform(ColorRGB.White), new Cube { Scale = new Vector3(3, 3, 3) });
            scene.ShowSky = false;
            scene.AmbientFromEnvironment = fromEnvironment;
            scene.AmbientIntensity = 0.8f;
            scene.World.Lights.Clear();

            new Renderer().Render(scene, new GouraudPainter(ambient: 0.05f));

            return ColorRGB.FromPacked(scene.Surface.GetColor(32, 32));
        }

        // A bright white environment delivers far more ambient than the painter's 0.05.
        Assert.True(Centre(fromEnvironment: true).R > Centre(fromEnvironment: false).R);
    }
}
