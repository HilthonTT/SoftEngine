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

    [Fact]
    public void PhaseOne_RecordsTheSameAccountSequentially()
    {
        var restore = Renderer.ParallelCullPhase;

        try
        {
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

    [Fact]
    public void PhaseOne_ClipsAcrossTheNearPlaneWithTheSameResultEitherWay()
    {
        var restore = Renderer.ParallelCullPhase;

        try
        {
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
