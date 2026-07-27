using SoftEngine.Core.Geometry;
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
/// Each cascade gets a parallel projection sized to a sphere around what it covers: a slice of
/// the camera's frustum when there is a view to slice, and the whole world when there is not.
/// A sphere rather than a box because a sphere is the same size whichever way the camera has
/// turned — fit a box to the slice's corners and the box grows and shrinks as the camera
/// rotates, which makes every shadow edge in the frame crawl. For the same reason the fitted
/// box is snapped to whole texels, so the map's grid stays put while the camera moves through
/// it.
/// </para>
///
/// A parallel projection makes a point light an approximation — its rays are treated as
/// parallel, aimed the way it points at the region being covered — which holds while the light
/// is well outside the scene and breaks for one sitting inside it.
///
/// Reused across frames: the map, the projected-vertex scratch and the per-mesh offsets are
/// all resized only when the world outgrows them.
/// </summary>
public sealed class ShadowMapRenderer
{
    private ShadowMap? _map;
    private Vector3[] _projected = [];
    private int[] _vertexOffset = [];

    private readonly List<int> _casters = [];
    private readonly List<int> _cascadeCasters = [];

    // Per-caster world-space bounding spheres, so a cascade can reject one without
    // re-deriving it from the mesh's matrix.
    private Vector3[] _casterCenter = [];
    private float[] _casterRadius = [];

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
    /// The camera the cascades are fitted to. Without one the pass falls back to a single map
    /// over the whole world, whatever <see cref="ShadowSettings.CascadeCount"/> asks for:
    /// cascades are slices of a view frustum, and there is no honest way to slice one that was
    /// not supplied.
    /// </param>
    public ShadowMap? Render(IWorld world, ILight light, ShadowSettings settings, ShadowView? view = null)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));
        ArgumentNullException.ThrowIfNull(light, nameof(light));
        ArgumentNullException.ThrowIfNull(settings, nameof(settings));

        TriangleCount = 0;
        CascadeCount = 0;

        if (!CollectCasters(world, out var worldCenter, out var worldRadius))
        {
            return null;
        }

        var cascades = view is null ? 1 : settings.CascadeCount;

        var map = EnsureMap(settings.Resolution, cascades);
        map.Begin(settings.Strength, settings.SoftFilter);

        CascadeCount = cascades;

        Span<float> splits = stackalloc float[ShadowMap.MaxCascades + 1];

        var camera = view ?? default;
        var sliced = view is not null && Split(camera, settings, cascades, splits);

        for (var cascade = 0; cascade < cascades; cascade++)
        {
            // A cascade that cannot be fitted — a slice a degenerate projection produces no
            // corners for — falls back to covering the world, so it shadows something rather
            // than nothing.
            var center = worldCenter;
            var radius = worldRadius;

            if (sliced && Fit(camera, splits[cascade], splits[cascade + 1], out var sliceCenter, out var sliceRadius))
            {
                center = sliceCenter;
                radius = sliceRadius;
            }

            RenderCascade(world, light, settings, map, cascade, center, radius, worldCenter, worldRadius);
        }

        return map;
    }

    /// <summary>
    /// Divides the view distance between the cascades.
    ///
    /// Neither of the two obvious schemes works alone. Splitting evenly by distance gives the
    /// near slice — where nearly all the pixels are — the same span as the far one. Splitting
    /// so each slice is a fixed multiple of the last puts the first boundary a few units in
    /// front of the eye. The standard answer is to interpolate between them, weighted toward
    /// the ratio, which is what <see cref="ShadowSettings.SplitBlend"/> selects.
    /// </summary>
    private static bool Split(in ShadowView view, ShadowSettings settings, int cascades, Span<float> splits)
    {
        var near = view.Near;
        var far = settings.MaxDistance > 0f ? MathF.Min(settings.MaxDistance, view.Far) : view.Far;

        if (far <= near)
        {
            return false;
        }

        splits[0] = near;
        splits[cascades] = far;

        for (var i = 1; i < cascades; i++)
        {
            var fraction = i / (float)cascades;

            var logarithmic = near * MathF.Pow(far / near, fraction);
            var uniform = near + (far - near) * fraction;

            splits[i] = uniform + (logarithmic - uniform) * settings.SplitBlend;
        }

        return true;
    }

    /// <summary>
    /// The sphere containing one slice of the view frustum. Its centre is on the view axis by
    /// symmetry, so the radius depends only on the slice's own dimensions — which is exactly
    /// the property that keeps the cascade from resizing as the camera turns.
    /// </summary>
    private static bool Fit(in ShadowView view, float near, float far, out Vector3 center, out float radius)
    {
        center = Vector3.Zero;
        radius = 0f;

        Span<Vector3> corners = stackalloc Vector3[8];

        if (!view.Corners(near, far, corners))
        {
            return false;
        }

        foreach (var corner in corners)
        {
            center += corner;
        }

        center *= 1f / 8f;

        foreach (var corner in corners)
        {
            radius = MathF.Max(radius, (corner - center).Length());
        }

        radius = MathF.Max(radius, 1e-3f);
        return true;
    }

    private void RenderCascade(
        IWorld world,
        ILight light,
        ShadowSettings settings,
        ShadowMap map,
        int cascade,
        Vector3 center,
        float radius,
        Vector3 worldCenter,
        float worldRadius)
    {
        var toLight = light.DirectionFrom(center);
        if (toLight.LengthSquared() < 1e-12f)
        {
            toLight = Vector3.UnitY;
        }
        toLight = Vector3.Normalize(toLight);

        // CreateLookAt degenerates when the view direction is parallel to up.
        var up = MathF.Abs(toLight.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        // The light's *orientation* only, anchored at the world origin rather than at the
        // region being covered. That anchoring is the whole point: a frame built by looking at
        // the region puts the region's centre at the origin by construction, so there is
        // nothing left to snap and the grid slides continuously with the camera. Fixed to the
        // world, the grid stays put and the region moves across it in whole texels.
        var lightView = Matrix4x4.CreateLookAt(Vector3.Zero, -toLight, up);

        var lightCenter = Vector3.Transform(center, lightView);

        // Snapping shifts the region by up to half a texel, so the box is widened by a texel
        // on each side to keep covering the sphere. Deriving the half-extent first and the
        // texel from it keeps the snap grid and the map's real texel grid the same grid.
        var resolution = settings.Resolution;
        var halfExtent = radius * resolution / (resolution - 2f);
        var texel = halfExtent * 2f / resolution;

        var snapX = MathF.Round(lightCenter.X / texel) * texel;
        var snapY = MathF.Round(lightCenter.Y / texel) * texel;

        // Depth has to span from the nearest thing that can cast — anywhere in the world,
        // including well outside this cascade and between it and the light — to the far side
        // of the region receiving. A cascade fitted to a slice of ground would otherwise clip
        // away the building standing over it.
        var lightWorld = Vector3.Transform(worldCenter, lightView);

        var nearestZ = MathF.Max(lightWorld.Z + worldRadius, lightCenter.Z + radius);
        var farthestZ = MathF.Min(lightWorld.Z - worldRadius, lightCenter.Z - radius);

        const float near = 0.01f;
        var far = near + MathF.Max(nearestZ - farthestZ, 1e-3f);

        // Looking down -Z, so the plane at light-space z = nearestZ has to land at -near.
        var offset = new Vector3(-snapX, -snapY, -nearestZ - near);

        var projection = Matrix4x4.CreateOrthographic(halfExtent * 2f, halfExtent * 2f, near, far);

        var lightViewProjection = lightView * Matrix4x4.CreateTranslation(offset) * projection;

        // The settings express bias in texels of depth; convert to the normalized units the
        // comparison works in. One texel spans `texel` world units across the map, and the
        // depth range covers (far - near) of them — so a cascade covering less ground needs
        // proportionally less bias, and gets it without being told.
        var texelDepth = texel / (far - near);

        map.SetCascade(
            cascade,
            lightViewProjection,
            settings.DepthBias * texelDepth,
            settings.SlopeBias * texelDepth);

        // Only the casters that can reach this cascade. Under a parallel projection that is a
        // question about perpendicular distance to the light axis and nothing else — which is
        // where a cascade earns its cost back, since the near one covers a few metres and
        // rejects nearly everything.
        SelectCasters(center, halfExtent, toLight);

        if (_cascadeCasters.Count == 0)
        {
            return;
        }

        ProjectVertices(world, lightViewProjection, resolution);
        TriangleCount += Rasterize(world, map, cascade);
    }

    /// <summary>
    /// Finds the meshes that cast and a world-space sphere containing them. Transparent and
    /// hidden meshes are excluded: a mesh you can see through should not block the light, and
    /// one dropped from the frame should not leave its shadow behind.
    /// </summary>
    private bool CollectCasters(IWorld world, out Vector3 center, out float radius)
    {
        _casters.Clear();
        center = Vector3.Zero;
        radius = 0f;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        var meshes = world.Meshes;

        if (_casterCenter.Length < meshes.Count)
        {
            _casterCenter = new Vector3[System.Math.Max(meshes.Count, _casterCenter.Length * 2)];
            _casterRadius = new float[_casterCenter.Length];
        }

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            if (!mesh.Visible || mesh.Opacity < 1f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var worldMatrix = mesh.WorldMatrix;

            var meshCenter = Vector3.Transform(Vector3.Zero, worldMatrix);
            var meshRadius = mesh.BoundingRadius * MeshExtensions.MaxScale(worldMatrix);

            if (float.IsNaN(meshRadius) || float.IsInfinity(meshRadius))
            {
                continue;
            }

            min = Vector3.Min(min, meshCenter - new Vector3(meshRadius));
            max = Vector3.Max(max, meshCenter + new Vector3(meshRadius));

            _casterCenter[i] = meshCenter;
            _casterRadius[i] = meshRadius;

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

    /// <summary>
    /// The casters that can put something into one cascade: those whose bounding sphere comes
    /// within the cascade's radius of the light axis through its centre. Depth along that axis
    /// is not tested, because the projection was already stretched to cover every caster.
    /// </summary>
    private void SelectCasters(Vector3 center, float radius, Vector3 toLight)
    {
        _cascadeCasters.Clear();

        foreach (var index in _casters)
        {
            var offset = _casterCenter[index] - center;
            var perpendicular = offset - toLight * Vector3.Dot(offset, toLight);

            var reach = radius + _casterRadius[index];

            if (perpendicular.LengthSquared() <= reach * reach)
            {
                _cascadeCasters.Add(index);
            }
        }
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
    private void ProjectVertices(IWorld world, in Matrix4x4 lightViewProjection, int resolution)
    {
        var meshes = world.Meshes;

        if (_vertexOffset.Length < meshes.Count)
        {
            _vertexOffset = new int[System.Math.Max(meshes.Count, _vertexOffset.Length * 2)];
        }

        var total = 0;
        foreach (var index in _cascadeCasters)
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

        Parallel.ForEach(_cascadeCasters, index =>
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
    private int Rasterize(IWorld world, ShadowMap map, int cascade)
    {
        var meshes = world.Meshes;
        var bands = System.Math.Clamp(Environment.ProcessorCount, 1, 16);
        var resolution = map.Resolution;

        var triangles = 0;
        foreach (var index in _cascadeCasters)
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
            RasterizeBand(world, map, cascade, 0, resolution);
            return triangles;
        }

        var rowsPerBand = (resolution + bands - 1) / bands;

        Parallel.For(0, bands, band =>
        {
            var from = band * rowsPerBand;
            var to = System.Math.Min(from + rowsPerBand, resolution);

            if (from < to)
            {
                RasterizeBand(world, map, cascade, from, to);
            }
        });

        return triangles;
    }

    private void RasterizeBand(IWorld world, ShadowMap map, int cascade, int rowFrom, int rowTo)
    {
        var meshes = world.Meshes;
        var depth = map.DepthOf(cascade);

        foreach (var index in _cascadeCasters)
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
