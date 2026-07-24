using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Shadows;

/// <summary>
/// The depth-only pass that fills a <see cref="ShadowMap"/>. It is the main pipeline in
/// miniature — transform, project, rasterize — with everything that doesn't affect
/// occlusion removed: no colour, no lighting, no clipping, and no varyings.
///
/// The light gets a parallel projection sized to a sphere around the whole world, so the
/// map covers the scene from any angle at the cost of spending its resolution uniformly.
/// That makes a point light an approximation — its rays are treated as parallel, aimed the
/// way it points at the world's centre — which holds while the light is well outside the
/// scene and breaks for one sitting inside it.
/// Reused across frames: the map, the projected-vertex scratch and the per-mesh offsets
/// are all resized only when the world outgrows them.
/// </summary>
public sealed class ShadowMapRenderer
{
    private ShadowMap? _map;
    private Vector3[] _projected = [];
    private int[] _vertexOffset = [];
    private readonly List<int> _casters = [];

    /// <summary>Triangles rasterized into the map by the last <see cref="Render"/> call.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>
    /// Renders every opaque, visible mesh of <paramref name="world"/> into a shadow map for
    /// <paramref name="light"/>. Returns null when the world casts nothing — an empty world,
    /// or one whose meshes are all transparent or hidden.
    /// </summary>
    public ShadowMap? Render(IWorld world, ILight light, ShadowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(settings);

        TriangleCount = 0;

        if (!CollectCasters(world, out var center, out var radius))
        {
            return null;
        }

        var map = EnsureMap(settings.Resolution);

        // Pull the light back outside the world sphere and look at its centre. A parallel
        // projection has no eye point, so the distance only has to keep the near plane in
        // front of the geometry.
        var toLight = light.DirectionFrom(center);
        if (toLight.LengthSquared() < 1e-12f)
        {
            toLight = Vector3.UnitY;
        }
        toLight = Vector3.Normalize(toLight);

        var distance = radius * 2f + 1f;
        var eye = center + toLight * distance;

        // CreateLookAt degenerates when the view direction is parallel to up.
        var up = MathF.Abs(toLight.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        const float near = 0.01f;
        var far = distance + radius + 1f;

        var view = Matrix4x4.CreateLookAt(eye, center, up);
        var projection = Matrix4x4.CreateOrthographic(radius * 2f, radius * 2f, near, far);
        var lightViewProjection = view * projection;

        // The settings express bias in texels of depth; convert to the normalized units the
        // comparison works in. One texel spans (2·radius / resolution) world units across
        // the map, and the depth range covers (far - near) of them.
        var texelDepth = radius * 2f / (map.Resolution * (far - near));

        map.Begin(
            lightViewProjection,
            settings.DepthBias * texelDepth,
            settings.SlopeBias * texelDepth,
            settings.Strength,
            settings.SoftFilter);

        ProjectVertices(world, lightViewProjection, map.Resolution);
        TriangleCount = Rasterize(world, map);

        return map;
    }

    /// <summary>
    /// Finds the meshes that cast and a world-space sphere containing them. Transparent and
    /// hidden meshes are excluded: a mesh you can see through should not block the light,
    /// and one dropped from the frame should not leave its shadow behind.
    /// </summary>
    private bool CollectCasters(IWorld world, out Vector3 center, out float radius)
    {
        _casters.Clear();
        center = Vector3.Zero;
        radius = 0f;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        var meshes = world.Meshes;
        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            if (!mesh.Visible || mesh.Opacity < 1f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var meshCenter = Vector3.Transform(Vector3.Zero, mesh.WorldMatrix);
            var meshRadius = mesh.BoundingRadius * MaxAbsComponent(mesh.Scale);

            if (float.IsNaN(meshRadius) || float.IsInfinity(meshRadius))
            {
                continue;
            }

            min = Vector3.Min(min, meshCenter - new Vector3(meshRadius));
            max = Vector3.Max(max, meshCenter + new Vector3(meshRadius));

            _casters.Add(i);
        }

        if (_casters.Count == 0)
        {
            return false;
        }

        center = (min + max) * 0.5f;
        radius = MathF.Max((max - min).Length() * 0.5f, 1e-3f);

        return true;
    }

    private ShadowMap EnsureMap(int resolution)
    {
        if (_map is null || _map.Resolution != resolution)
        {
            _map = new ShadowMap(resolution);
        }

        return _map;
    }

    /// <summary>
    /// Transforms every caster's vertices straight into shadow-map texel space: X and Y in
    /// texels from the top-left, Z the normalized depth the comparison later reads.
    /// </summary>
    private void ProjectVertices(IWorld world, in Matrix4x4 lightViewProjection, int resolution)
    {
        var meshes = world.Meshes;

        if (_vertexOffset.Length < meshes.Count)
        {
            _vertexOffset = new int[System.Math.Max(meshes.Count, _vertexOffset.Length * 2)];
        }

        var total = 0;
        foreach (var index in _casters)
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

        Parallel.ForEach(_casters, index =>
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
    /// Fills the map, keeping the nearest depth per texel. Work is split into contiguous
    /// bands of rows: every worker walks every triangle but writes only its own rows, so
    /// the depth writes never overlap and no band needs a lock.
    /// </summary>
    private int Rasterize(IWorld world, ShadowMap map)
    {
        var meshes = world.Meshes;
        var bands = System.Math.Clamp(Environment.ProcessorCount, 1, 16);
        var resolution = map.Resolution;

        var triangles = 0;
        foreach (var index in _casters)
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
            RasterizeBand(world, map, 0, resolution);
            return triangles;
        }

        var rowsPerBand = (resolution + bands - 1) / bands;

        Parallel.For(0, bands, band =>
        {
            var from = band * rowsPerBand;
            var to = System.Math.Min(from + rowsPerBand, resolution);

            if (from < to)
            {
                RasterizeBand(world, map, from, to);
            }
        });

        return triangles;
    }

    private void RasterizeBand(IWorld world, ShadowMap map, int rowFrom, int rowTo)
    {
        var meshes = world.Meshes;

        foreach (var index in _casters)
        {
            var mesh = meshes[index];
            var offset = _vertexOffset[index];
            var faces = mesh.Triangles;

            for (var t = 0; t < faces.Length; t++)
            {
                var face = faces[t];

                FillTriangle(
                    map,
                    _projected[offset + face.I0],
                    _projected[offset + face.I1],
                    _projected[offset + face.I2],
                    rowFrom,
                    rowTo);
            }
        }
    }

    /// <summary>
    /// Depth-only triangle fill by edge functions over the bounding box. The main
    /// rasterizer's scanline walk carries varyings and a shader it doesn't need here, and
    /// edge functions clip to a band of rows by simply narrowing the box.
    /// </summary>
    private static void FillTriangle(ShadowMap map, Vector3 p0, Vector3 p1, Vector3 p2, int rowFrom, int rowTo)
    {
        var resolution = map.Resolution;

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

        var depth = map.Depth;

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

    private static float MaxAbsComponent(Vector3 v) =>
        MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z)));
}
