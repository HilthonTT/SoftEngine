using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Culling;

public sealed class OcclusionCuller
{
    public int Divisor { get; set; } = 2;

    public int MinimumResolution { get; set; } = 64;

    public float MinimumOccluderExtent { get; set; } = 0.18f;

    public int MaximumOccluders { get; set; } = 12;

    public int MinimumTestableMeshes { get; set; } = 32;

    public int TriangleBudget { get; set; } = 6000;

    private readonly OcclusionBuffer _buffer = new();

    private readonly List<Candidate> _candidates = [];

    private bool[] _isOccluder = [];

    private readonly List<int> _occluders = [];
    private int[] _vertexOffset = [];

    private Vector4[] _projected = [];

    private Matrix4x4 _projection;
    private bool _prepared;

    private readonly record struct Candidate(int MeshIndex, float ScreenExtent, int TriangleCount);

    public OcclusionBuffer Buffer => _buffer;

    public int OccluderCount { get; private set; }

    public int TriangleCount { get; private set; }

    public void Prepare(
        IWorld world,
        in Matrix4x4 viewMatrix,
        in Matrix4x4 projectionMatrix,
        ReadOnlySpan<Vector4> frustumPlanes,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        _projection = projectionMatrix;
        _prepared = false;

        OccluderCount = 0;
        TriangleCount = 0;

        var meshCount = world.Meshes.Count;

        if (meshCount < MinimumTestableMeshes)
        {
            return;
        }

        var meshes = world.Meshes;

        if (_isOccluder.Length < meshCount)
        {
            _isOccluder = new bool[System.Math.Max(meshCount, _isOccluder.Length * 2)];
        }

        Array.Clear(_isOccluder, 0, meshCount);

        if (!Select(meshes, viewMatrix, projectionMatrix, frustumPlanes))
        {
            return;
        }

        _buffer.Resize(
            System.Math.Max(MinimumResolution, width / System.Math.Max(1, Divisor)),
            System.Math.Max(MinimumResolution, height / System.Math.Max(1, Divisor)));

        _buffer.Clear();

        Rasterize(meshes, viewMatrix, projectionMatrix);

        _buffer.Build();
        _prepared = true;
    }

    private bool Select(
        List<IMesh> meshes,
        in Matrix4x4 viewMatrix,
        in Matrix4x4 projectionMatrix,
        ReadOnlySpan<Vector4> frustumPlanes)
    {
        _candidates.Clear();

        var verticalScale = projectionMatrix.M22;

        if (verticalScale <= 0f)
        {
            return false;
        }

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            if (!mesh.Visible || mesh.Opacity < 1f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var worldMatrix = mesh.WorldMatrix;
            var radius = mesh.WorldBoundingRadius(worldMatrix);

            if (!float.IsFinite(radius) || radius <= 0f)
            {
                continue;
            }

            var center = Vector3.Transform(Vector3.Zero, worldMatrix * viewMatrix);

            if (Frustum.IsSphereOutside(frustumPlanes, center, radius))
            {
                continue;
            }

            var depth = -center.Z;

            if (depth <= 1e-4f)
            {
                continue;
            }

            var extent = radius * verticalScale / depth;

            if (extent < MinimumOccluderExtent)
            {
                continue;
            }

            _candidates.Add(new Candidate(i, extent, mesh.Triangles.Length));
        }

        if (_candidates.Count == 0)
        {
            return false;
        }

        _candidates.Sort(static (a, b) => b.ScreenExtent.CompareTo(a.ScreenExtent));

        var triangles = 0;
        var chosen = 0;

        foreach (var candidate in _candidates)
        {
            if (chosen >= MaximumOccluders || triangles + candidate.TriangleCount > TriangleBudget)
            {
                continue;
            }

            _isOccluder[candidate.MeshIndex] = true;
            triangles += candidate.TriangleCount;
            chosen++;
        }

        OccluderCount = chosen;
        return chosen > 0;
    }

    private void Rasterize(List<IMesh> meshes, in Matrix4x4 viewMatrix, in Matrix4x4 projectionMatrix)
    {
        var viewProjection = viewMatrix * projectionMatrix;

        _occluders.Clear();

        var vertexTotal = 0;

        for (var i = 0; i < meshes.Count; i++)
        {
            if (_isOccluder[i])
            {
                _occluders.Add(i);
                vertexTotal += meshes[i].Vertices.Length;
            }
        }

        if (_projected.Length < vertexTotal)
        {
            _projected = new Vector4[System.Math.Max(vertexTotal, _projected.Length * 2)];
        }

        if (_vertexOffset.Length < meshes.Count)
        {
            _vertexOffset = new int[System.Math.Max(meshes.Count, _vertexOffset.Length * 2)];
        }

        var triangles = 0;
        var offset = 0;

        foreach (var index in _occluders)
        {
            var mesh = meshes[index];
            var vertices = mesh.Vertices;

            _vertexOffset[index] = offset;

            var matrix = mesh.WorldMatrix * viewProjection;

            for (var v = 0; v < vertices.Length; v++)
            {
                _projected[offset + v] = Vector4.Transform(vertices[v], matrix);
            }

            offset += vertices.Length;
            triangles += mesh.Triangles.Length;
        }

        TriangleCount = triangles;

        var bands = System.Math.Clamp(Environment.ProcessorCount, 1, 16);
        var height = _buffer.Height;

        if (bands == 1 || triangles < 8)
        {
            FillBand(meshes, 0, height);
            return;
        }

        var rowsPerBand = (height + bands - 1) / bands;

        Parallel.For(0, bands, band =>
        {
            var from = band * rowsPerBand;
            var to = System.Math.Min(from + rowsPerBand, height);

            if (from < to)
            {
                FillBand(meshes, from, to);
            }
        });
    }

    private void FillBand(List<IMesh> meshes, int rowFrom, int rowTo)
    {
        foreach (var index in _occluders)
        {
            var mesh = meshes[index];
            var offset = _vertexOffset[index];

            foreach (var triangle in mesh.Triangles)
            {
                _buffer.AddTriangle(
                    _projected[offset + triangle.I0],
                    _projected[offset + triangle.I1],
                    _projected[offset + triangle.I2],
                    rowFrom,
                    rowTo);
            }
        }
    }

    public bool IsOccluded(int meshIndex, Vector3 viewCenter, float radius)
    {
        if (!_prepared || !_buffer.HasOccluders)
        {
            return false;
        }

        if ((uint)meshIndex < (uint)_isOccluder.Length && _isOccluder[meshIndex])
        {
            return false;
        }

        if (!float.IsFinite(radius) || radius <= 0f)
        {
            return false;
        }

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector3(
                viewCenter.X + ((corner & 1) == 0 ? -radius : radius),
                viewCenter.Y + ((corner & 2) == 0 ? -radius : radius),
                viewCenter.Z + ((corner & 4) == 0 ? -radius : radius));

            var clip = Vector4.Transform(point, _projection);

            if (clip.W <= 1e-6f)
            {
                return false;
            }

            var inverseW = 1f / clip.W;

            var x = clip.X * inverseW;
            var y = clip.Y * inverseW;
            var z = clip.Z * inverseW;

            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
            minZ = MathF.Min(minZ, z);
        }

        if (!_buffer.IsHidden(minX, minY, maxX, maxY, minZ))
        {
            return false;
        }

        return true;
    }

    public void Reset()
    {
        _prepared = false;
        OccluderCount = 0;
        TriangleCount = 0;
    }
}
