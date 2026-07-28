using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Culling;

/// <summary>
/// Drops whole meshes that are hidden behind other meshes, before the frame transforms a
/// single one of their vertices.
///
/// <para>
/// Frustum culling answers "is it on screen"; this answers "is anything already in front of
/// it". The second question is the one that matters in a scene built the way real scenes are —
/// a room, a street, a hillside — where most of what faces the camera is behind something else
/// that also faces the camera, and every bit of it is otherwise transformed, clipped, projected
/// and binned before the depth test gets to say so.
/// </para>
///
/// <para>
/// The pass has three parts and a rule. It picks a few large meshes as occluders, rasterizes
/// them into an <see cref="OcclusionBuffer"/> at a fraction of the frame's resolution, and then
/// tests every other mesh's bounding volume against the pyramid that buffer folds into. The
/// rule is that it may only ever be wrong in the direction of drawing too much: a mesh it
/// fails to reject costs time, and a mesh it rejects wrongly is a hole in the picture.
/// </para>
/// </summary>
public sealed class OcclusionCuller
{
    /// <summary>
    /// How much coarser than the frame the buffer is rasterized, in each direction.
    ///
    /// <para>
    /// Half, which is a quarter of the pixels — and because queries are answered one level up
    /// the pyramid, occlusion is actually decided at a quarter of the frame's resolution in
    /// each direction, over a sixteenth of its pixels. Resolution buys very little beyond that:
    /// the buffer decides whether <em>whole meshes</em> are hidden, and a mesh that is only
    /// hidden at full resolution is one poking out by a pixel, which the test has to decline to
    /// cull in any case.
    /// </para>
    /// </summary>
    public int Divisor { get; set; } = 2;

    /// <summary>Smallest buffer dimension, so a small viewport still gets a pyramid with levels to query.</summary>
    public int MinimumResolution { get; set; } = 64;

    /// <summary>
    /// How much of the frame's height a mesh's bounding sphere must cover to be considered as
    /// an occluder.
    ///
    /// <para>
    /// Not a tuning knob so much as the whole economics of the pass. Rasterizing an occluder
    /// costs real time and only pays it back through the meshes it hides, and a small one hides
    /// almost nothing — a wall covering a third of the screen can hide half the scene, where
    /// ten scattered crates covering a twentieth each hide essentially nothing between them and
    /// cost ten times as much to draw.
    /// </para>
    /// </summary>
    public float MinimumOccluderExtent { get; set; } = 0.18f;

    /// <summary>Most meshes rasterized as occluders in one frame.</summary>
    public int MaximumOccluders { get; set; } = 12;

    /// <summary>
    /// How many meshes a world must hold before the pass runs at all.
    ///
    /// <para>
    /// Rasterizing an occluder is a fixed cost paid up front, and it is repaid one rejected
    /// mesh at a time — so a scene without many meshes to reject cannot repay it however well
    /// it occludes. A handful of nested spheres is the worst case in miniature: every one of
    /// them is enormous on screen and would be chosen, drawing them into the buffer costs more
    /// than the whole rest of the frame, and there is nothing behind them to find. Counting
    /// meshes is a crude way to ask "is there anything here worth rejecting", and it is crude
    /// in the safe direction — the pass declines to run rather than running and charging for
    /// it.
    /// </para>
    /// </summary>
    public int MinimumTestableMeshes { get; set; } = 32;

    /// <summary>
    /// Most triangles rasterized as occluders in one frame. A budget rather than a count,
    /// because "the biggest thing on screen" and "a cheap thing to draw" are unrelated
    /// properties, and the pass has to stay cheap against a scene where the nearest object is
    /// also the densest.
    /// </summary>
    public int TriangleBudget { get; set; } = 6000;

    private readonly OcclusionBuffer _buffer = new();

    // Candidate occluders for this frame, scored by projected size. Reused across frames.
    private readonly List<Candidate> _candidates = [];

    // Which mesh indices were rasterized as occluders, so they are never tested against
    // a buffer they wrote into themselves.
    private bool[] _isOccluder = [];

    // The chosen occluders' mesh indices, and where each one's projected vertices start.
    private readonly List<int> _occluders = [];
    private int[] _vertexOffset = [];

    private Vector4[] _projected = [];

    private Matrix4x4 _projection;
    private bool _prepared;

    private readonly record struct Candidate(int MeshIndex, float ScreenExtent, int TriangleCount);

    /// <summary>The buffer the last <see cref="Prepare"/> filled.</summary>
    public OcclusionBuffer Buffer => _buffer;

    /// <summary>Meshes rasterized as occluders by the last <see cref="Prepare"/>.</summary>
    public int OccluderCount { get; private set; }

    /// <summary>Triangles rasterized by the last <see cref="Prepare"/>.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>Meshes <see cref="IsOccluded"/> has rejected since the last <see cref="Prepare"/>.</summary>
    public int CulledMeshCount { get; private set; }

    /// <summary>
    /// Chooses this frame's occluders and rasterizes them. Must be called before
    /// <see cref="IsOccluded"/>, and is a no-op that rejects nothing when the scene offers
    /// nothing big enough to be worth drawing.
    /// </summary>
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
        CulledMeshCount = 0;

        var meshCount = world.Meshes.Count;

        // Checked before anything is measured or cleared, so a scene the pass cannot help pays
        // nothing at all for having asked.
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

        // Selection first, and the buffer cleared only once there is something to put in it.
        // A world full of meshes with nothing large enough among them reaches this point on
        // every frame, and wiping a quarter-million texels for it is a cost with no possible
        // return.
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

    /// <summary>
    /// Finds the meshes worth rasterizing, largest on screen first, and marks them. Returns
    /// false when none qualify.
    /// </summary>
    private bool Select(
        List<IMesh> meshes,
        in Matrix4x4 viewMatrix,
        in Matrix4x4 projectionMatrix,
        ReadOnlySpan<Vector4> frustumPlanes)
    {
        _candidates.Clear();

        // How much of the frame's half-height one world unit spans at one unit of view depth.
        // The projection's vertical scale is exactly that, in the [-1, 1] clip space the
        // extents below are measured in, so the frame's pixel dimensions never enter into it.
        var verticalScale = projectionMatrix.M22;

        if (verticalScale <= 0f)
        {
            return false;
        }

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            // The same exclusions the shadow pass makes, for the same reason: something you can
            // see through does not hide what is behind it, and a mesh dropped from the frame
            // must not go on hiding things after it has gone.
            if (!mesh.Visible || mesh.Opacity < 1f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var worldMatrix = mesh.WorldMatrix;
            var radius = mesh.BoundingRadius * MeshExtensions.MaxScale(worldMatrix);

            if (!float.IsFinite(radius) || radius <= 0f)
            {
                continue;
            }

            var center = Vector3.Transform(Vector3.Zero, worldMatrix * viewMatrix);

            if (Frustum.IsSphereOutside(frustumPlanes, center, radius))
            {
                continue;
            }

            // Looking down -Z, so depth is -Z. Only a mesh centred at or behind the eye is
            // rejected, and only because there is no projected size to compute for one.
            //
            // Explicitly *not* rejected: a mesh whose bounding sphere reaches the camera. That
            // test looks reasonable and throws away the best occluder in most scenes — a wall
            // is a flat thing with a bounding sphere as wide as its diagonal, so a wall filling
            // the view from a few units away has a sphere that swallows the camera while every
            // triangle in it sits comfortably in front. Triangles that really do straddle the
            // near plane are dropped one at a time by the rasterizer, which is where a question
            // about a triangle belongs.
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
                // Not a break: a later candidate may be smaller on screen and cheap enough to
                // fit in what the budget has left, and taking it costs nothing.
                continue;
            }

            _isOccluder[candidate.MeshIndex] = true;
            triangles += candidate.TriangleCount;
            chosen++;
        }

        OccluderCount = chosen;
        return chosen > 0;
    }

    /// <summary>
    /// Projects the chosen occluders and fills the buffer with them.
    ///
    /// <para>
    /// The occluders are exactly the largest things on screen, so the fill is a small number of
    /// triangles covering a great many texels — which is the shape that wants splitting by rows
    /// rather than by triangle. Every worker walks the whole list and writes only its own band,
    /// so no texel has two writers and the buffer needs no locking.
    /// </para>
    /// </summary>
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

            // Transformed once per vertex rather than once per corner: a closed mesh shares
            // every vertex between several triangles, and the transform is the expensive half.
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

        // One band is cheaper than the scheduling when there is little to fill.
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

    /// <summary>
    /// Whether the mesh at <paramref name="meshIndex"/>, whose view-space bounding sphere is
    /// given, is entirely behind this frame's occluders.
    /// </summary>
    /// <param name="viewCenter">The sphere's centre in view space — the same one the frustum test uses.</param>
    /// <param name="radius">The sphere's radius, already scaled by the whole scene-graph chain.</param>
    public bool IsOccluded(int meshIndex, Vector3 viewCenter, float radius)
    {
        if (!_prepared || !_buffer.HasOccluders)
        {
            return false;
        }

        // An occluder is never tested against the buffer it helped write. The conservative
        // sphere makes it impossible for one to hide itself in any case, but a mesh vanishing
        // because of its own depth is a bug worth being unable to write rather than merely
        // unlikely to hit.
        if ((uint)meshIndex < (uint)_isOccluder.Length && _isOccluder[meshIndex])
        {
            return false;
        }

        if (!float.IsFinite(radius) || radius <= 0f)
        {
            return false;
        }

        // The sphere's axis-aligned box, projected corner by corner. A box rather than the
        // sphere because a sphere's silhouette under a perspective projection is a conic, and
        // the box contains it — so every bound this produces is at least as generous as the
        // truth, which is the direction the whole pass has to err in.
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

            // A corner at or behind the eye: the projection says nothing usable about where
            // this mesh is on screen, so nothing is claimed about it.
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

        CulledMeshCount++;
        return true;
    }

    /// <summary>Forgets the last frame, so a stale buffer cannot reject anything in the next one.</summary>
    public void Reset()
    {
        _prepared = false;
        OccluderCount = 0;
        TriangleCount = 0;
        CulledMeshCount = 0;
    }
}
