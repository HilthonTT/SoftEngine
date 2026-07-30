using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

/// <summary>
/// Temporal antialiasing: this frame averaged with the previous ones, each of which sampled the scene
/// at a different point inside every pixel.
///
/// <para>
/// It is the same trade supersampling makes — more samples per pixel — spread over time rather than
/// over area. <see cref="SuperSampler"/> renders four times the pixels to get four samples each and
/// pays for all four every frame; this renders one and keeps the previous seven, so a still image
/// converges on eight samples per pixel at the cost of one. What it buys over supersampling is the
/// cost; what it gives up is that the samples are only <em>there</em> if the pixel has been looking
/// at the same surface for eight frames, which is what the rest of this class is about.
/// </para>
///
/// <para>
/// Two problems have to be solved for that to hold. A pixel's surface moves, so last frame's colour
/// for it is at a different pixel — which is what <see cref="VelocityBuffer"/> answers. And a
/// surface that has just come out from behind another one has a history belonging to whatever was in
/// front of it, which no reprojection can fix because the information was never rendered: that is
/// what the neighbourhood clamp below is for.
/// </para>
/// </summary>
public sealed class TemporalResolver
{
    /// <summary>Linear RGB of the resolved frame, three floats per pixel — what the next frame blends against.</summary>
    private float[] _history = [];

    private int _width;
    private int _height;

    /// <summary>
    /// How much of each frame is new. 0.1 means a still pixel converges on the average of about ten
    /// frames, which is more than the jitter cycle needs and is the point of diminishing returns for
    /// how long a moving edge keeps a trail.
    /// </summary>
    public float Blend { get; set; } = 0.1f;

    /// <summary>
    /// How far outside the neighbourhood's colour range the history may sit before it is pulled back
    /// in, as a fraction of that range. 0 clamps hard; a little slack keeps the clamp from eating the
    /// very gradients it is meant to preserve.
    /// </summary>
    public float ClampSlack { get; set; } = 0.25f;

    /// <summary>Whether there is a resolved frame to blend against.</summary>
    public bool HasHistory { get; private set; }

    /// <summary>Throws the history away — what a resize, a scene load or a backend switch has to do.</summary>
    public void Reset() => HasHistory = false;

    /// <summary>
    /// Blends the surface with the history and leaves the result in both.
    ///
    /// Runs before the post-process stack, on the render target as the rasterizer left it: the
    /// history has to be of the shaded frame rather than of a tone-mapped, bloomed one, or every
    /// effect in the chain would be applied again to its own output on the next frame.
    /// </summary>
    public void Resolve(FrameBuffer surface, VelocityBuffer velocity)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));
        ArgumentNullException.ThrowIfNull(velocity, nameof(velocity));

        var width = surface.Width;
        var height = surface.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_width != width || _height != height || _history.Length < width * height * 3)
        {
            _history = new float[width * height * 3];

            _width = width;
            _height = height;

            HasHistory = false;
        }

        var reprojects = HasHistory && velocity.IsFilled && velocity.Width == width && velocity.Height == height;

        // The frame as it stands, read once: the blend writes into the surface, and a neighbourhood
        // test that read its own results would compare a pixel against ones already resolved.
        var current = new float[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = Read(surface, x, y);
                var i = (x + y * width) * 3;

                current[i] = color.R;
                current[i + 1] = color.G;
                current[i + 2] = color.B;
            }
        }

        if (!reprojects)
        {
            // First frame, or nothing to reproject with: the frame becomes the history untouched.
            Array.Copy(current, _history, width * height * 3);
            HasHistory = true;

            return;
        }

        var blend = System.Math.Clamp(Blend, 0.01f, 1f);

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var i = (x + y * width) * 3;

                var color = new LinearColor(current[i], current[i + 1], current[i + 2]);

                var motion = velocity.IsCovered(x, y) ? velocity.At(x, y) : Vector2.Zero;

                var previousX = x + 0.5f - motion.X;
                var previousY = y + 0.5f - motion.Y;

                if (previousX < 0f || previousY < 0f || previousX >= width || previousY >= height)
                {
                    // The surface came in from off screen. There is no history for it and nothing to
                    // do but take this frame's sample, which is why the edges of a panning frame are
                    // the one place TAA cannot help.
                    Write(surface, x, y, color);
                    continue;
                }

                var history = Sample(_history, width, height, previousX - 0.5f, previousY - 0.5f);

                // The clamp: whatever the history says, the surface at this pixel cannot plausibly
                // be a colour that does not appear anywhere around it in this frame. Anything
                // outside that range is a sample of something else — of what used to be in front of
                // this surface — and is pulled back to the edge of what is here now.
                history = Clamp(current, width, height, x, y, history, ClampSlack);

                Write(surface, x, y, LinearColor.Lerp(history, color, blend));
            }
        });

        // The history is the frame that was just resolved, which is what the next one has to blend
        // against — not the frame the rasterizer produced.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = Read(surface, x, y);
                var i = (x + y * width) * 3;

                _history[i] = color.R;
                _history[i + 1] = color.G;
                _history[i + 2] = color.B;
            }
        }

        HasHistory = true;
    }

    /// <summary>
    /// Pulls a colour into the range of the 3×3 neighbourhood around a pixel, widened by
    /// <paramref name="slack"/> of its own extent.
    ///
    /// Per channel rather than by luminance: a history whose brightness happens to match while its
    /// hue does not is exactly the ghost this is meant to catch.
    /// </summary>
    private static LinearColor Clamp(float[] frame, int width, int height, int x, int y, LinearColor color, float slack)
    {
        var minR = float.MaxValue;
        var minG = float.MaxValue;
        var minB = float.MaxValue;

        var maxR = float.MinValue;
        var maxG = float.MinValue;
        var maxB = float.MinValue;

        var x0 = System.Math.Max(x - 1, 0);
        var x1 = System.Math.Min(x + 1, width - 1);
        var y0 = System.Math.Max(y - 1, 0);
        var y1 = System.Math.Min(y + 1, height - 1);

        for (var ny = y0; ny <= y1; ny++)
        {
            for (var nx = x0; nx <= x1; nx++)
            {
                var i = (nx + ny * width) * 3;

                minR = MathF.Min(minR, frame[i]);
                maxR = MathF.Max(maxR, frame[i]);

                minG = MathF.Min(minG, frame[i + 1]);
                maxG = MathF.Max(maxG, frame[i + 1]);

                minB = MathF.Min(minB, frame[i + 2]);
                maxB = MathF.Max(maxB, frame[i + 2]);
            }
        }

        return new LinearColor(
            ClampChannel(color.R, minR, maxR, slack),
            ClampChannel(color.G, minG, maxG, slack),
            ClampChannel(color.B, minB, maxB, slack));
    }

    private static float ClampChannel(float value, float min, float max, float slack)
    {
        var margin = (max - min) * slack;

        return System.Math.Clamp(value, min - margin, max + margin);
    }

    /// <summary>Bilinear sample of a float image at a continuous pixel coordinate, clamped at the edges.</summary>
    private static LinearColor Sample(float[] frame, int width, int height, float x, float y)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);

        var tx = x - x0;
        var ty = y - y0;

        var xa = System.Math.Clamp(x0, 0, width - 1);
        var xb = System.Math.Clamp(x0 + 1, 0, width - 1);
        var ya = System.Math.Clamp(y0, 0, height - 1);
        var yb = System.Math.Clamp(y0 + 1, 0, height - 1);

        var top = LinearColor.Lerp(At(frame, width, xa, ya), At(frame, width, xb, ya), tx);
        var bottom = LinearColor.Lerp(At(frame, width, xa, yb), At(frame, width, xb, yb), tx);

        return LinearColor.Lerp(top, bottom, ty);
    }

    private static LinearColor At(float[] frame, int width, int x, int y)
    {
        var i = (x + y * width) * 3;
        return new LinearColor(frame[i], frame[i + 1], frame[i + 2]);
    }

    /// <summary>
    /// The frame's colour at a pixel, in linear light, whichever way the target holds it.
    ///
    /// An HDR target already is linear floats. An 8-bit one is sRGB bytes, and decoding them loses
    /// the range that was clipped out of them — so temporal antialiasing on an 8-bit target averages
    /// clipped values, which is worth doing and is not worth pretending otherwise about.
    /// </summary>
    internal static LinearColor Read(FrameBuffer surface, int x, int y)
    {
        if (!surface.IsHighDynamicRange)
        {
            return Diagnostics.ColorRGB.FromPacked(surface.GetColor(x, y));
        }

        var i = (x + y * surface.Width) * 3;
        var hdr = surface.HdrColor;

        return new LinearColor(hdr[i], hdr[i + 1], hdr[i + 2]);
    }

    /// <summary>
    /// Writes a colour back into the frame without touching the depth buffer — the pass owns every
    /// pixel it writes, having read it first, so there is nothing for a depth test to decide.
    /// </summary>
    internal static void Write(FrameBuffer surface, int x, int y, LinearColor color) =>
        surface.PutBackground(x, y, color);
}
