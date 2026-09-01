using SoftEngine.Core.Geometry;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Shadows;

public sealed class ShadowMapRenderer
{
    private readonly ShadowCascadePlanner _planner = new();

    private ShadowMap? _map;
    private Vector3[] _projected = [];
    private int[] _vertexOffset = [];

    public int TriangleCount { get; private set; }

    public int CascadeCount { get; private set; }

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

            var cutout = mesh.Material is { IsCutout: true } material && mesh.TexCoords is { } texCoords
                ? new Cutout(new TextureSampler(material.DiffuseMap, 0, TextureFiltering.Nearest), material.AlphaCutoff, texCoords)
                : default;

            for (var t = 0; t < faces.Length; t++)
            {
                var face = faces[t];

                if (cutout.IsActive)
                {
                    FillTriangleMasked(
                        depth,
                        map.Resolution,
                        _projected[offset + face.I0],
                        _projected[offset + face.I1],
                        _projected[offset + face.I2],
                        cutout,
                        face.I0, face.I1, face.I2,
                        rowFrom,
                        rowTo);

                    continue;
                }

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

    private readonly struct Cutout(in TextureSampler mask, float cutoff, Vector2[] texCoords)
    {
        public readonly TextureSampler Mask = mask;
        public readonly float Cutoff = cutoff;
        public readonly Vector2[]? TexCoords = texCoords;

        public bool IsActive => TexCoords is not null && Mask.HasTexture;
    }

    private static void FillTriangleMasked(
        Span<float> depth, int resolution,
        Vector3 p0, Vector3 p1, Vector3 p2,
        in Cutout cutout,
        int i0, int i1, int i2,
        int rowFrom, int rowTo)
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

        var invArea = 1f / area;

        var dw0 = (p1.Y - p2.Y) * invArea;
        var dw1 = (p2.Y - p0.Y) * invArea;

        var texCoords = cutout.TexCoords!;
        var uv0 = texCoords[i0];
        var uv1 = texCoords[i1];
        var uv2 = texCoords[i2];

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

                if (z >= depth[texel])
                {
                    continue;
                }

                var u = w0 * uv0.X + w1 * uv1.X + w2 * uv2.X;
                var v = w0 * uv0.Y + w1 * uv1.Y + w2 * uv2.Y;

                if (cutout.Mask.SampleAlpha(u, v) < cutout.Cutoff)
                {
                    continue;
                }

                depth[texel] = z;
            }
        }
    }

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

        var invArea = 1f / area;

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

    private static float Edge(in Vector3 a, in Vector3 b, float x, float y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
