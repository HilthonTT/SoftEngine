using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.Clipping;
using SoftEngine.Core.Pipeline.Culling;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

public sealed partial class Renderer
{
    private const int CullVertexBandSize = 1024;

    private const int CullTriangleBandSize = 512;

    private const int ParallelVertexThreshold = 4096;

    private const int ParallelTriangleThreshold = 2048;

    public static bool ParallelCullPhase { get; set; } = true;

    private enum MeshCullOutcome : byte
    {
        Visible,
        Inactive,
        OutsideFrustum,
        Occluded,
    }

    private struct CullJob
    {
        public int MeshIndex;
        public int VertexCount;
        public int TriangleCount;

        public bool Transparent;

        public bool HasNormals;

        public Matrix4x4 ModelView;
        public Matrix4x4 World;

        public int FirstTriangleBand;
        public int TriangleBandCount;
    }

    private struct CullBand
    {
        public int Job;
        public int From;
        public int To;
    }

    private struct CullCounts
    {
        public int BehindView;
        public int FacingBack;
        public int OutOfView;
        public int Drawn;
    }

    private CullJob[] _cullJobs = [];
    private int _cullJobCount;

    private int _cullVertexTotal;
    private int _cullTriangleTotal;

    private CullBand[] _vertexBands = [];
    private int _vertexBandCount;

    private CullBand[] _triangleBands = [];
    private int _triangleBandCount;

    private List<int>[] _bandKept = [];

    private CullCounts[] _bandCounts = [];

    private MeshCullOutcome[] _meshOutcome = [];

    private int[] _meshJob = [];

    private List<IMesh>? _passMeshes;
    private VertexBuffer[]? _passBuffers;
    private Matrix4x4 _passProjection;
    private bool _passBackFaceCulling;

    private Action<int>? _vertexBandAction;
    private Action<int>? _triangleBandAction;

    private void CullPhase(
        List<IMesh> meshes,
        int volumeCount,
        int[]? meshOrder,
        WorldBuffer worldBuffer,
        GraphicsEventLog events,
        int meshIdBase,
        in Matrix4x4 viewMatrix,
        in Matrix4x4 projectionMatrix,
        ReadOnlySpan<Vector4> frustumPlanes,
        OcclusionCuller? occlusion,
        bool backFaceCulling)
    {
        _visible.Clear();
        _transparent.Clear();

        BuildCullJobs(meshes, volumeCount, meshOrder, worldBuffer, viewMatrix, frustumPlanes, occlusion);

        RunVertexPass(worldBuffer, meshes, projectionMatrix);
        RunTrianglePass(worldBuffer, backFaceCulling);

        MergeCullResults(meshes, volumeCount, meshOrder, worldBuffer, events, meshIdBase);
    }

    private void BuildCullJobs(
        List<IMesh> meshes,
        int volumeCount,
        int[]? meshOrder,
        WorldBuffer worldBuffer,
        in Matrix4x4 viewMatrix,
        ReadOnlySpan<Vector4> frustumPlanes,
        OcclusionCuller? occlusion)
    {
        EnsureCapacity(ref _meshOutcome, volumeCount);
        EnsureCapacity(ref _meshJob, volumeCount);
        EnsureCapacity(ref _cullJobs, volumeCount);

        _cullJobCount = 0;
        _vertexBandCount = 0;
        _triangleBandCount = 0;
        _cullVertexTotal = 0;
        _cullTriangleTotal = 0;

        for (var slot = 0; slot < volumeCount; slot++)
        {
            var idxVolume = meshOrder is null ? slot : meshOrder[slot];

            var vbx = worldBuffer.VertexBuffers[idxVolume];
            var mesh = meshes[idxVolume];

            var worldMatrix = meshOrder is null ? mesh.WorldMatrix : _meshWorld[idxVolume];
            var modelViewMatrix = worldMatrix * viewMatrix;

            vbx.Mesh = mesh;
            vbx.WorldMatrix = worldMatrix;

            Stats.TotalTriangleCount += mesh.Triangles.Length;

            _meshJob[slot] = -1;

            if (!mesh.Visible || mesh.Opacity <= 0f)
            {
                _meshOutcome[slot] = MeshCullOutcome.Inactive;
                continue;
            }

            var radius = mesh.WorldBoundingRadius(worldMatrix);
            if (float.IsFinite(radius))
            {
                var viewCenter = Vector3.Transform(Vector3.Zero, modelViewMatrix);
                if (Frustum.IsSphereOutside(frustumPlanes, viewCenter, radius))
                {
                    Stats.OutOfViewTriangleCount += mesh.Triangles.Length;
                    _meshOutcome[slot] = MeshCullOutcome.OutsideFrustum;
                    continue;
                }

                if (occlusion is not null && occlusion.IsOccluded(idxVolume, viewCenter, radius))
                {
                    Stats.OccludedMeshTriangleCount += mesh.Triangles.Length;
                    Stats.OccludedMeshCount++;
                    _meshOutcome[slot] = MeshCullOutcome.Occluded;
                    continue;
                }
            }

            _meshOutcome[slot] = MeshCullOutcome.Visible;
            _meshJob[slot] = _cullJobCount;

            var vertexCount = mesh.Vertices.Length;
            var triangleCount = mesh.Triangles.Length;

            _cullJobs[_cullJobCount] = new CullJob
            {
                MeshIndex = idxVolume,
                VertexCount = vertexCount,
                TriangleCount = triangleCount,

                Transparent = mesh.Opacity < 1f,

                HasNormals = mesh.NormVertices.Length >= vertexCount,

                ModelView = modelViewMatrix,
                World = worldMatrix,
                FirstTriangleBand = _triangleBandCount,
                TriangleBandCount = BandCount(triangleCount, CullTriangleBandSize),
            };

            AddVertexBands(_cullJobCount, vertexCount);
            AddTriangleBands(_cullJobCount, triangleCount);

            _cullVertexTotal += vertexCount;
            _cullTriangleTotal += triangleCount;

            _cullJobCount++;
        }
    }

    private void RunVertexPass(WorldBuffer worldBuffer, List<IMesh> meshes, in Matrix4x4 projectionMatrix)
    {
        if (_vertexBandCount == 0)
        {
            return;
        }

        _passProjection = projectionMatrix;
        _passMeshes = meshes;
        _passBuffers = worldBuffer.VertexBuffers;

        RunBands(_vertexBandCount, _cullVertexTotal, ParallelVertexThreshold, _vertexBandAction ??= VertexBand);
    }

    private void VertexBand(int band)
    {
        var work = _vertexBands[band];
        var job = _cullJobs[work.Job];
        var mesh = _passMeshes![job.MeshIndex];

        var projection = _passProjection;
        var source = mesh.Vertices;
        var normals = mesh.NormVertices;
        var target = _passBuffers![job.MeshIndex].Vertices;

        var modelView = job.ModelView;
        var world = job.World;
        var hasNormals = job.HasNormals;

        for (var v = work.From; v < work.To; v++)
        {
            var model = source[v];
            var view = Vector3.Transform(model, modelView);

            target[v] = new Vertices
            {
                View = view,
                Proj = Vector4.Transform(view, projection),
                World = Vector3.Transform(model, world),
                Norm = hasNormals ? Vector3.TransformNormal(normals[v], world) : Vector3.Zero,
            };
        }
    }

    private void RunTrianglePass(WorldBuffer worldBuffer, bool backFaceCulling)
    {
        if (_triangleBandCount == 0)
        {
            return;
        }

        _passBuffers = worldBuffer.VertexBuffers;
        _passBackFaceCulling = backFaceCulling;

        RunBands(_triangleBandCount, _cullTriangleTotal, ParallelTriangleThreshold, _triangleBandAction ??= TriangleBand);
    }

    private void TriangleBand(int band)
    {
        var work = _triangleBands[band];
        var job = _cullJobs[work.Job];
        var vbx = _passBuffers![job.MeshIndex];
        var triangles = vbx.Mesh!.Triangles;

        var backFaceCulling = _passBackFaceCulling;
        var list = _bandKept[band];
        list.Clear();

        var behindView = 0;
        var facingBack = 0;
        var outOfView = 0;
        var drawn = 0;

        for (var idxTriangle = work.From; idxTriangle < work.To; idxTriangle++)
        {
            Triangle t = triangles[idxTriangle];

            if (t.IsBehindFarPlane(vbx))
            {
                behindView++;
                continue;
            }

            if (backFaceCulling && t.IsFacingBack(vbx))
            {
                facingBack++;
                continue;
            }

            var behindNear = (vbx.Vertices[t.I0].Proj.Z < 0 ? 1 : 0)
                + (vbx.Vertices[t.I1].Proj.Z < 0 ? 1 : 0)
                + (vbx.Vertices[t.I2].Proj.Z < 0 ? 1 : 0);

            if (behindNear == 3)
            {
                behindView++;
                continue;
            }

            if (behindNear == 0)
            {
                if (t.IsOutsideFrustum(vbx))
                {
                    outOfView++;
                    continue;
                }

                list.Add(idxTriangle);
                drawn++;
                continue;
            }

            list.Add(~idxTriangle);
        }

        _bandCounts[band] = new CullCounts
        {
            BehindView = behindView,
            FacingBack = facingBack,
            OutOfView = outOfView,
            Drawn = drawn,
        };
    }

    private void MergeCullResults(
        List<IMesh> meshes,
        int volumeCount,
        int[]? meshOrder,
        WorldBuffer worldBuffer,
        GraphicsEventLog events,
        int meshIdBase)
    {
        for (var slot = 0; slot < volumeCount; slot++)
        {
            var idxVolume = meshOrder is null ? slot : meshOrder[slot];
            var objectId = meshIdBase + idxVolume;
            var triangleCount = meshes[idxVolume].Triangles.Length;

            switch (_meshOutcome[slot])
            {
                case MeshCullOutcome.Inactive:
                    events.Add(GraphicsEventKind.MeshSkipInactive, objectId, triangleCount);
                    continue;

                case MeshCullOutcome.OutsideFrustum:
                    events.Add(GraphicsEventKind.MeshCullBoundingSphere, objectId, triangleCount);
                    continue;

                case MeshCullOutcome.Occluded:
                    events.Add(GraphicsEventKind.MeshCullOccluded, objectId, triangleCount);
                    continue;
            }

            var job = _cullJobs[_meshJob[slot]];
            var vbx = worldBuffer.VertexBuffers[idxVolume];
            var target = job.Transparent ? _transparent : _visible;

            events.Add(GraphicsEventKind.MeshTransformVertices, objectId, job.VertexCount);

            var drawn = 0;
            var facingBack = 0;
            var clipped = 0;

            var lastBand = job.FirstTriangleBand + job.TriangleBandCount;

            for (var band = job.FirstTriangleBand; band < lastBand; band++)
            {
                var counts = _bandCounts[band];

                Stats.BehindViewTriangleCount += counts.BehindView;
                Stats.FacingBackTriangleCount += counts.FacingBack;
                Stats.OutOfViewTriangleCount += counts.OutOfView;
                Stats.DrawnTriangleCount += counts.Drawn;

                clipped += counts.BehindView + counts.OutOfView;
                facingBack += counts.FacingBack;
                drawn += counts.Drawn;

                var kept = _bandKept[band];

                for (var i = 0; i < kept.Count; i++)
                {
                    var entry = kept[i];

                    if (entry >= 0)
                    {
                        target.Add((idxVolume, entry));
                        continue;
                    }

                    var idxTriangle = ~entry;

                    if (NearPlaneClipper.Clip(vbx, vbx.Mesh!.Triangles[idxTriangle], idxTriangle, idxVolume, target) > 0)
                    {
                        Stats.DrawnTriangleCount++;
                        Stats.NearClippedTriangleCount++;
                        drawn++;
                    }
                    else
                    {
                        Stats.OutOfViewTriangleCount++;
                        clipped++;
                    }
                }
            }

            events.Add(GraphicsEventKind.MeshCullTriangles, objectId, drawn, facingBack, clipped);
        }
    }

    private static void RunBands(int bandCount, int work, int threshold, Action<int> band)
    {
        if (!ParallelCullPhase || bandCount == 1 || work < threshold || Environment.ProcessorCount <= 1)
        {
            for (var i = 0; i < bandCount; i++)
            {
                band(i);
            }

            return;
        }

        Parallel.For(0, bandCount, band);
    }

    private static int BandCount(int items, int bandSize) => (items + bandSize - 1) / bandSize;

    private void AddVertexBands(int job, int items) =>
        AddBands(ref _vertexBands, ref _vertexBandCount, job, items, CullVertexBandSize);

    private void AddTriangleBands(int job, int items)
    {
        var needed = _triangleBandCount + BandCount(items, CullTriangleBandSize);

        EnsureCapacity(ref _bandCounts, needed);
        EnsureLists(ref _bandKept, needed);

        AddBands(ref _triangleBands, ref _triangleBandCount, job, items, CullTriangleBandSize);
    }

    private static void AddBands(ref CullBand[] bands, ref int count, int job, int items, int bandSize)
    {
        EnsureCapacity(ref bands, count + BandCount(items, bandSize));

        for (var from = 0; from < items; from += bandSize)
        {
            bands[count++] = new CullBand
            {
                Job = job,
                From = from,
                To = System.Math.Min(from + bandSize, items),
            };
        }
    }

    private static void EnsureCapacity<T>(ref T[] array, int needed)
    {
        if (array.Length < needed)
        {
            Array.Resize(ref array, System.Math.Max(needed, array.Length * 2));
        }
    }

    private static void EnsureLists(ref List<int>[] lists, int needed)
    {
        var from = lists.Length;

        EnsureCapacity(ref lists, needed);

        for (var i = from; i < lists.Length; i++)
        {
            lists[i] = [];
        }
    }
}
