using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Shadows;

/// <summary>
/// The depth-only pass that fills a <see cref="ShadowMap"/>. It is the main pipeline in
/// miniature — transform, project, rasterize — with everything that doesn't affect occlusion
/// removed: no colour, no lighting, no clipping, and no varyings.
///
/// <para>
/// Where the cascades go and which meshes reach them is
/// <see cref="ShadowCascadePlanner"/>'s answer, shared with the GPU backend so the two
/// cannot drift. What is left here is the rasterizing.
/// </para>
///
/// Reused across frames: the map, the projected-vertex scratch and the per-mesh offsets are
/// all resized only when the world outgrows them.
/// </summary>
public sealed class ShadowMapRenderer
{
    private readonly ShadowCascadePlanner _planner = new();

    private ShadowMap? _map;
    private Vector3[] _projected = [];
    private int[] _vertexOffset = [];

    /// <summary>Triangles rasterized into the map by the last <see cref="Render"/> call, over every cascade.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>Cascades the last <see cref="Render"/> call actually filled.</summary>
    public int CascadeCount { get; private set; }

    /// <summary>
    /// Renders every opaque, visible mesh of <paramref name="world"/> into a shadow map for
    /// <paramref name="light"/>. Returns null when the world casts nothing — an empty world, or
    /// one whose meshes are all transparent or hidden.
    /// </summary>
    /// <param name="view">
    /// The camera the cascades are fitted to; see <see cref="ShadowCascadePlanner.Plan"/>.
    /// </param>
    public ShadowMap? Render(IWorld world, ILight light, ShadowSettings settings, ShadowView? view = null)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));
        ArgumentNullException.ThrowIfNull(light, nameof(light));
        ArgumentNullException.ThrowIfNull(settings, nameof(settings));

        TriangleCount = 0;
        CascadeCount = 0;

        if (!_planner.Plan(world, light, settings, view))
        {
            return null;
        }

        var cascades = _planner.CascadeCount;

        var map = EnsureMap(settings.Resolution, cascades);
        map.Begin(settings.Strength, settings.SoftFilter);

        CascadeCount = cascades;

        for (var cascade = 0; cascade < cascades; cascade++)
        {
            var setup = _planner.SetupOf(cascade);

            map.SetCascade(cascade, setup.LightViewProjection, setup.DepthBias, setup.SlopeBias);

            var casters = _planner.CastersOf(cascade);

            if (casters.Count == 0)
            {
                continue;
            }

            ProjectVertices(world, casters, setup.LightViewProjection, settings.Resolution);
            TriangleCount += Rasterize(world, casters, map, cascade);
        }

        return map;
    }

    private ShadowMap EnsureMap(int resolution, int cascades)
    {
        if (_map is null || _map.Resolution != resolution || _map.CascadeCount != cascades)
        {
            _map = new ShadowMap(resolution, cascades);
        }

        return _map;
    }

    /// <summary>
    /// Transforms every selected caster's vertices straight into shadow-map texel space: X and
    /// Y in texels from the top-left, Z the normalized depth the comparison later reads.
    /// </summary>
    private void ProjectVertices(IWorld world, IReadOnlyList<int> casters, in Matrix4x4 lightViewProjection, int resolution)
    {
        var meshes = world.Meshes;

        if (_vertexOffset.Length < meshes.Count)
        {
            _vertexOffset = new int[System.Math.Max(meshes.Count, _vertexOffset.Length * 2)];
        }

        var total = 0;
        foreach (var index in casters)
        {
            _vertexOffset[index] = total;
            total += meshes[index].Vertices.Length;
        }

        if (_projected.Length < total)
        {
            _projected = new Vector3[System.Math.Max(total, _projected.Length * 2)];
        }

        var half = resolution * 0.5f;
        var matrix = lightViewProjection;
        var projected = _projected;
        var offsets = _vertexOffset;

        Parallel.ForEach(casters, index =>
        {
            var mesh = meshes[index];
            var model = mesh.WorldMatrix * matrix;
            var vertices = mesh.Vertices;
            var offset = offsets[index];

            for (var v = 0; v < vertices.Length; v++)
            {
                var clip = Vector4.Transform(vertices[v], model);

                projected[offset + v] = new Vector3(
                    (clip.X + 1f) * half,
                    (1f - clip.Y) * half,
                    clip.Z);
            }
        });
    }

    /// <summary>
    /// Fills one cascade, keeping the nearest depth per texel. Work is split into contiguous
    /// bands of rows: every worker walks every triangle but writes only its own rows, so the
    /// depth writes never overlap and no band needs a lock.
    /// </summary>
    private int Rasterize(IWorld world, IReadOnlyList<int> casters, ShadowMap map, int cascade)
    {
        var meshes = world.Meshes;
        var bands = System.Math.Clamp(Environment.ProcessorCount, 1, 16);
        var resolution = map.Resolution;

        var triangles = 0;
        foreach (var index in casters)
        {
            triangles += meshes[index].Triangles.Length;
        }

        if (triangles == 0)
        {
            return 0;
        }

        // One band is cheaper than the scheduling for a handful of triangles.
        if (bands == 1 || triangles < 64)
        {
            RasterizeBand(world, casters, map, cascade, 0, resolution);
            return triangles;
        }

        var rowsPerBand = (resolution + bands - 1) / bands;

        Parallel.For(0, bands, band =>
        {
            var from = band * rowsPerBand;
            var to = System.Math.Min(from + rowsPerBand, resolution);

            if (from < to)
            {
                RasterizeBand(world, casters, map, cascade, from, to);
            }
        });

        return triangles;
    }

    private void RasterizeBand(IWorld world, IReadOnlyList<int> casters, ShadowMap map, int cascade, int rowFrom, int rowTo)
    {
        var meshes = world.Meshes;
        var depth = map.DepthOf(cascade);

        foreach (var index in casters)
        {
            var mesh = meshes[index];
            var offset = _vertexOffset[index];
            var faces = mesh.Triangles;

            for (var t = 0; t < faces.Length; t++)
            {
                var face = faces[t];

                FillTriangle(
                    depth,
                    map.Resolution,
                    _projected[offset + face.I0],
                    _projected[offset + face.I1],
                    _projected[offset + face.I2],
                    rowFrom,
                    rowTo);
            }
        }
    }

    /// <summary>
    /// Depth-only triangle fill by edge functions over the bounding box. The main rasterizer's
    /// scanline walk carries varyings and a shader it doesn't need here, and edge functions
    /// clip to a band of rows by simply narrowing the box.
    /// </summary>
    private static void FillTriangle(Span<float> depth, int resolution, Vector3 p0, Vector3 p1, Vector3 p2, int rowFrom, int rowTo)
    {
        var minX = System.Math.Max((int)MathF.Ceiling(MathF.Min(p0.X, MathF.Min(p1.X, p2.X)) - 0.5f), 0);
        var maxX = System.Math.Min((int)MathF.Floor(MathF.Max(p0.X, MathF.Max(p1.X, p2.X)) - 0.5f), resolution - 1);

        if (minX > maxX)
        {
            return;
        }

        var minY = System.Math.Max((int)MathF.Ceiling(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)) - 0.5f), rowFrom);
        var maxY = System.Math.Min((int)MathF.Floor(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)) - 0.5f), rowTo - 1);

        if (minY > maxY)
        {
            return;
        }

        var area = Edge(p0, p1, p2.X, p2.Y);
        if (MathF.Abs(area) < 1e-9f)
        {
            return;
        }

        // Normalize the winding away so one inside-test covers both orientations: shadow
        // casting has no front and back.
        var invArea = 1f / area;

        // Edge functions are affine in x and y, so each row starts from one evaluation and
        // steps by a constant.
        var dw0 = (p1.Y - p2.Y) * invArea;
        var dw1 = (p2.Y - p0.Y) * invArea;

        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            var px = minX + 0.5f;

            var w0 = Edge(p1, p2, px, py) * invArea;
            var w1 = Edge(p2, p0, px, py) * invArea;

            var row = y * resolution;

            for (var x = minX; x <= maxX; x++, w0 += dw0, w1 += dw1)
            {
                var w2 = 1f - w0 - w1;

                if (w0 < 0f || w1 < 0f || w2 < 0f)
                {
                    continue;
                }

                var z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z;

                if (z < 0f || z > 1f)
                {
                    continue;
                }

                var texel = row + x;
                if (z < depth[texel])
                {
                    depth[texel] = z;
                }
            }
        }
    }

    /// <summary>Twice the signed area of (a, b, point); its sign says which side the point is on.</summary>
    private static float Edge(in Vector3 a, in Vector3 b, float x, float y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
