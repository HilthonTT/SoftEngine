using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.Clipping;
using SoftEngine.Core.Pipeline.Culling;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

/// <summary>
/// Phase 1 of the frame: everything between "here is a world" and "here is the list of
/// triangles to fill". Transform, cull, project, clip.
///
/// <para>
/// It used to be one sequential loop over the meshes, and on a machine with cores to spare that
/// made it the frame's ceiling: the fill phase divides across every thread, so once it is fast
/// the serial half is what is left. Measured on the benchmark scenes at 320x180 — small enough
/// that the fill is nearly free — a frame of the 81,920-triangle sphere still cost 3.07 of its
/// 4.47 ms, and the 4,096-cube scene 4.53 of 7.82. That is the part this file divides up.
/// </para>
///
/// <para>
/// Splitting it is not simply a matter of running the loop in parallel, because two things in it
/// are order-dependent and one is not thread-safe at all:
/// </para>
///
/// <list type="bullet">
/// <item>
/// The draw list's order decides what the opaque fill draws first (nearest mesh first, so the
/// depth test rejects the rest), what a pixel probe reports, and what the transparent sort
/// permutes. It has to come out in exactly the order the sequential loop produced.
/// </item>
/// <item>
/// The event log is a record of the pipeline in pipeline order, so a frame capture reads as a
/// story rather than as a set.
/// </item>
/// <item>
/// Near-plane clipping appends to the mesh's own vertex and triangle lists, which nothing
/// synchronises.
/// </item>
/// </list>
///
/// <para>
/// So the phase is four passes rather than one loop. Two of them are parallel and hold all the
/// work; the two sequential ones are the ones that have to be ordered, and are cheap enough that
/// being sequential does not matter — see each for what it costs.
/// </para>
/// </summary>
public sealed partial class Renderer
{
    /// <summary>
    /// How many vertices one worker of the vertex pass takes at a time, and how many triangles
    /// one worker of the triangle pass takes.
    ///
    /// <para>
    /// Triangles come in smaller batches because a triangle costs more than a vertex — three
    /// projected positions read, two cross products, up to six plane tests — so the same batch
    /// size would make the last worker's tail longer.
    /// </para>
    /// </summary>
    private const int CullVertexBandSize = 1024;

    private const int CullTriangleBandSize = 512;

    /// <summary>
    /// Below this much work, the pass runs on the calling thread instead.
    ///
    /// <para>
    /// Both are a band or two's worth: at that size the join costs more than the split saves,
    /// and a viewer sitting on an empty scene should not be paying a scheduler to transform
    /// nothing. The thresholds are in the pass's own unit — vertices for one, triangles for the
    /// other — because a mesh can be dense in either without being dense in both.
    /// </para>
    /// </summary>
    private const int ParallelVertexThreshold = 4096;

    private const int ParallelTriangleThreshold = 2048;

    /// <summary>
    /// Whether the phase divides its two heavy passes across the cores. On by default.
    ///
    /// <para>
    /// It is settable because the claim this whole file makes is that splitting the phase
    /// changes nothing but the time it takes — same pixels, same statistics, same event list —
    /// and a claim like that is worth testing rather than asserting. The golden suite renders
    /// every one of its scenes both ways in one process and compares the frames pixel for
    /// pixel. A diagnostic seam, not a rendering option: nothing in the pipeline reads it, and
    /// neither front-end offers any way to change it.
    /// </para>
    /// </summary>
    public static bool ParallelCullPhase { get; set; } = true;

    /// <summary>What the whole-mesh rejections decided about one mesh.</summary>
    private enum MeshCullOutcome : byte
    {
        Visible,
        Inactive,
        OutsideFrustum,
        Occluded,
    }

    /// <summary>
    /// One mesh that survived the whole-mesh rejections, holding everything the two parallel
    /// passes need. They read this rather than the mesh, so nothing in them walks a scene graph
    /// or composes a matrix — <see cref="IMesh.WorldMatrix"/> does both, and doing either from
    /// several threads at once is exactly the sort of question this avoids having to answer.
    /// </summary>
    private struct CullJob
    {
        public int MeshIndex;
        public int VertexCount;
        public int TriangleCount;

        /// <summary>Whether the mesh's triangles collect into the transparent list.</summary>
        public bool Transparent;

        /// <summary>False when the mesh's normal array does not line up with its vertices.</summary>
        public bool HasNormals;

        public Matrix4x4 ModelView;
        public Matrix4x4 World;

        /// <summary>Where this mesh's triangle bands sit in <see cref="_triangleBands"/>.</summary>
        public int FirstTriangleBand;
        public int TriangleBandCount;
    }

    /// <summary>A run of one mesh's vertices or triangles: the unit of work both parallel passes are divided into.</summary>
    private struct CullBand
    {
        public int Job;
        public int From;
        public int To;
    }

    /// <summary>
    /// What one triangle band rejected, so the counters can be summed once at the end rather
    /// than incremented from every thread. The same trick <see cref="RenderStats"/> plays with
    /// its pixel counters, and for the same reason: the cost of a shared counter is not the
    /// increment but the cache line.
    /// </summary>
    private struct CullCounts
    {
        public int BehindView;
        public int FacingBack;
        public int OutOfView;
        public int Drawn;
    }

    // Everything below is grown to the world and reused frame after frame; a steady-state
    // frame allocates none of it.

    private CullJob[] _cullJobs = [];
    private int _cullJobCount;

    /// <summary>How much the two parallel passes have in front of them, in their own units.</summary>
    private int _cullVertexTotal;
    private int _cullTriangleTotal;

    private CullBand[] _vertexBands = [];
    private int _vertexBandCount;

    private CullBand[] _triangleBands = [];
    private int _triangleBandCount;

    /// <summary>
    /// The triangle indices each band kept, in index order. A triangle that straddles the near
    /// plane is stored as its bitwise complement, because clipping one is the thing the merge
    /// has to do sequentially and this is how it is told which those are.
    /// </summary>
    private List<int>[] _bandKept = [];

    private CullCounts[] _bandCounts = [];

    /// <summary>Per mesh slot — the order phase 1 walks the world in — what became of that mesh.</summary>
    private MeshCullOutcome[] _meshOutcome = [];

    /// <summary>Per mesh slot, its index in <see cref="_cullJobs"/>, or -1 when it was rejected.</summary>
    private int[] _meshJob = [];

    /// <summary>
    /// What the two parallel passes need beyond the band they were handed, written before the
    /// pass starts rather than captured by it.
    ///
    /// <para>
    /// A closure would be the obvious way to give a worker its matrices, and it is one object
    /// and one delegate allocated per pass per frame. Every other buffer this file touches is
    /// grown once and reused, and at three hundred frames a second the exception would be the
    /// only thing in the phase making garbage. The passes run one at a time on one renderer, so
    /// there is nothing for two of them to disagree about.
    /// </para>
    /// </summary>
    private List<IMesh>? _passMeshes;
    private VertexBuffer[]? _passBuffers;
    private Matrix4x4 _passProjection;
    private bool _passBackFaceCulling;

    /// <summary>The band bodies as delegates, made once each rather than once a frame.</summary>
    private Action<int>? _vertexBandAction;
    private Action<int>? _triangleBandAction;

    /// <summary>
    /// Transforms, culls and projects the world, and leaves <see cref="_visible"/> and
    /// <see cref="_transparent"/> holding the triangles the fill phase will draw — in exactly
    /// the order a single thread walking the meshes would have produced them.
    /// </summary>
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

    /// <summary>
    /// Pass 1a, sequential: decide which meshes are worth transforming at all, and describe each
    /// survivor to the passes that will.
    ///
    /// <para>
    /// Sequential because it is where the scene graph is read — a mesh's world matrix walks its
    /// parent chain — and because it is the cheap end of the phase either way: two sphere tests
    /// and a matrix multiply per mesh, against the thousands of triangles those two tests decide
    /// the fate of. The occlusion buffer is only read here, which is also what keeps the pass
    /// that reads it off the parallel path entirely.
    /// </para>
    /// </summary>
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

            // Composed from scratch on the unordered path, and read back from the ordering
            // pass on the other — it walks every mesh's parent chain to place it, and doing
            // that twice a frame would be most of what the ordering saves.
            var worldMatrix = meshOrder is null ? mesh.WorldMatrix : _meshWorld[idxVolume];
            var modelViewMatrix = worldMatrix * viewMatrix;

            vbx.Mesh = mesh;
            vbx.WorldMatrix = worldMatrix;

            Stats.TotalTriangleCount += mesh.Triangles.Length;

            _meshJob[slot] = -1;

            // Deactivated from the graphics object table, or faded out entirely.
            if (!mesh.Visible || mesh.Opacity <= 0f)
            {
                _meshOutcome[slot] = MeshCullOutcome.Inactive;
                continue;
            }

            // Whole-mesh rejection: if the mesh's bounding sphere is fully outside the
            // frustum, skip transforming its vertices and culling its triangles.
            //
            // Sized off the world matrix rather than the mesh's own Scale: the centre below
            // already follows the whole scene-graph chain, and a radius that did not would
            // cull a mesh hanging off a scaled node while it is still on screen.
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

                // On screen, and behind something already covering all of it. The same sphere
                // answers both questions, so being hidden costs one more test on a mesh that
                // has survived the cheaper one.
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

                // Transparent meshes collect into their own list; they must blend over the
                // finished opaque image, in back-to-front order.
                Transparent = mesh.Opacity < 1f,

                // A mesh whose normals do not line up with its vertices gets none rather than an
                // index out of range. The old lazy path would have thrown on the first triangle
                // drawn, which is a worse answer to the same malformed mesh.
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

    /// <summary>
    /// Pass 1b, parallel: model space to view, clip and world space, for every vertex of every
    /// surviving mesh.
    ///
    /// <para>
    /// A pure map — each vertex reads its own slot of the mesh and writes its own slot of the
    /// buffer — so the world's vertices divide straight across the cores however they are
    /// distributed between meshes. One sphere of 40,962 and forty thousand cubes of eight
    /// produce the same number of bands and the same balance.
    /// </para>
    ///
    /// <para>
    /// It also does what the per-triangle path used to do lazily, behind a
    /// <c>Proj == Vector4.Zero</c> sentinel that only recomputed a vertex the first time a
    /// triangle asked for it. That memoization is what made the old loop impossible to split:
    /// two triangles sharing a vertex are a read-modify-write of the same struct from two
    /// threads, and the loser of that race silently loses a field. Computing every vertex once,
    /// up front and unconditionally, is what removes the sharing — at the cost of projecting
    /// vertices that only back-facing or off-screen triangles will ask about. On a closed model
    /// that is a little under twice the projections; spread across every core it is still far
    /// less time.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Pass 1c, parallel: decide, for every triangle of every surviving mesh, whether it is
    /// drawn, rejected, or split by the near plane.
    ///
    /// <para>
    /// Read-only on the vertex buffers — pass 1b left nothing to compute — so the only thing a
    /// band writes is its own kept-list and its own counters. What it cannot do is the actual
    /// clipping, which appends to the mesh's shared clipped-geometry lists; a straddling
    /// triangle is marked and handed to the sequential merge instead. That is the right place
    /// for it in any case: a straddler is a triangle touching the camera plane, so a frame
    /// usually has none and never has many.
    /// </para>
    /// </summary>
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

            // Discard if behind the camera
            if (t.IsBehindFarPlane(vbx))
            {
                behindView++;
                continue;
            }

            // Discard if back facing
            if (backFaceCulling && t.IsFacingBack(vbx))
            {
                facingBack++;
                continue;
            }

            // Classify against the near plane (clip-space z >= 0): fully behind is
            // discarded, straddling is clipped, fully in front takes the fast path.
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
                // Discard if outside view frustum
                if (t.IsOutsideFrustum(vbx))
                {
                    outOfView++;
                    continue;
                }

                list.Add(idxTriangle);
                drawn++;
                continue;
            }

            // Straddles the near plane. Marked rather than clipped: splitting it appends
            // vertices and triangles to this mesh's buffer, and the merge is where that can
            // be done one at a time and in order.
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

    /// <summary>
    /// Pass 1d, sequential: walk the meshes in order, append what the bands kept to the frame's
    /// draw lists, clip the triangles that straddle the near plane, and record the phase's
    /// events and statistics.
    ///
    /// <para>
    /// This is the pass that makes the parallel ones safe to have. The draw lists come out in
    /// mesh order and, within a mesh, in triangle order — the order the sequential loop
    /// produced, which is what the nearest-first fill, the pixel probe and the transparent sort
    /// all read. The event log gets one mesh's worth of story at a time, rejections included,
    /// which is why the rejections were recorded in pass 1a rather than emitted there. And the
    /// clipper runs on one thread, so appending to a mesh's clipped geometry needs no lock.
    /// </para>
    ///
    /// <para>
    /// What it costs is one list append per surviving triangle. That is the cheap end of what
    /// the phase does — the transforms, the projections and the six plane tests all happened in
    /// the passes above — and it is why the split is worth having even though this part cannot
    /// be divided.
    /// </para>
    /// </summary>
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

                    // Straddles the near plane: clip into sub-triangles. The new vertices
                    // interpolate world data, which pass 1b computed for every vertex of the
                    // mesh — so unlike the sequential loop this replaced, there is nothing to
                    // transform first.
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

    /// <summary>
    /// Runs one pass's bands, on every core or on this one. <paramref name="work"/> is the
    /// pass's own measure of how much there is to do — vertices or triangles — rather than the
    /// band count, because a hundred bands of eight vertices is not work worth scheduling.
    /// </summary>
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

    /// <summary>
    /// The triangle pass's bands, plus the per-band output they are indexed by. The kept-lists
    /// are created once and reused: a frame over a world the last one already banded allocates
    /// nothing here.
    /// </summary>
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
