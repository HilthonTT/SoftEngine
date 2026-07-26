using System.Numerics;

namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// The image a post-process effect works on: linear RGB as three floats per pixel, plus a
/// same-sized scratch buffer for passes that need to read a pixel's neighbours while
/// writing it.
///
/// Float and linear rather than the framebuffer's packed sRGB bytes, because that is the
/// space these operations are defined in — blurring or adding two sRGB-encoded values
/// gives the wrong answer, and 8 bits per channel would band badly across a chain of
/// effects. The stack converts once on the way in and once on the way out.
///
/// Effects that ask for it (<see cref="IPostEffect.NeedsDepth"/>) also get
/// <see cref="ViewDepth"/> and enough of the projection to turn a pixel back into the
/// point in space it came from — which is what separates an effect that filters an image
/// from one that knows anything about the scene behind it.
/// </summary>
public sealed class PostProcessTarget
{
    private float[] _color = [];
    private float[] _scratch = [];
    private float[] _viewDepth = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Linear RGB, three floats per pixel, row-major.</summary>
    public float[] Color => _color;

    /// <summary>A second buffer of the same shape; its contents are undefined between effects.</summary>
    public float[] Scratch => _scratch;

    /// <summary>Number of floats in use — <c>Width * Height * 3</c>, which may be less than the array length.</summary>
    public int Length => Width * Height * 3;

    /// <summary>
    /// View-space distance at every pixel, one float each, or an empty array when the frame
    /// carries no usable depth. Background pixels hold <see cref="float.PositiveInfinity"/>.
    /// </summary>
    public float[] ViewDepth => _viewDepth;

    /// <summary>Whether <see cref="ViewDepth"/> and <see cref="ViewPositionAt"/> are meaningful this frame.</summary>
    public bool HasDepth { get; private set; }

    /// <summary>How far a view-space unit at unit distance stretches across the screen, per axis.</summary>
    public float ProjectionScaleX { get; private set; } = 1f;

    public float ProjectionScaleY { get; private set; } = 1f;

    /// <summary>
    /// The point in view space a pixel shows, or a position at infinity for background.
    /// The inverse of the projection, for the one pixel: undo the screen mapping to get a
    /// normalized device coordinate, undo the projection's scale to get a direction, and
    /// scale that by the distance the depth buffer recorded.
    ///
    /// A coordinate outside the frame counts as background: there is no recorded geometry
    /// there, which is exactly what background means. Saying so is what keeps an effect that
    /// walks a pixel's neighbours from indexing past the end of the buffer at the border —
    /// and the depth buffer is only ever grown, never shrunk, so an overrun would otherwise
    /// read a stale pixel from a larger frame on most frames and throw on the rest.
    /// </summary>
    public Vector3 ViewPositionAt(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return new Vector3(0f, 0f, float.NegativeInfinity);
        }

        var w = _viewDepth[x + y * Width];

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

    /// <summary>Where a view-space point lands on screen, in pixels. The inverse of <see cref="ViewPositionAt"/>.</summary>
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

    internal void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        HasDepth = false;

        var length = width * height * 3;
        if (_color.Length >= length)
        {
            return;
        }

        _color = new float[length];
        _scratch = new float[length];
    }

    /// <summary>Reserves the depth buffer and records the projection it is to be read against.</summary>
    internal float[] PrepareDepth(float projectionScaleX, float projectionScaleY)
    {
        var count = Width * Height;
        if (_viewDepth.Length < count)
        {
            _viewDepth = new float[count];
        }

        ProjectionScaleX = projectionScaleX;
        ProjectionScaleY = projectionScaleY;
        HasDepth = true;

        return _viewDepth;
    }

    /// <summary>Copies the image into <see cref="Scratch"/>, so an effect can read the original while it writes.</summary>
    public void SnapshotToScratch() => Array.Copy(_color, _scratch, Length);

    /// <summary>Makes <see cref="Scratch"/> the image and the old image the scratch — a pass that wrote its result there.</summary>
    public void SwapWithScratch() => (_color, _scratch) = (_scratch, _color);
}
