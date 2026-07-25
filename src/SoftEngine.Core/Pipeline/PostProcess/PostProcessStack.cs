using SoftEngine.Core.Buffers;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// The chain of full-screen effects applied to a finished render target. The stack owns
/// the conversion at both ends, so effects only ever see linear float RGB and never have
/// to think about the framebuffer's format.
///
/// What that conversion is depends on the target. Against an
/// <see cref="FrameBuffer.IsHighDynamicRange">HDR</see> surface the image arrives already
/// linear and unbounded, and the effects see the highlights as the shader measured them:
/// bloom's threshold picks out what is genuinely bright, and <see cref="ToneMapEffect"/>
/// compresses a real range. Against an 8-bit surface the stack decodes sRGB on the way in,
/// and nothing here can recover highlights the rasterizer already clipped — exposure
/// re-expands the range it was given rather than revealing detail above white.
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
    /// The stack in the order effects are normally composed: ambient occlusion darkens the
    /// creases, bloom gathers the bright parts of what is left, tone mapping compresses the
    /// result, anti-aliasing runs on the final contrast, and the vignette shades the frame
    /// last. All five start disabled.
    /// </summary>
    public static PostProcessStack CreateDefault()
    {
        var stack = new PostProcessStack();

        // Occlusion goes first: it darkens the lighting, and everything after it — what
        // blooms, what the tone-map compresses — should be reacting to the darkened result
        // rather than to light the scene never actually received.
        stack.Effects.Add(new SsaoEffect());
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

    /// <summary>Whether any enabled effect reads the depth buffer.</summary>
    public bool NeedsDepth
    {
        get
        {
            foreach (var effect in Effects)
            {
                if (effect is { Enabled: true, NeedsDepth: true })
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Runs every enabled effect over <paramref name="surface"/>, in order, in place.</summary>
    public void Apply(FrameBuffer surface) => Apply(surface, null);

    /// <summary>
    /// Runs every enabled effect over <paramref name="surface"/>, in order, in place.
    ///
    /// <paramref name="projection"/> is what lets an effect see the scene rather than only
    /// the image: with it, the depth buffer can be turned back into a position per pixel.
    /// Without it — or under a parallel projection, whose depth carries no distance to
    /// recover — depth-reading effects such as <see cref="SsaoEffect"/> find no depth and
    /// do nothing.
    /// </summary>
    public void Apply(FrameBuffer surface, IProjection? projection)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!HasEffects || surface.Width <= 0 || surface.Height <= 0)
        {
            return;
        }

        _target.Resize(surface.Width, surface.Height);

        if (NeedsDepth && projection is not null && surface.HasRecoverableDepth)
        {
            var matrix = projection.ProjectionMatrix(surface.Width, surface.Height);

            surface.ReadViewDepth(_target.PrepareDepth(matrix.M11, matrix.M22));
        }

        if (surface.IsHighDynamicRange)
        {
            // Already the space the effects work in — no decode, and no ceiling.
            Array.Copy(surface.HdrColor, _target.Color, _target.Length);
        }
        else
        {
            Decode(surface.Screen, _target);
        }

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
