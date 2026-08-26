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
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

/// <summary>
/// Phase 1 no longer runs in the order it reports: it decides each mesh's fate in one pass, does
/// the work in two parallel ones, and records what happened in a fourth.
///
/// <para>
/// The golden suite already compares the two configurations pixel for pixel, and that is the
/// check that would catch a vertex computed wrongly. What it cannot catch is a reordering,
/// because for opaque geometry the z-buffer makes the fill order-independent — the same pixels
/// come out whichever triangle reached them first. This is where the order itself is pinned:
/// the event log's, which is read top to bottom as an account of what the renderer did, and the
/// draw list's, which is visible in how much work the depth test managed to reject.
/// </para>
/// </summary>
public class CullPhaseTests
{
    private const int Width = 320;
    private const int Height = 240;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Mesh CubeAt(Vector3 position, float scale = 0.5f)
    {
        var source = new Cube();
        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, new ColorRGB(200, 120, 80));

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
        renderer.Settings.OcclusionCulling = false;

        // Recording is the subject here, and it is also what puts phase 1 on the unordered
        // path: a captured frame is walked in the world's own order so that the account it
        // gives is of the world as it is written down.
        renderer.Diagnostics.Events.IsEnabled = true;

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

    /// <summary>The phase-1 events, in the order they were recorded, paired with the mesh they name.</summary>
    private static List<(GraphicsEventKind Kind, int ObjectId)> PhaseOneEvents(Renderer renderer, Scene scene)
    {
        var meshIdBase = SceneObjectIds.Mesh(scene.World.Lights.Count, 0);
        var meshCount = scene.World.Meshes.Count;

        var kinds = new[]
        {
            GraphicsEventKind.MeshSkipInactive,
            GraphicsEventKind.MeshCullBoundingSphere,
            GraphicsEventKind.MeshCullOccluded,
            GraphicsEventKind.MeshTransformVertices,
            GraphicsEventKind.MeshCullTriangles,
        };

        var recorded = new List<(GraphicsEventKind, int)>();

        foreach (var graphicsEvent in renderer.Diagnostics.Events.AsSpan())
        {
            if (Array.IndexOf(kinds, graphicsEvent.Kind) >= 0 &&
                graphicsEvent.ObjectId >= meshIdBase &&
                graphicsEvent.ObjectId < meshIdBase + meshCount)
            {
                recorded.Add((graphicsEvent.Kind, graphicsEvent.ObjectId));
            }
        }

        return recorded;
    }

    /// <summary>
    /// Four meshes, one of each outcome, in an order that interleaves the rejected with the
    /// drawn. A phase that recorded its rejections separately from its survivors would pass any
    /// assertion about which events are present and fail this one.
    /// </summary>
    [Fact]
    public void PhaseOne_RecordsEachMeshInWorldOrder()
    {
        var hidden = CubeAt(new Vector3(-1f, 0f, 0f));
        hidden.Visible = false;

        var (renderer, scene) = Build(
            CubeAt(new Vector3(-1.2f, 0f, 0f)),
            hidden,
            CubeAt(new Vector3(0f, 400f, 0f)),
            CubeAt(new Vector3(1.2f, 0f, 0f)));

        renderer.Render(scene, new GouraudPainter());

        var meshIdBase = SceneObjectIds.Mesh(scene.World.Lights.Count, 0);

        Assert.Equal(
            [
                (GraphicsEventKind.MeshTransformVertices, meshIdBase + 0),
                (GraphicsEventKind.MeshCullTriangles, meshIdBase + 0),
                (GraphicsEventKind.MeshSkipInactive, meshIdBase + 1),
                (GraphicsEventKind.MeshCullBoundingSphere, meshIdBase + 2),
                (GraphicsEventKind.MeshTransformVertices, meshIdBase + 3),
                (GraphicsEventKind.MeshCullTriangles, meshIdBase + 3),
            ],
            PhaseOneEvents(renderer, scene));
    }

    /// <summary>
    /// The same account whether the phase divided its work or not. The seam exists so this can
    /// be asked rather than assumed; see <see cref="Renderer.ParallelCullPhase"/>.
    /// </summary>
    [Fact]
    public void PhaseOne_RecordsTheSameAccountSequentially()
    {
        var restore = Renderer.ParallelCullPhase;

        try
        {
            // Dense enough that the passes actually divide: the triangle pass takes the
            // parallel path only above a couple of thousand triangles, which one cube is not.
            var meshes = new IMesh[64];

            for (var i = 0; i < meshes.Length; i++)
            {
                meshes[i] = new IcoSphere(2)
                {
                    Position = new Vector3(i % 8 - 4f, i / 8 - 4f, 0f) * 0.6f,
                    Scale = new Vector3(0.25f),
                };
            }

            Renderer.ParallelCullPhase = true;
            var (parallelRenderer, parallelScene) = Build(meshes);
            parallelRenderer.Render(parallelScene, new GouraudPainter());
            var parallel = PhaseOneEvents(parallelRenderer, parallelScene);

            Renderer.ParallelCullPhase = false;
            var (sequentialRenderer, sequentialScene) = Build(meshes);
            sequentialRenderer.Render(sequentialScene, new GouraudPainter());
            var sequential = PhaseOneEvents(sequentialRenderer, sequentialScene);

            Assert.NotEmpty(parallel);
            Assert.Equal(sequential, parallel);

            // The counters the same events carry, which is the other half of the account.
            Assert.Equal(sequentialRenderer.Stats.DrawnTriangleCount, parallelRenderer.Stats.DrawnTriangleCount);
            Assert.Equal(sequentialRenderer.Stats.FacingBackTriangleCount, parallelRenderer.Stats.FacingBackTriangleCount);
            Assert.Equal(sequentialRenderer.Stats.OutOfViewTriangleCount, parallelRenderer.Stats.OutOfViewTriangleCount);
            Assert.Equal(sequentialRenderer.Stats.BehindViewTriangleCount, parallelRenderer.Stats.BehindViewTriangleCount);
            Assert.Equal(sequentialRenderer.Stats.TotalTriangleCount, parallelRenderer.Stats.TotalTriangleCount);
        }
        finally
        {
            Renderer.ParallelCullPhase = restore;
        }
    }

    /// <summary>
    /// The draw list's order, pinned through the one thing that can see it.
    ///
    /// <para>
    /// Two nested spheres, so most of the outer one's pixels are drawn over and most of the
    /// inner one's are rejected. How many the depth test rejects, and how many triangles the
    /// tile's coarse bound drops whole, both depend on what was drawn before what — which is
    /// the entire reason the fill is handed its triangles nearest-mesh-first. Reverse a mesh's
    /// triangles and the picture is identical while both counters move; that is the failure
    /// this exists to catch, and nothing else in the suite would notice it.
    /// </para>
    /// </summary>
    [Fact]
    public void PhaseOne_CollectsTheTrianglesInTheSameOrderEitherWay()
    {
        var restore = Renderer.ParallelCullPhase;

        try
        {
            static (int Drawn, int BehindZ, int Occluded) Run(bool parallel)
            {
                Renderer.ParallelCullPhase = parallel;

                var (renderer, scene) = Build(
                    new IcoSphere(5) { Scale = new Vector3(2.4f) },
                    new IcoSphere(5) { Scale = new Vector3(2.2f) });

                renderer.Render(scene, new GouraudPainter());

                return (renderer.Stats.DrawnPixelCount, renderer.Stats.BehindZPixelCount, renderer.Stats.OccludedTriangleCount);
            }

            var parallel = Run(true);
            var sequential = Run(false);

            Assert.True(parallel.BehindZ > 0 && parallel.Occluded > 0, "the scene was meant to reject work");
            Assert.Equal(sequential, parallel);
        }
        finally
        {
            Renderer.ParallelCullPhase = restore;
        }
    }

    /// <summary>
    /// A mesh straddling the near plane is the one case phase 1 cannot finish in parallel: its
    /// sub-triangles are appended to the mesh's own buffer, so the split is left to the
    /// sequential pass at the end. Worth a case of its own, because a frame usually has none —
    /// and a path that only runs when something touches the lens is a path that goes untested.
    /// </summary>
    [Fact]
    public void PhaseOne_ClipsAcrossTheNearPlaneWithTheSameResultEitherWay()
    {
        var restore = Renderer.ParallelCullPhase;

        try
        {
            // A ground plane just below the eye and long enough to run out past the camera, so
            // it crosses the near plane in the middle of the frame rather than off the edge of
            // it. Subdivided past the threshold the triangle pass parallelizes at, so the
            // straddlers being marked and the bands being merged are both on the divided path.
            static IMesh[] Straddling() =>
            [
                new PlaneMesh(600f, 600f, 40, 40) { Position = new Vector3(0f, -0.02f, 0f) },
            ];

            Renderer.ParallelCullPhase = true;
            var (parallelRenderer, parallelScene) = Build(Straddling());
            parallelRenderer.Render(parallelScene, new GouraudPainter());

            Renderer.ParallelCullPhase = false;
            var (sequentialRenderer, sequentialScene) = Build(Straddling());
            sequentialRenderer.Render(sequentialScene, new GouraudPainter());

            Assert.True(parallelRenderer.Stats.NearClippedTriangleCount > 0, "the scene was meant to straddle the near plane");

            Assert.Equal(sequentialRenderer.Stats.NearClippedTriangleCount, parallelRenderer.Stats.NearClippedTriangleCount);
            Assert.Equal(sequentialRenderer.Stats.DrawnTriangleCount, parallelRenderer.Stats.DrawnTriangleCount);
            Assert.Equal(PhaseOneEvents(sequentialRenderer, sequentialScene), PhaseOneEvents(parallelRenderer, parallelScene));
            Assert.Equal(sequentialScene.Surface.Screen, parallelScene.Surface.Screen);
        }
        finally
        {
            Renderer.ParallelCullPhase = restore;
        }
    }
}
