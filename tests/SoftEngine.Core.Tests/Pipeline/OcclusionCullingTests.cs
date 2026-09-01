using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Tests.Golden;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class OcclusionCullingTests
{
    private const int Width = 200;
    private const int Height = 150;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Mesh Wall(float half, float z, ColorRGB color)
    {
        Vector3[] vertices =
        [
            new(-half, -half, z),
            new(half, -half, z),
            new(half, half, z),
            new(-half, half, z),
        ];

        Triangle[] triangles = [new(0, 1, 2), new(0, 2, 3)];

        var normal = Vector3.UnitZ;

        return new Mesh(vertices, triangles, [normal, normal, normal, normal], [color, color]);
    }

    private static Mesh SmallCube(Vector3 position, float scale = 0.2f)
    {
        var source = new Cube();
        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, new ColorRGB(220, 90, 70));

        return new Mesh(
            (Vector3[])source.Vertices.Clone(),
            source.Triangles,
            (Vector3[])source.NormVertices.Clone(),
            colors)
        {
            Position = position,
            Scale = new Vector3(scale),
        };
    }

    private static (Renderer Renderer, Scene Scene) Build(params IMesh[] meshes)
    {
        var renderer = new Renderer();
        renderer.Settings.BackFaceCulling = true;
        renderer.Diagnostics.Events.IsEnabled = false;

        renderer.Occlusion.MinimumTestableMeshes = 1;

        var scene = new Scene
        {
            Surface = new FrameBuffer(Width, Height) { Stats = renderer.Stats },
            Camera = new FixedCamera(new Vector3(0f, 0f, 5f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld
            {
                Meshes = [.. meshes],
                Lights = [new DirectionalLight { Direction = Vector3.Normalize(new Vector3(-0.3f, -0.6f, -0.7f)) }],
            },
        };

        return (renderer, scene);
    }

    [Fact]
    public void MeshBehindAWall_IsRejectedBeforeItsVerticesAreTransformed()
    {
        var (renderer, scene) = Build(Wall(3f, 0f, ColorRGB.Gray), SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(1, renderer.Stats.OccludedMeshCount);
        Assert.Equal(1, renderer.Stats.OccluderMeshCount);
        Assert.True(renderer.Stats.OccludedMeshTriangleCount > 0);

        Assert.Equal(0, renderer.Stats.FacingBackTriangleCount);
    }

    [Fact]
    public void MeshInFrontOfTheWall_IsDrawn()
    {
        var (renderer, scene) = Build(Wall(3f, 0f, ColorRGB.Gray), SmallCube(new Vector3(0f, 0f, 2f)));

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
        Assert.True(renderer.Stats.DrawnTriangleCount > 2);
    }

    [Fact]
    public void MeshPeekingPastTheWallsEdge_IsDrawn()
    {
        var (renderer, scene) = Build(Wall(1.2f, 0f, ColorRGB.Gray), SmallCube(new Vector3(1.7f, 0f, -2f)));

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(1, renderer.Stats.OccluderMeshCount);
        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
    }

    [Fact]
    public void Disabled_RejectsNothing()
    {
        var (renderer, scene) = Build(Wall(3f, 0f, ColorRGB.Gray), SmallCube(new Vector3(0f, 0f, -2f)));
        renderer.Settings.OcclusionCulling = false;

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
        Assert.Equal(0, renderer.Stats.OccluderMeshCount);
    }

    [Fact]
    public void TransparentWall_OccludesNothing()
    {
        var wall = Wall(3f, 0f, ColorRGB.Gray);
        wall.Opacity = 0.5f;

        var (renderer, scene) = Build(wall, SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccluderMeshCount);
        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
    }

    [Fact]
    public void HiddenWall_OccludesNothing()
    {
        var wall = Wall(3f, 0f, ColorRGB.Gray);
        wall.Visible = false;

        var (renderer, scene) = Build(wall, SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccluderMeshCount);
        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
    }

    [Fact]
    public void ProbedFrame_RunsWithoutThePass()
    {
        var (renderer, scene) = Build(Wall(3f, 0f, ColorRGB.Gray), SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Diagnostics.SetProbe(Width / 2, Height / 2);

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
        Assert.Equal(0, renderer.Stats.OccluderMeshCount);
    }

    [Fact]
    public void CullingOnAndOff_ProduceTheSameFrame()
    {
        static (Renderer Renderer, Scene Scene) Compose()
        {
            var meshes = new List<IMesh> { Wall(3f, 0f, new ColorRGB(150, 155, 165)) };

            for (var i = 0; i < 12; i++)
            {
                var angle = i * MathF.Tau / 12f;

                meshes.Add(SmallCube(new Vector3(MathF.Cos(angle) * 1.4f, MathF.Sin(angle) * 1.4f, -2f - i * 0.2f)));
            }

            for (var i = 0; i < 4; i++)
            {
                meshes.Add(SmallCube(new Vector3(-3.4f + i * 0.35f, 1.2f, 1.5f)));
            }

            return Build([.. meshes]);
        }

        var (culling, cullingScene) = Compose();
        culling.Settings.OcclusionCulling = true;
        culling.Render(cullingScene, new PhongPainter());

        var (plain, plainScene) = Compose();
        plain.Settings.OcclusionCulling = false;
        plain.Render(plainScene, new PhongPainter());

        Assert.True(culling.Stats.OccludedMeshCount > 0, "the scene was meant to have hidden meshes in it");
        Assert.Equal(0, plain.Stats.OccludedMeshCount);

        GoldenImage.VerifyIdentical(
            "occlusion culling",
            plainScene.Surface.Screen,
            cullingScene.Surface.Screen,
            Width,
            Height);
    }

    [Fact]
    public void SceneWithNoOccluderBigEnough_LeavesEveryMeshAlone()
    {
        var meshes = new List<IMesh>();

        for (var i = 0; i < 6; i++)
        {
            meshes.Add(SmallCube(new Vector3(-1.5f + i * 0.6f, 0f, -i * 0.4f)));
        }

        var (renderer, scene) = Build([.. meshes]);

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccluderMeshCount);
        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
        Assert.True(renderer.Stats.DrawnTriangleCount > 0);
    }
}

public class OcclusionBufferTests
{
    private static void AddQuad(OcclusionBuffer buffer, float ndcHalf, float depth)
    {
        Vector4 a = new(-ndcHalf, -ndcHalf, depth, 1f);
        Vector4 b = new(ndcHalf, -ndcHalf, depth, 1f);
        Vector4 c = new(ndcHalf, ndcHalf, depth, 1f);
        Vector4 d = new(-ndcHalf, ndcHalf, depth, 1f);

        buffer.AddTriangle(a, b, c);
        buffer.AddTriangle(a, c, d);
    }

    [Fact]
    public void ClearedBuffer_HidesNothing()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(64, 64);
        buffer.Clear();
        buffer.Build();

        Assert.False(buffer.HasOccluders);
        Assert.False(buffer.IsHidden(-0.5f, -0.5f, 0.5f, 0.5f, 0.99f));
    }

    [Fact]
    public void RectangleBehindTheOccluder_IsHidden()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(64, 64);
        buffer.Clear();

        AddQuad(buffer, 0.9f, 0.3f);
        buffer.Build();

        Assert.True(buffer.HasOccluders);
        Assert.True(buffer.IsHidden(-0.4f, -0.4f, 0.4f, 0.4f, 0.5f));
    }

    [Fact]
    public void RectangleInFrontOfTheOccluder_IsNotHidden()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(64, 64);
        buffer.Clear();

        AddQuad(buffer, 0.9f, 0.5f);
        buffer.Build();

        Assert.False(buffer.IsHidden(-0.4f, -0.4f, 0.4f, 0.4f, 0.3f));
    }

    [Fact]
    public void RectangleReachingPastTheOccluder_IsNotHidden()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(64, 64);
        buffer.Clear();

        AddQuad(buffer, 0.4f, 0.3f);
        buffer.Build();

        Assert.True(buffer.IsHidden(-0.2f, -0.2f, 0.2f, 0.2f, 0.9f));
        Assert.False(buffer.IsHidden(-0.2f, -0.2f, 0.7f, 0.2f, 0.9f));
    }

    [Fact]
    public void RectangleLeavingTheFrame_IsNotHidden()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(64, 64);
        buffer.Clear();

        AddQuad(buffer, 1f, 0.3f);
        buffer.Build();

        Assert.False(buffer.IsHidden(-1.4f, -0.2f, -0.6f, 0.2f, 0.9f));
    }

    [Fact]
    public void OccluderTooSmallToFillAQueryableTexel_HidesNothing()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(64, 64);
        buffer.Clear();

        AddQuad(buffer, 0.02f, 0.3f);
        buffer.Build();

        Assert.False(buffer.IsHidden(-0.02f, -0.02f, 0.02f, 0.02f, 0.9f));
    }

    [Fact]
    public void SlopedOccluder_StoresItsFarthestDepthPerTexel()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(16, 16);
        buffer.Clear();

        Vector4 a = new(-0.9f, -0.9f, 0.30f, 1f);
        Vector4 b = new(0.9f, -0.9f, 0.70f, 1f);
        Vector4 c = new(0.9f, 0.9f, 0.70f, 1f);
        Vector4 d = new(-0.9f, 0.9f, 0.30f, 1f);

        buffer.AddTriangle(a, b, c);
        buffer.AddTriangle(a, c, d);
        buffer.Build();

        const int row = 12;

        var left = buffer.DepthAt(0, 4, row);
        var right = buffer.DepthAt(0, 10, row);

        Assert.True(left < right, $"depth should increase across the slope, got {left} then {right}");

        var edgeNdcX = 5f / 16f * 2f - 1f;
        var atFarEdge = 0.30f + 0.40f * ((edgeNdcX + 0.9f) / 1.8f);

        Assert.True(
            left >= atFarEdge - 1e-4f,
            $"the texel stored {left}, nearer than the {atFarEdge} the plane reaches inside it");
    }
}
