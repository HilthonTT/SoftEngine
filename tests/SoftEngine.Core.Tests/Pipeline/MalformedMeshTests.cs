using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class MalformedMeshTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    public static TheoryData<string> PainterNames =>
        ["classic", "flat", "gouraud", "phong", "textured", "material", "pbr"];

    private static IPainter Painter(string name) => name switch
    {
        "classic" => new ClassicPainter(),
        "flat" => new FlatPainter(),
        "phong" => new PhongPainter(),
        "textured" => new TexturedPainter(),
        "material" => new MaterialPainter(),
        "pbr" => new PbrPainter(),
        _ => new GouraudPainter(),
    };

    private static Mesh CubeWithShortNormals(int normalCount)
    {
        var source = new Cube();
        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, new ColorRGB(200, 120, 80));

        return new Mesh(
            (Vector3[])source.Vertices.Clone(),
            source.Triangles,
            new Vector3[normalCount],
            colors)
        {
            TexCoords = new Vector2[source.Vertices.Length],
        };
    }

    private static Scene SceneOf(Renderer renderer, IMesh mesh) => new()
    {
        Surface = new FrameBuffer(160, 120) { Stats = renderer.Stats },
        Camera = new FixedCamera(new Vector3(0f, 0f, 5f)),
        Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
        World = new SimpleWorld
        {
            Meshes = [mesh],
            Lights = [new DirectionalLight { Direction = Vector3.Normalize(new Vector3(-0.3f, -0.6f, -0.7f)) }],
        },
    };

    [Theory]
    [MemberData(nameof(PainterNames))]
    public void MeshWithNoNormals_Renders(string painter)
    {
        var renderer = new Renderer();

        renderer.Render(SceneOf(renderer, CubeWithShortNormals(0)), Painter(painter));

        Assert.True(renderer.Stats.DrawnTriangleCount > 0);
    }

    [Theory]
    [MemberData(nameof(PainterNames))]
    public void MeshWithTooFewNormals_Renders(string painter)
    {
        var renderer = new Renderer();

        renderer.Render(SceneOf(renderer, CubeWithShortNormals(3)), Painter(painter));

        Assert.True(renderer.Stats.DrawnTriangleCount > 0);
    }
}
