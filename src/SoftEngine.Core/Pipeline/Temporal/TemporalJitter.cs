using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

/// <summary>
/// The sub-pixel offsets temporal antialiasing moves the camera by, and how to put one into a
/// projection matrix.
///
/// <para>
/// This is where the antialiasing actually comes from, and it is worth being clear about why.
/// Blending consecutive frames on its own only steadies an image: every frame samples the scene at
/// the same point in every pixel, so an edge that lands two-thirds of the way across a pixel is
/// resolved as covering all of it or none of it, over and over, and averaging that answer with
/// itself changes nothing. Shifting the camera by a fraction of a pixel each frame makes each frame
/// sample the scene <em>somewhere else</em> inside the pixel — and then the average over eight
/// frames is eight samples per pixel, which is what supersampling buys by rendering eight times the
/// area in one frame instead.
/// </para>
///
/// <para>
/// The offsets are the Halton sequence in bases 2 and 3, centred on zero. A low-discrepancy sequence
/// rather than random offsets because eight random points inside a pixel will clump and leave gaps;
/// Halton fills the square evenly at every prefix length, so the average is worth its sample count
/// after four frames as well as after eight.
/// </para>
/// </summary>
public static class TemporalJitter
{
    /// <summary>
    /// Offsets in the cycle. Eight is the usual choice: long enough that the average is a real
    /// distribution over the pixel, short enough that a scene which starts moving again has not
    /// spent a quarter of a second converging.
    /// </summary>
    public const int Phases = 8;

    /// <summary>
    /// The offset for a frame, in pixels, inside [-0.5, 0.5] on each axis.
    /// </summary>
    public static Vector2 Offset(long frame)
    {
        var index = (int)(frame % Phases);

        // The sequence is 1-based: Halton's first point is the origin, which would waste a phase
        // sampling exactly where an unjittered frame already does.
        return new Vector2(
            Halton(index + 1, 2) - 0.5f,
            Halton(index + 1, 3) - 0.5f);
    }

    /// <summary>
    /// Bends a projection matrix by a sub-pixel offset.
    ///
    /// <para>
    /// Not a translation of the scene and not a change of the frustum: the third row of a projection
    /// matrix is what multiplies view z, and view z is what becomes clip w — so adding to it shifts
    /// clip x and y by a constant fraction of w. That is a shift of a constant number of
    /// <em>pixels</em>, whatever the depth, which is exactly what jittering the sample grid means and
    /// is not what moving the camera sideways would do.
    /// </para>
    /// </summary>
    /// <param name="offset">The offset in pixels, from <see cref="Offset"/>.</param>
    public static Matrix4x4 Apply(Matrix4x4 projection, Vector2 offset, int width, int height)
    {
        if (width <= 1 || height <= 1 || offset == Vector2.Zero)
        {
            return projection;
        }

        // Pixels to normalized device coordinates, which span 2 across the frame. The mapping puts
        // NDC ±1 on the centres of the outermost pixels, so the span is width − 1 of them.
        var ndcX = 2f * offset.X / (width - 1);
        var ndcY = 2f * offset.Y / (height - 1);

        // A parallel projection carries no w at all — clip.w is 1 — so the offset goes into the
        // translation row and shifts every depth by the same amount, which for a projection with no
        // foreshortening is the same statement.
        if (MathF.Abs(projection.M34) < 1e-6f)
        {
            projection.M41 += ndcX;
            projection.M42 += ndcY;

            return projection;
        }

        // Row-vector convention: clip.x = … + view.z · M31, and clip.w = −view.z for a perspective
        // projection, so subtracting gives clip.x += offset · clip.w.
        projection.M31 -= ndcX;
        projection.M32 -= ndcY;

        return projection;
    }

    /// <summary>
    /// The i'th point of the Halton sequence in a base: i written in that base, with its digits
    /// reflected about the point. The one-dimensional building block of a low-discrepancy grid.
    /// </summary>
    public static float Halton(int index, int radix)
    {
        var result = 0f;
        var fraction = 1f / radix;

        while (index > 0)
        {
            result += fraction * (index % radix);

            index /= radix;
            fraction /= radix;
        }

        return result;
    }
}
