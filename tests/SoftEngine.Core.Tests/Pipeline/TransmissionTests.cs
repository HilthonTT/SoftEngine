using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class TransmissionTests
{
    private const int Size = 64;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Scene Pane(float transmission, float thickness = 0f)
    {
        var world = new SimpleWorld();

        var wall = new Cube
        {
            Position = new Vector3(0f, 0f, 4f),
            Scale = new Vector3(8f, 8f, 0.2f),
        };

        wall.Material.Diffuse = new ColorRGB(220, 20, 20);
        wall.Material.Roughness = 1f;

        var glass = new Cube
        {
            Position = Vector3.Zero,
            Scale = new Vector3(2f, 2f, 0.1f),
        };

        glass.Material.Diffuse = ColorRGB.White;
        glass.Material.Roughness = 0f;
        glass.Material.Transmission = transmission;
        glass.Material.Thickness = thickness;

        world.Meshes.Add(wall);
        world.Meshes.Add(glass);
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(0f, -0.2f, 1f) });

        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0f, 0f, -8f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f),
            Surface = new FrameBuffer(Size, Size) { Stats = new RenderStats() },
            GammaCorrect = true,
            HighDynamicRange = true,
            ShowSky = false,
        };
    }

    private static ColorRGB Centre(Scene scene) =>
        ColorRGB.FromPacked(scene.Surface.GetColor(Size / 2, Size / 2));

    private static Scene Render(float transmission, float thickness = 0f)
    {
        var scene = Pane(transmission, thickness);

        new Renderer().Render(scene, new PbrPainter());

        return scene;
    }

    [Fact]
    public void AGlassPane_CarriesTheColourOfWhatIsBehindIt()
    {
        var opaque = Centre(Render(transmission: 0f));
        var glass = Centre(Render(transmission: 1f));

        Assert.True(glass.R > glass.B * 2, $"expected a red cast, got {glass.R}/{glass.G}/{glass.B}");
        Assert.True(glass.R - glass.B > opaque.R - opaque.B, "transmission did not tint the pane");
    }

    [Fact]
    public void WithoutTransmission_ThePaneStaysItsOwnColour()
    {
        var opaque = Centre(Render(transmission: 0f));

        Assert.True(System.Math.Abs(opaque.R - opaque.B) < 40, $"unexpected tint {opaque.R}/{opaque.G}/{opaque.B}");
    }

    [Fact]
    public void ATransmissiveMesh_HasABackdropCapturedForIt()
    {
        var scene = Pane(transmission: 1f);

        new Renderer().Render(scene, new PbrPainter());

        Assert.NotNull(scene.Backdrop);
        Assert.True(scene.Backdrop!.IsFilled);
    }

    [Fact]
    public void WithNoTransmissiveMesh_NoBackdropIsCaptured()
    {
        var scene = Pane(transmission: 0f);

        new Renderer().Render(scene, new PbrPainter());

        Assert.Null(scene.Backdrop);
    }

    [Fact]
    public void Thickness_StillFindsTheWallBehindThePane()
    {
        var straight = Centre(Render(transmission: 1f));
        var bent = Centre(Render(transmission: 1f, thickness: 3f));

        Assert.True(straight.R > 0);
        Assert.True(bent.R > 0);
    }
}
