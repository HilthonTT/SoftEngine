using System.Numerics;

namespace SoftEngine.Core.Buffers;

/// <summary>
/// A depth buffer read as geometry: one view-space distance per pixel, plus enough of the
/// projection to turn a pixel back into the point in space it came from.
///
/// This is the partial model of the scene that a finished frame leaves behind. Given a
/// distance per pixel and the projection that produced it, a position follows by undoing the
/// projection, and a surface orientation follows by differencing neighbouring positions —
/// which is all that separates an effect that filters an image from one that knows something
/// about the scene behind it. Screen-space ambient occlusion and the normals buffer view are
/// two readings of the same structure, so they share it rather than each deriving it.
///
/// Background pixels hold <see cref="float.PositiveInfinity"/>, and the positions derived
/// from them are pushed to negative infinity along Z, so a caller that forgets to check gets
/// an obviously wrong answer instead of a plausible one.
/// </summary>
public readonly struct DepthField(float[] depth, int width, int height, float projectionScaleX, float projectionScaleY)
{
    private readonly float[] _depth = depth;

    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>How far a view-space unit at unit distance stretches across the screen, per axis.</summary>
    public float ProjectionScaleX { get; } = projectionScaleX;

    public float ProjectionScaleY { get; } = projectionScaleY;

    /// <summary>View-space distance at every pixel, row-major.</summary>
    public float[] Depth => _depth;

    /// <summary>
    /// The point in view space a pixel shows, or a position at infinity for background.
    /// Undo the screen mapping to get a normalized device coordinate, undo the projection's
    /// scale to get a direction, and scale that by the distance the depth buffer recorded.
    ///
    /// A coordinate outside the frame counts as background: there is no recorded geometry
    /// there, which is exactly what background means. Saying so is what keeps a caller that
    /// walks a pixel's neighbours from indexing past the end of the buffer at the border —
    /// and the buffer is only ever grown, never shrunk, so an overrun would otherwise read a
    /// stale pixel from a larger frame on most frames and throw on the rest.
    /// </summary>
    public Vector3 PositionAt(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return new Vector3(0f, 0f, float.NegativeInfinity);
        }

        var w = _depth[x + y * Width];

        if (float.IsPositiveInfinity(w))
        {
            return new Vector3(0f, 0f, float.NegativeInfinity);
        }

        // Matching FrameBuffer.ToScreen3, which maps NDC ±1 onto pixel 0 and pixel n - 1.
        var ndcX = x * (2f / MathF.Max(Width - 1, 1)) - 1f;
        var ndcY = 1f - y * (2f / MathF.Max(Height - 1, 1));

        // The view looks down -Z, so a point at distance w sits at z = -w.
        return new Vector3(ndcX * w / ProjectionScaleX, ndcY * w / ProjectionScaleY, -w);
    }

    /// <summary>Where a view-space point lands on screen, in pixels. The inverse of <see cref="PositionAt"/>.</summary>
    public bool ProjectToScreen(Vector3 viewPosition, out int x, out int y, out float distance)
    {
        distance = -viewPosition.Z;

        if (distance <= 1e-6f)
        {
            x = 0;
            y = 0;
            return false;
        }

        var ndcX = viewPosition.X * ProjectionScaleX / distance;
        var ndcY = viewPosition.Y * ProjectionScaleY / distance;

        x = (int)((ndcX + 1f) * 0.5f * MathF.Max(Width - 1, 1) + 0.5f);
        y = (int)((1f - ndcY) * 0.5f * MathF.Max(Height - 1, 1) + 0.5f);

        return (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    /// <summary>
    /// The surface normal at a pixel, from the two neighbours on each side. Whichever of
    /// the forward and backward differences spans the smaller depth step is used, so a
    /// pixel on a silhouette takes its normal from the surface it belongs to rather than
    /// from the gap across the edge.
    ///
    /// Returns <see cref="Vector3.Zero"/> where no normal can be derived — background, or a
    /// pixel with nothing usable beside it.
    /// </summary>
    public Vector3 NormalAt(int x, int y)
    {
        var width = Width;
        var height = Height;

        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return Vector3.Zero;
        }

        var here = _depth[x + y * width];

        var left = x > 0 ? _depth[x - 1 + y * width] : float.PositiveInfinity;
        var right = x < width - 1 ? _depth[x + 1 + y * width] : float.PositiveInfinity;
        var up = y > 0 ? _depth[x + (y - 1) * width] : float.PositiveInfinity;
        var down = y < height - 1 ? _depth[x + (y + 1) * width] : float.PositiveInfinity;

        var origin = PositionAt(x, y);

        // Whichever side spans the smaller depth step — but never a pixel off the edge of the
        // frame. At a border one of the two neighbours does not exist, and the other one has
        // to be used however the depths compare: when both are background the comparison is a
        // tie between two infinities, which resolves to "take the far side" and walks straight
        // off the end of the row.
        var useLeft = x > 0 && (x == width - 1 || Closer(here, left, right));
        var useUp = y > 0 && (y == height - 1 || Closer(here, up, down));

        var horizontal = useLeft
            ? PositionAt(x - 1, y) - origin
            : origin - PositionAt(x + 1, y);

        var vertical = useUp
            ? PositionAt(x, y - 1) - origin
            : origin - PositionAt(x, y + 1);

        // A neighbour that is background sits at infinity, and the difference against it
        // carries that through — Z is where it lands, since that is the axis distance runs
        // along. A pixel with nothing usable beside it gets no normal.
        if (!float.IsFinite(horizontal.Z) || !float.IsFinite(vertical.Z))
        {
            return Vector3.Zero;
        }

        var normal = Vector3.Cross(vertical, horizontal);

        if (normal.LengthSquared() < 1e-16f)
        {
            return Vector3.Zero;
        }

        normal = Vector3.Normalize(normal);

        // A visible surface faces the eye, which in view space sits at the origin: the ray to
        // the pixel is the pixel's own position, and a front-facing normal opposes it.
        //
        // Tested against that ray rather than against the view axis. The axis test — "flip it
        // if Z points away" — is the same statement only for a surface square to the camera,
        // and it fails exactly where it matters: a ground plane seen at a shallow angle has a
        // normal almost perpendicular to the axis, so the Z component that decides the flip is
        // a rounding error, and its sign alternates from one row of pixels to the next. The
        // result is a floor that stripes. The ray carries the pixel's full offset from the
        // centre of the frame, so on that same floor the test is decided by a term the size of
        // the camera's height above it.
        return Vector3.Dot(normal, origin) > 0f ? -normal : normal;
    }

    private static bool Closer(float here, float a, float b) =>
        MathF.Abs(a - here) < MathF.Abs(b - here);
}
