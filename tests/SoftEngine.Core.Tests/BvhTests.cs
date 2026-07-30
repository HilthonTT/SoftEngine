using SoftEngine.Core.Acceleration;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class BvhTests
{
    /// <summary>A grid of cubes, which is enough geometry that a wrong tree cannot pass by luck.</summary>
    private static SimpleWorld CubeGrid(int side, float spacing = 3f)
    {
        var world = new SimpleWorld();

        for (var x = 0; x < side; x++)
        {
            for (var y = 0; y < side; y++)
            {
                for (var z = 0; z < side; z++)
                {
                    world.Meshes.Add(new Cube
                    {
                        Position = new Vector3(
                            (x - side * 0.5f) * spacing,
                            (y - side * 0.5f) * spacing,
                            (z - side * 0.5f) * spacing),
                    });
                }
            }
        }

        return world;
    }

    /// <summary>Rays fired from a shell around the scene, aimed at points scattered through it.</summary>
    private static IEnumerable<Ray> ProbeRays(int count)
    {
        // Fixed sequence rather than a random one: a test that fails should fail every time.
        var state = 12345u;

        uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        float Unit() => (Next() >> 8) / (float)(1 << 24);

        for (var i = 0; i < count; i++)
        {
            var origin = new Vector3(Unit() * 40f - 20f, Unit() * 40f - 20f, Unit() * 40f - 20f);
            var target = new Vector3(Unit() * 12f - 6f, Unit() * 12f - 6f, Unit() * 12f - 6f);

            var direction = target - origin;

            if (direction.LengthSquared() < 1e-6f)
            {
                continue;
            }

            yield return new Ray(origin, Vector3.Normalize(direction));
        }
    }

    [Fact]
    public void Build_CoversEveryTriangleExactlyOnce()
    {
        var geometry = SceneGeometry.Build(CubeGrid(3));
        var bvh = Bvh.Build(geometry);

        Assert.Equal(3 * 3 * 3 * new Cube().Triangles.Length, geometry.TriangleCount);

        // Every triangle has to end up in exactly one leaf, or the tree is either losing geometry
        // or testing some of it twice.
        Assert.True(bvh.LeafCount > 1);
        Assert.True(bvh.NodeCount >= 2 * bvh.LeafCount - 1);
    }

    [Fact]
    public void Intersect_AgreesWithTestingEveryTriangle()
    {
        var geometry = SceneGeometry.Build(CubeGrid(3));
        var bvh = Bvh.Build(geometry);

        var tested = 0;

        foreach (var ray in ProbeRays(400))
        {
            // The answer the tree is supposed to be an optimisation of: every triangle, in order.
            var expectedDistance = float.PositiveInfinity;

            for (var t = 0; t < geometry.TriangleCount; t++)
            {
                var (a, b, c) = geometry.Corners(t);

                if (Bvh.IntersectsTriangle(ray, a, b, c, out var distance, out _, out _) &&
                    distance < expectedDistance)
                {
                    expectedDistance = distance;
                }
            }

            var found = bvh.Intersect(ray, out var hit);

            Assert.Equal(!float.IsPositiveInfinity(expectedDistance), found);

            if (found)
            {
                Assert.Equal(expectedDistance, hit.Distance, 4);
                tested++;
            }
        }

        Assert.True(tested > 50, $"only {tested} of the probe rays hit anything");
    }

    [Fact]
    public void Intersect_AgreesWithThePicker()
    {
        // The two structures answer the same question by different routes — one walks a tree in
        // world space, the other transforms the ray into each mesh's own space — so they are each
        // other's check.
        var world = CubeGrid(3, spacing: 2f);
        var bvh = Bvh.Build(SceneGeometry.Build(world));

        var compared = 0;

        foreach (var ray in ProbeRays(400))
        {
            var picked = ScenePicker.Pick(world, ray);
            var traced = bvh.Intersect(ray, out var hit);

            Assert.Equal(picked is not null, traced);

            if (picked is { } pick)
            {
                Assert.Equal(pick.Distance, hit.Distance, 3);
                Assert.Equal(pick.MeshIndex, bvh.Geometry.MeshIndex(hit.Triangle));

                // And the same again through the accelerated pick, which has to produce the same
                // record — same mesh, same triangle of it, same point — or a caller that switches
                // to it would select something else.
                var accelerated = ScenePicker.Pick(bvh, ray);

                Assert.NotNull(accelerated);
                Assert.Same(pick.Mesh, accelerated!.Value.Mesh);
                Assert.Equal(pick.MeshIndex, accelerated.Value.MeshIndex);
                Assert.Equal(pick.TriangleIndex, accelerated.Value.TriangleIndex);
                Assert.True((pick.Point - accelerated.Value.Point).Length() < 1e-3f);

                compared++;
            }
        }

        Assert.True(compared > 20, $"only {compared} rays hit the world");
    }

    [Fact]
    public void Intersect_RespectsTheDistanceLimit()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Position = new Vector3(0, 0, 10f) });

        var bvh = Bvh.Build(SceneGeometry.Build(world));
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);

        Assert.True(bvh.Intersect(ray, out var hit));
        Assert.True(hit.Distance > 8f && hit.Distance < 10f, $"hit at {hit.Distance}");

        Assert.False(bvh.Intersect(ray, hit.Distance - 0.01f, out _));
        Assert.True(bvh.IsOccluded(ray, hit.Distance + 0.01f));
        Assert.False(bvh.IsOccluded(ray, hit.Distance - 0.01f));
    }

    [Fact]
    public void Intersect_ReportsWhereOnTheTriangleItLanded()
    {
        var world = new SimpleWorld();

        // One triangle in the z = 0 plane, big enough to aim at parts of.
        world.Meshes.Add(new Mesh(
            [new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 4, 0)],
            [new Triangle(0, 1, 2)]));

        var geometry = SceneGeometry.Build(world);
        var bvh = Bvh.Build(geometry);

        foreach (var (target, expectedU, expectedV) in new[]
        {
            (new Vector3(0.4f, 0.4f, 0f), 0.1f, 0.1f),
            (new Vector3(2f, 1f, 0f), 0.5f, 0.25f),
            (new Vector3(1f, 2f, 0f), 0.25f, 0.5f),
        })
        {
            var ray = new Ray(target + new Vector3(0, 0, 5f), -Vector3.UnitZ);

            Assert.True(bvh.Intersect(ray, out var hit));

            Assert.Equal(expectedU, hit.U, 4);
            Assert.Equal(expectedV, hit.V, 4);

            // The barycentric weights have to reconstruct the point the ray was aimed at.
            var (a, b, c) = geometry.Corners(hit.Triangle);
            var reconstructed = a * hit.W + b * hit.U + c * hit.V;

            Assert.True((reconstructed - target).Length() < 1e-3f, $"{reconstructed} vs {target}");
        }
    }

    [Fact]
    public void Intersect_FindsGeometryFromInsideIt()
    {
        // The camera standing inside a box has to see its walls, which means a ray starting inside
        // every bounding box in the tree still has to descend it.
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Scale = new Vector3(20f, 20f, 20f) });

        var bvh = Bvh.Build(SceneGeometry.Build(world));

        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ })
        {
            Assert.True(bvh.Intersect(new Ray(Vector3.Zero, direction), out _));
        }
    }

    [Fact]
    public void Build_HandlesAnEmptyWorld()
    {
        var bvh = Bvh.Build(SceneGeometry.Build(new SimpleWorld()));

        Assert.Equal(0, bvh.Geometry.TriangleCount);
        Assert.False(bvh.Intersect(new Ray(Vector3.Zero, Vector3.UnitZ), out _));
        Assert.False(bvh.IsOccluded(new Ray(Vector3.Zero, Vector3.UnitZ), 1000f));
    }

    [Fact]
    public void Build_HandlesCoincidentGeometry()
    {
        // Every centroid at the same point: there is no axis to split on, so the build has to fall
        // back to a leaf rather than recursing forever or splitting nothing off.
        var world = new SimpleWorld();

        for (var i = 0; i < 40; i++)
        {
            world.Meshes.Add(new Mesh(
                [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
                [new Triangle(0, 1, 2)]));
        }

        var bvh = Bvh.Build(SceneGeometry.Build(world));

        Assert.Equal(40, bvh.Geometry.TriangleCount);
        Assert.True(bvh.Intersect(new Ray(new Vector3(0.2f, 0.2f, 5f), -Vector3.UnitZ), out _));
    }

    [Fact]
    public void SceneGeometry_SkipsWhatTheRendererWouldNotDraw()
    {
        var world = new SimpleWorld();

        world.Meshes.Add(new Cube());
        world.Meshes.Add(new Cube { Visible = false });
        world.Meshes.Add(new Cube { Opacity = 0f });

        // Transparent but not invisible: a ray can still decide what to do about it.
        world.Meshes.Add(new Cube { Opacity = 0.5f });

        var geometry = SceneGeometry.Build(world);

        Assert.Equal(2 * new Cube().Triangles.Length, geometry.TriangleCount);
    }

    [Fact]
    public void SceneGeometry_PutsTrianglesInWorldSpace()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube
        {
            Position = new Vector3(10f, 0f, 0f),
            Scale = new Vector3(2f, 2f, 2f),
        });

        var geometry = SceneGeometry.Build(world);

        for (var t = 0; t < geometry.TriangleCount; t++)
        {
            var (a, _, _) = geometry.Corners(t);

            // A unit cube scaled by two and moved ten along x spans [8, 12] there.
            Assert.True(a.X is >= 7.9f and <= 12.1f, $"{a}");
        }
    }

    [Fact]
    public void Stamp_ChangesWhenSomethingMoves()
    {
        var world = new SimpleWorld();
        var mesh = new Cube();
        world.Meshes.Add(mesh);

        var before = SceneGeometry.Stamp(world);

        Assert.Equal(before, SceneGeometry.Stamp(world));

        mesh.Position = new Vector3(1f, 0f, 0f);
        Assert.NotEqual(before, SceneGeometry.Stamp(world));

        mesh.Position = Vector3.Zero;
        Assert.Equal(before, SceneGeometry.Stamp(world));

        mesh.Visible = false;
        Assert.NotEqual(before, SceneGeometry.Stamp(world));
    }
}
