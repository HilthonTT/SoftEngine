using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// The chain of full-screen effects applied to a finished render target. The stack owns
/// the sRGB → linear decode on the way in and the encode on the way out, so effects only
/// ever see linear float RGB and never have to think about the framebuffer's format.
///
/// The source is an 8-bit LDR image, so nothing here recovers highlights the rasterizer
/// already clipped — <see cref="ToneMapEffect"/>'s exposure re-expands the range it was
/// given rather than revealing detail above white.
/// </summary>
public sealed class PostProcessStack
{
    private readonly PostProcessTarget _target = new();

    public List<IPostEffect> Effects { get; } = [];

    public int EnabledCount
    {
        get
        {
            var count = 0;
            foreach (var effect in Effects)
            {
                if (effect.Enabled)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public bool HasEffects => EnabledCount > 0;

    /// <summary>
    /// The stack in the order effects are normally composed: bloom gathers the bright parts
    /// of the raw image, tone mapping compresses the result, anti-aliasing runs on the final
    /// contrast, and the vignette shades the frame last. All four start disabled.
    /// </summary>
    public static PostProcessStack CreateDefault()
    {
        var stack = new PostProcessStack();

        stack.Effects.Add(new BloomEffect());
        stack.Effects.Add(new ToneMapEffect());
        stack.Effects.Add(new FxaaEffect());
        stack.Effects.Add(new VignetteEffect());

        return stack;
    }

    /// <summary>The first effect of the given type in the stack, or null when it holds none.</summary>
    public T? Find<T>() where T : class, IPostEffect
    {
        foreach (var effect in Effects)
        {
            if (effect is T match)
            {
                return match;
            }
        }
        return null;
    }

    /// <summary>Runs every enabled effect over <paramref name="surface"/>, in order, in place.</summary>
    public void Apply(FrameBuffer surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!HasEffects || surface.Width <= 0 || surface.Height <= 0)
        {
            return;
        }

        _target.Resize(surface.Width, surface.Height);

        Decode(surface.Screen, _target);

        foreach (var effect in Effects)
        {
            if (effect.Enabled)
            {
                effect.Apply(_target);
            }
        }

        Encode(_target, surface.Screen);
    }

    private static void Decode(int[] screen, PostProcessTarget target)
    {
        var color = target.Color;
        var width = target.Width;

        Parallel.For(0, target.Height, y =>
        {
            var pixel = y * width;
            var i = pixel * 3;

            for (var x = 0; x < width; x++, pixel++, i += 3)
            {
                var argb = screen[pixel];

                color[i] = ColorSpace.ToLinear((byte)((argb >> 16) & 0xFF));
                color[i + 1] = ColorSpace.ToLinear((byte)((argb >> 8) & 0xFF));
                color[i + 2] = ColorSpace.ToLinear((byte)(argb & 0xFF));
            }
        });
    }

    private static void Encode(PostProcessTarget target, int[] screen)
    {
        var color = target.Color;
        var width = target.Width;

        Parallel.For(0, target.Height, y =>
        {
            var pixel = y * width;
            var i = pixel * 3;

            for (var x = 0; x < width; x++, pixel++, i += 3)
            {
                // Alpha is forced opaque: the render target is presented, never composited.
                screen[pixel] = unchecked((int)0xFF000000)
                    | (ColorSpace.ToSrgb(color[i]) << 16)
                    | (ColorSpace.ToSrgb(color[i + 1]) << 8)
                    | ColorSpace.ToSrgb(color[i + 2]);
            }
        });
    }
}
