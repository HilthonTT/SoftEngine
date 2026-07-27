using SoftEngine.Core.Buffers;
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
    /// The depth buffer read as geometry — positions and normals per pixel. Only meaningful
    /// while <see cref="HasDepth"/>; see <see cref="DepthField"/>.
    /// </summary>
    public DepthField Field => new(_viewDepth, Width, Height, ProjectionScaleX, ProjectionScaleY);

    /// <inheritdoc cref="DepthField.PositionAt"/>
    public Vector3 ViewPositionAt(int x, int y) => Field.PositionAt(x, y);

    /// <inheritdoc cref="DepthField.ProjectToScreen"/>
    public bool ProjectToScreen(Vector3 viewPosition, out int x, out int y, out float distance) =>
        Field.ProjectToScreen(viewPosition, out x, out y, out distance);

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
