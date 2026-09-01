using SoftEngine.Core.Buffers;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Pipeline.PostProcess;

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

    public static PostProcessStack CreateDefault()
    {
        var stack = new PostProcessStack();

        stack.Effects.Add(new SsrEffect());

        stack.Effects.Add(new SsaoEffect());
        stack.Effects.Add(new BloomEffect());
        stack.Effects.Add(new ToneMapEffect());
        stack.Effects.Add(new FxaaEffect());
        stack.Effects.Add(new VignetteEffect());

        return stack;
    }

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

    public bool NeedsReflectance
    {
        get
        {
            foreach (var effect in Effects)
            {
                if (effect is { Enabled: true, NeedsReflectance: true })
                {
                    return true;
                }
            }
            return false;
        }
    }

    public void Apply(FrameBuffer surface) => Apply(surface, null);

    public void Apply(FrameBuffer surface, IProjection? projection)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));

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

        if (NeedsReflectance && surface.IsRecordingReflectance)
        {
            surface.ReadReflectance(_target.PrepareReflectance());
        }

        if (surface.IsHighDynamicRange)
        {
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
                screen[pixel] = unchecked((int)0xFF000000)
                    | (ColorSpace.ToSrgb(color[i]) << 16)
                    | (ColorSpace.ToSrgb(color[i + 1]) << 8)
                    | ColorSpace.ToSrgb(color[i + 2]);
            }
        });
    }
}
