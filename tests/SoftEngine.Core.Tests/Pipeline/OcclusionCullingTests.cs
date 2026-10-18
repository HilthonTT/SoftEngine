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

    /// <summary>
    /// A wall across the whole frame at z = 0, and whatever the caller puts behind it. The wall
    /// is deliberately far larger on screen than anything else in these scenes, so which mesh
    /// the pass picks as its occluder is never in question.
    /// </summary>
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

    /// <summary>
    /// A cube small enough on screen that the pass will never mistake it for an occluder — a
    /// mesh it rasterizes is one it then declines to test, so a "was it culled" assertion needs
    /// a subject that could only ever have been in the second group.
    /// </summary>
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

        // The pass declines to run on a world too small to repay it, which every scene here is:
        // they are built to isolate one decision, not to be worth making. Switching the floor
        // off is what lets a two-mesh scene exercise a pass aimed at a two-thousand-mesh one.
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

        // The claim is about work not done, not merely about a mesh not appearing. A rejected
        // mesh never reaches the triangle loop at all, so none of its triangles are counted
        // anywhere except as occluded.
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

    /// <summary>
    /// The case a buffer that rounded the wrong way would get wrong: a mesh mostly behind the
    /// wall but reaching past its edge. Every bound in the pass is deliberately generous, and
    /// this is what those choices are for.
    /// </summary>
    [Fact]
    public void MeshPeekingPastTheWallsEdge_IsDrawn()
    {
        // Far enough out to actually clear the edge, which is further than it looks. The wall
        // stands at z = 0 and the cube at z = -2, so perspective shrinks the cube's spread
        // toward the centre of the frame: at x = 1.15 it would sit comfortably *inside* the
        // wall's silhouette despite being the wider of the two in world space.
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

    /// <summary>
    /// Something you can see through does not hide what is behind it — the same exclusion the
    /// shadow pass makes, and for the same reason.
    /// </summary>
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

    /// <summary>A mesh dropped from the frame must not go on hiding things after it has gone.</summary>
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

    /// <summary>
    /// The pixel history has to show the writes the depth test rejects, and a mesh dropped
    /// before its vertices are transformed never attempts them. So a probed frame runs without
    /// the pass, exactly as it runs without the tile's coarse depth bound.
    /// </summary>
    [Fact]
    public void ProbedFrame_RunsWithoutThePass()
    {
        var (renderer, scene) = Build(Wall(3f, 0f, ColorRGB.Gray), SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Diagnostics.SetProbe(Width / 2, Height / 2);

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccludedMeshCount);
        Assert.Equal(0, renderer.Stats.OccluderMeshCount);
    }

    /// <summary>
    /// The whole claim, stated as an image. An optimization that decides what not to draw is
    /// only correct if what <em>is</em> drawn does not change, and no count of rejected meshes
    /// says that — a pass that culled the wall itself would report splendid numbers.
    ///
    /// <para>
    /// Both frames come out of one process on one machine, so this is compared at zero
    /// tolerance: any difference at all is a real one.
    /// </para>
    /// </summary>
    [Fact]
    public void CullingOnAndOff_ProduceTheSameFrame()
    {
        static (Renderer Renderer, Scene Scene) Compose()
        {
            var meshes = new List<IMesh> { Wall(3f, 0f, new ColorRGB(150, 155, 165)) };

            // A spread of small meshes behind the wall, plus a few beside it that must survive.
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

        // If the pass rejected nothing, the comparison below would pass for the wrong reason.
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

/// <summary>
/// The buffer's own rules, tested where they can be stated exactly rather than inferred from a
/// frame. Each is one of the places the pass was built to fail safe, and each would be nearly
/// invisible if it broke — a slightly too eager buffer deletes a mesh now and then, which reads
/// as a flicker rather than as a wrong answer.
/// </summary>
public class OcclusionBufferTests
{
    /// <summary>A triangle covering the middle of the buffer, at a known depth, in clip space.</summary>
    private static void AddQuad(OcclusionBuffer buffer, float ndcHalf, float depth)
    {
        // w = 1, so clip space is normalized device space and the depths are exactly as given.
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

    /// <summary>
    /// A rectangle reaching past the occluder's edge is not hidden, however deep it is. This is
    /// the pyramid's fold doing its job: over any region the level reports the <em>farthest</em>
    /// of the depths below it, so one uncovered texel in the region defeats the whole test.
    /// </summary>
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

    /// <summary>
    /// Anything leaving the frame is left alone. The part of it that is off screen is covered
    /// by nothing at all, so no rectangle crossing the edge can honestly be called hidden.
    /// </summary>
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

    /// <summary>
    /// An occluder too small to fill a whole queryable texel hides nothing, however deep the
    /// thing behind it is. It writes depth — level 0 is centre-sampled and it reaches a couple
    /// of centres — and still cannot answer a query, because a query reads a level up and a
    /// texel there carries a depth only where all four of its children were covered.
    ///
    /// <para>
    /// It is the rule that stops a scene of small scattered meshes assembling an occlusion
    /// buffer none of them individually earns.
    /// </para>
    /// </summary>
    [Fact]
    public void OccluderTooSmallToFillAQueryableTexel_HidesNothing()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(64, 64);
        buffer.Clear();

        // Level-0 texels are 1/32 of normalized device space across; this quad is a little
        // over one of them, and a query is answered on a grid twice as coarse again.
        AddQuad(buffer, 0.02f, 0.3f);
        buffer.Build();

        Assert.False(buffer.IsHidden(-0.02f, -0.02f, 0.02f, 0.02f, 0.9f));
    }

    /// <summary>
    /// The depth kept for a texel is the occluder's farthest point within it, not the value at
    /// its centre. On a steeply sloped surface those differ, and taking the centre would claim
    /// the near edge of a pixel covers the far edge too.
    /// </summary>
    [Fact]
    public void SlopedOccluder_StoresItsFarthestDepthPerTexel()
    {
        var buffer = new OcclusionBuffer();
        buffer.Resize(16, 16);
        buffer.Clear();

        // A quad tilted in depth: 0.30 along its left edge, 0.70 along its right.
        Vector4 a = new(-0.9f, -0.9f, 0.30f, 1f);
        Vector4 b = new(0.9f, -0.9f, 0.70f, 1f);
        Vector4 c = new(0.9f, 0.9f, 0.70f, 1f);
        Vector4 d = new(-0.9f, 0.9f, 0.30f, 1f);

        buffer.AddTriangle(a, b, c);
        buffer.AddTriangle(a, c, d);
        buffer.Build();

        // Sampled along row 12 rather than through the middle, only to stay clear of the
        // anti-diagonal the quad is split along and keep the reading about depth alone.
        const int row = 12;

        var left = buffer.DepthAt(0, 4, row);
        var right = buffer.DepthAt(0, 10, row);

        Assert.True(left < right, $"depth should increase across the slope, got {left} then {right}");

        // The plane's depth at the far edge of texel 4 — the boundary it shares with texel 5.
        // Interpolating at the texel's centre instead would store something nearer than this,
        // and so claim the near edge of the texel covers its far edge as well.
        var edgeNdcX = 5f / 16f * 2f - 1f;
        var atFarEdge = 0.30f + 0.40f * ((edgeNdcX + 0.9f) / 1.8f);

        Assert.True(
            left >= atFarEdge - 1e-4f,
            $"the texel stored {left}, nearer than the {atFarEdge} the plane reaches inside it");
    }
}
