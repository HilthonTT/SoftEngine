using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Benchmarks;

/// <summary>
/// One measurable workload: a scene, a painter, and whatever renderer settings the workload is
/// about. Built fresh per run so a measurement never inherits another one's warmed buffers.
/// </summary>
internal sealed class BenchmarkScene(string name, string description, Func<int, int, (Renderer Renderer, Scene Scene, IPainter Painter)> build)
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    public (Renderer Renderer, Scene Scene, IPainter Painter) Build(int width, int height) => build(width, height);

    /// <summary>
    /// The workloads the renderer is actually shaped around: a dense model where the cost is
    /// per-triangle setup, heavy overdraw where it is the depth test, a handful of huge
    /// triangles where one tile's worth of pixels dominates, and thousands of small meshes
    /// where it is the per-mesh work before any pixel is touched.
    /// </summary>
    public static IReadOnlyList<BenchmarkScene> All { get; } =
    [
        new("dense-model",
            "one 81,920-triangle sphere filling the frame",
            static (w, h) => Compose(w, h, [Sphere(6, Vector3.Zero, 1f)], 2.6f, new GouraudPainter())),

        new("overdraw",
            "24 nested spheres, back-face culling off — every pixel written many times",
            static (w, h) =>
            {
                var meshes = new List<IMesh>();
                for (var i = 0; i < 24; i++)
                {
                    meshes.Add(Sphere(3, Vector3.Zero, 1f - i * 0.02f));
                }

                var built = Compose(w, h, meshes, 2.6f, new GouraudPainter());
                built.Renderer.Settings.BackFaceCulling = false;
                return built;
            }),

        new("big-triangles",
            "16 screen-filling triangles at stepped depths",
            static (w, h) => Compose(w, h, [BigTriangles(16)], 3f, new GouraudPainter())),

        new("many-meshes",
            "4,096 cubes — per-mesh transform and cull rather than per-pixel fill",
            static (w, h) =>
            {
                var meshes = new List<IMesh>();
                const int side = 16;

                for (var x = 0; x < side; x++)
                {
                    for (var y = 0; y < side; y++)
                    {
                        for (var z = 0; z < side; z++)
                        {
                            meshes.Add(new Cube
                            {
                                Position = new Vector3(x - side / 2f, y - side / 2f, z - side / 2f) * 1.5f,
                                Scale = new Vector3(0.6f),
                            });
                        }
                    }
                }

                return Compose(w, h, meshes, 34f, new GouraudPainter());
            }),

        new("shadows",
            "the dense model over a ground plane, with the shadow pass on",
            static (w, h) =>
            {
                var built = Compose(
                    w, h,
                    [Sphere(5, new Vector3(0, 0.6f, 0), 1f), Ground(8f, -0.5f)],
                    5f,
                    new PhongPainter());

                built.Scene.Shadows.Enabled = true;
                return built;
            }),

        new("occlusion",
            "512 dense spheres behind a wall that covers the frame — 164k hidden triangles, every one of them transformed without the pass",
            static (w, h) =>
            {
                var meshes = new List<IMesh> { Wall(5f, 0f) };

                // Dense meshes rather than cubes, because that is the case the pass exists for.
                // A twelve-triangle cube behind a wall costs almost nothing to reject and
                // almost nothing to keep, so a scene made of them measures the pass's overhead
                // and not its point; the work worth skipping is geometry that is expensive to
                // transform, and a rejected mesh's cost is its whole cost.
                for (var x = 0; x < 8; x++)
                {
                    for (var y = 0; y < 8; y++)
                    {
                        for (var z = 0; z < 8; z++)
                        {
                            meshes.Add(Sphere(
                                2,
                                new Vector3(x - 3.5f, y - 3.5f, -2f - z * 2.6f) * new Vector3(0.9f, 0.9f, 1f),
                                0.4f));
                        }
                    }
                }

                return Compose(w, h, meshes, 6f, new GouraudPainter());
            }),

        new("pbr",
            "36 spheres shaded by the Cook-Torrance path — the most expensive per-pixel shader",
            static (w, h) =>
            {
                var meshes = new List<IMesh>();

                for (var x = 0; x < 6; x++)
                {
                    for (var y = 0; y < 6; y++)
                    {
                        var sphere = Sphere(3, new Vector3(x - 2.5f, y - 2.5f, 0) * 1.2f, 0.5f);
                        sphere.Material.Roughness = (x + 0.5f) / 6f;
                        sphere.Material.Metallic = y / 5f;
                        meshes.Add(sphere);
                    }
                }

                return Compose(w, h, meshes, 11f, new PbrPainter());
            }),
    ];

    /// <summary>The common wiring: a frame buffer, a fixed camera pulled back to <paramref name="distance"/>, one sun.</summary>
    private static (Renderer Renderer, Scene Scene, IPainter Painter) Compose(
        int width,
        int height,
        List<IMesh> meshes,
        float distance,
        IPainter painter)
    {
        var renderer = new Renderer();
        renderer.Settings.BackFaceCulling = true;

        // Events cost allocation-free but non-zero work per mesh, and the interactive app can
        // switch them off; a benchmark of the renderer should not be measuring its debugger.
        renderer.Diagnostics.Events.IsEnabled = false;

        var scene = new Scene
        {
            Surface = new FrameBuffer(width, height) { Stats = renderer.Stats },
            Camera = new FixedCamera(new Vector3(0, 0, distance), Vector3.Zero),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 200f),
            World = new SimpleWorld
            {
                Meshes = meshes,
                Lights = [new DirectionalLight { Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.6f)) }],
            },
        };

        return (renderer, scene, painter);
    }

    private static Mesh Sphere(int recursion, Vector3 position, float radius)
    {
        var source = new IcoSphere(recursion);

        // A fresh Mesh rather than the IcoSphere itself: primitives share one static colour
        // array between instances, and a scene of 36 of them would otherwise be 36 views of
        // the same colours.
        var vertices = (Vector3[])source.Vertices.Clone();

        return new Mesh(vertices, source.Triangles, (Vector3[])source.NormVertices.Clone())
        {
            Position = position,
            Scale = new Vector3(radius),
        };
    }

    /// <summary>
    /// A stack of triangles each large enough to cover the whole viewport, at stepped depths.
    /// The per-triangle setup is negligible here and the fill is everything, which is the
    /// case the tiled rasterizer's vectorized depth test is aimed at.
    /// </summary>
    private static Mesh BigTriangles(int count)
    {
        var vertices = new Vector3[count * 3];
        var triangles = new Triangle[count];
        var colors = new ColorRGB[count];

        for (var i = 0; i < count; i++)
        {
            // Nearest first, so every triangle after the first is a full-screen depth test
            // that fails — which is precisely what the tile's coarse depth bound exists to
            // drop whole, and so what --compare measures the bound's worth on.
            var z = -i * 0.1f;

            vertices[i * 3 + 0] = new Vector3(-8f, -5f, z);
            vertices[i * 3 + 1] = new Vector3(8f, -5f, z);
            vertices[i * 3 + 2] = new Vector3(0f, 9f, z);

            triangles[i] = new Triangle(i * 3, i * 3 + 1, i * 3 + 2);
            colors[i] = new ColorRGB((byte)(40 + i * 8), 120, (byte)(200 - i * 8));
        }

        return new Mesh(vertices, triangles, null, colors);
    }

    /// <summary>A single quad facing the camera, large enough to fill the frame at the scene's viewing distance.</summary>
    private static Mesh Wall(float half, float z)
    {
        Vector3[] vertices =
        [
            new(-half, -half, z),
            new(half, -half, z),
            new(half, half, z),
            new(-half, half, z),
        ];

        Triangle[] triangles = [new(0, 1, 2), new(0, 2, 3)];

        return new Mesh(vertices, triangles, [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);
    }

    private static Mesh Ground(float extent, float y)
    {
        Vector3[] vertices =
        [
            new(-extent, y, -extent),
            new(extent, y, -extent),
            new(extent, y, extent),
            new(-extent, y, extent),
        ];

        Triangle[] triangles = [new(0, 2, 1), new(0, 3, 2)];

        return new Mesh(vertices, triangles, [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY]);
    }
}
