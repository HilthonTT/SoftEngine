using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Scenes;

public class OrthographicProjectionTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    [Fact]
    public void ProjectionMatrix_MapsNearAndFarToTheUnitDepthRange()
    {
        var projection = new OrthographicProjection(10f, 1f, 100f);
        var matrix = projection.ProjectionMatrix(200f, 100f);

        var near = Vector4.Transform(new Vector3(0, 0, -1f), matrix);
        var far = Vector4.Transform(new Vector3(0, 0, -100f), matrix);

        Assert.Equal(0f, near.Z, 4);
        Assert.Equal(1f, far.Z, 4);
        Assert.Equal(1f, near.W, 5);
        Assert.Equal(1f, far.W, 5);
    }

    [Fact]
    public void ProjectionMatrix_WidensTheViewVolumeWithTheAspectRatio()
    {
        var projection = new OrthographicProjection(10f, 1f, 100f);
        var matrix = projection.ProjectionMatrix(200f, 100f);

        var right = Vector4.Transform(new Vector3(10f, 5f, -10f), matrix);

        Assert.Equal(1f, right.X, 4);
        Assert.Equal(1f, right.Y, 4);
    }

    [Fact]
    public void IsOrthographic_SeparatesTheTwoProjections()
    {
        Assert.True(((IProjection)new OrthographicProjection(10f, 1f, 100f)).IsOrthographic);

        Assert.False(((IProjection)new PerspectiveProjection(1f, 1f, 100f)).IsOrthographic);
    }

    [Fact]
    public void SetLinearDepthRange_TakesTheProjectedZAsDeviceDepth()
    {
        var surface = new FrameBuffer(16, 16);
        surface.SetLinearDepthRange();

        var middle = surface.ToScreen3(new Vector4(0, 0, 0.5f, 1f));

        Assert.Equal(FrameBuffer.DepthResolution * 0.5f, middle.Z, 0.5f);
    }

    [Fact]
    public void SetDepthRange_AfterLinear_RestoresThePerspectiveMapping()
    {
        var surface = new FrameBuffer(16, 16);
        surface.SetLinearDepthRange();
        surface.SetDepthRange(1f, 101f);

        var atNear = surface.ToScreen3(new Vector4(0, 0, 0f, 1f));

        Assert.Equal(0f, atNear.Z, 1f);
    }

    [Fact]
    public void Render_WithAnOrthographicProjection_DrawsAndDepthTests()
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(128, 128) { Stats = renderer.Stats };

        var near = new Cube { Position = new Vector3(0, 0, 2f) };
        var far = new Cube { Position = new Vector3(0, 0, -2f), Scale = new Vector3(2, 2, 2) };

        var scene = new Scene
        {
            Surface = surface,
            Camera = new FixedCamera(new Vector3(0, 0, 10f)),
            Projection = new OrthographicProjection(8f, 0.1f, 50f),
            World = new SimpleWorld { Meshes = [far, near], Lights = [] },
        };

        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.DrawnPixelCount > 0);

        var centreDepth = surface.GetDepth(64, 64);
        Assert.True(centreDepth < FrameBuffer.DepthResolution);

        var edgeDepth = surface.GetDepth(64, 110);
        Assert.True(edgeDepth > centreDepth);
    }
}
