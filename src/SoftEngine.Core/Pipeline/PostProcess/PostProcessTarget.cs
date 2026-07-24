namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// The image a post-process effect works on: linear RGB as three floats per pixel, plus a
/// same-sized scratch buffer for passes that need to read a pixel's neighbours while
/// writing it.
///
/// Float and linear rather than the framebuffer's packed sRGB bytes, because that is the
/// space these operations are defined in — blurring or adding two sRGB-encoded values
/// gives the wrong answer, and 8 bits per channel would band badly across a chain of
/// effects. The stack decodes once on the way in and encodes once on the way out.
/// </summary>
public sealed class PostProcessTarget
{
    private float[] _color = [];
    private float[] _scratch = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Linear RGB, three floats per pixel, row-major.</summary>
    public float[] Color => _color;

    /// <summary>A second buffer of the same shape; its contents are undefined between effects.</summary>
    public float[] Scratch => _scratch;

    /// <summary>Number of floats in use — <c>Width * Height * 3</c>, which may be less than the array length.</summary>
    public int Length => Width * Height * 3;

    internal void Resize(int width, int height)
    {
        Width = width;
        Height = height;

        var length = width * height * 3;
        if (_color.Length >= length)
        {
            return;
        }

        _color = new float[length];
        _scratch = new float[length];
    }

    /// <summary>Copies the image into <see cref="Scratch"/>, so an effect can read the original while it writes.</summary>
    public void SnapshotToScratch() => Array.Copy(_color, _scratch, Length);

    /// <summary>Makes <see cref="Scratch"/> the image and the old image the scratch — a pass that wrote its result there.</summary>
    public void SwapWithScratch() => (_color, _scratch) = (_scratch, _color);
}
