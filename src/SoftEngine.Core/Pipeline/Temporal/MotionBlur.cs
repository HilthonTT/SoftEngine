using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Pipeline.Temporal;

/// <summary>
/// Smears each pixel along the direction its surface is travelling.
///
/// <para>
/// A rendered frame is a shutter open for no time at all, which is why fast motion in one strobes
/// rather than blurring. A real camera integrates over the time the shutter is open, and the image of
/// a moving surface is spread along the path it took. With a velocity per pixel that path is known,
/// so the integral can be approximated the cheap way: average the frame along the line the surface
/// swept, one sample per step.
/// </para>
///
/// <para>
/// It is a screen-space approximation and it shows in one specific way. The samples come from the
/// frame as it is, so a fast-moving object smears the <em>background</em> into itself where it has
/// come from — the pixels it moved across hold what is behind it, because nothing ever rendered what
/// was under it a fraction of a frame ago. Sampling only where the velocity agrees keeps that from
/// bleeding across silhouettes in the other direction, which is the artefact people notice.
/// </para>
///
/// <para>
/// This is not an <see cref="PostProcess.IPostEffect"/>, though it belongs in that part of a frame:
/// the stack's effects read the image and a depth buffer, and this needs the velocity buffer, which
/// no post-process pass is handed. Adding it to that interface for one effect would make every other
/// one carry the parameter.
/// </para>
/// </summary>
public sealed class MotionBlur
{
    /// <summary>
    /// What fraction of a frame's motion the shutter is open for. 0.5 — the "180° shutter" of film —
    /// smears a surface across half the distance it moved, which is what cinema looks like; 1 smears
    /// the whole way and reads as a smear.
    /// </summary>
    public float ShutterFraction { get; set; } = 0.5f;

    /// <summary>Samples taken along the smear. More is smoother and linearly more expensive.</summary>
    public int Samples { get; set; } = 8;

    /// <summary>
    /// The furthest a pixel may be smeared, in pixels. A surface crossing the frame in one frame
    /// would otherwise gather from half the image and take the frame time with it.
    /// </summary>
    public float MaxLength { get; set; } = 48f;

    /// <summary>
    /// Blurs the frame along its velocities. Does nothing without a filled velocity buffer, and
    /// nothing when the largest motion in the frame is under a pixel — which is most frames, and
    /// which is why this is worth checking before allocating anything.
    /// </summary>
    public void Apply(FrameBuffer surface, VelocityBuffer velocity)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));
        ArgumentNullException.ThrowIfNull(velocity, nameof(velocity));

        var width = surface.Width;
        var height = surface.Height;

        if (!velocity.IsFilled ||
            velocity.Width != width ||
            velocity.Height != height ||
            width <= 0 || height <= 0)
        {
            return;
        }

        var shutter = MathF.Max(0f, ShutterFraction);
        var samples = System.Math.Max(2, Samples);

        if (shutter <= 0f || velocity.MaxSpeed() * shutter < 1f)
        {
            return;
        }

        // The frame as it stands. Every sample has to come from the unblurred image, or the blur
        // would compound along whichever direction the loop happens to run in.
        var source = new float[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = TemporalResolver.Read(surface, x, y);
                var i = (x + y * width) * 3;

                source[i] = color.R;
                source[i + 1] = color.G;
                source[i + 2] = color.B;
            }
        }

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                if (!velocity.IsCovered(x, y))
                {
                    continue;
                }

                var motion = velocity.At(x, y) * shutter;
                var length = motion.Length();

                if (length < 1f)
                {
                    continue;
                }

                if (length > MaxLength)
                {
                    motion *= MaxLength / length;
                }

                float r = 0f, g = 0f, b = 0f;
                var taken = 0;

                for (var s = 0; s < samples; s++)
                {
                    // Centred on the pixel and spread both ways along the path: the surface was at
                    // one end at the start of the shutter and at the other by the end of it, and the
                    // pixel is the middle of that.
                    var t = s / (float)(samples - 1) - 0.5f;

                    var sx = (int)MathF.Round(x - motion.X * t);
                    var sy = (int)MathF.Round(y - motion.Y * t);

                    if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                    {
                        continue;
                    }

                    var i = (sx + sy * width) * 3;

                    r += source[i];
                    g += source[i + 1];
                    b += source[i + 2];

                    taken++;
                }

                if (taken == 0)
                {
                    continue;
                }

                var scale = 1f / taken;

                TemporalResolver.Write(surface, x, y, new LinearColor(r * scale, g * scale, b * scale));
            }
        });
    }
}
