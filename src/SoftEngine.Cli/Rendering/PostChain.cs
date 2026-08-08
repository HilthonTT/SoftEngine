using SoftEngine.Cli.Loading;
using SoftEngine.Cli.Options;
using SoftEngine.Core.Pipeline.PostProcess;

namespace SoftEngine.Cli.Rendering;

/// <summary>
/// Builds the post-process stack: everything off, then whatever <c>--post</c> named turned on.
///
/// The default stack exists so that the effects are constructed and ordered correctly; a batch
/// render should apply what was asked for and nothing else, which is why every effect is disabled
/// before any is enabled.
/// </summary>
internal static class PostChain
{
    public static PostProcessStack Build(RenderOptions options, LoadedWorld loaded)
    {
        var post = PostProcessStack.CreateDefault();

        foreach (var effect in post.Effects)
        {
            effect.Enabled = false;
        }

        ScaleToScene(post, loaded);

        foreach (var name in options.Post)
        {
            var effect = name.ToLowerInvariant() switch
            {
                "ssr" => post.Find<SsrEffect>() as IPostEffect,
                "ssao" => post.Find<SsaoEffect>(),
                "bloom" => post.Find<BloomEffect>(),
                "tonemap" => post.Find<ToneMapEffect>(),
                "fxaa" => post.Find<FxaaEffect>(),
                _ => post.Find<VignetteEffect>(),
            };

            if (effect is not null)
            {
                effect.Enabled = true;
            }
        }

        return post;
    }

    /// <summary>
    /// The effect settings that are distances in the world rather than fractions of the frame, and
    /// so have to be sized to the model that was loaded.
    /// </summary>
    private static void ScaleToScene(PostProcessStack post, LoadedWorld loaded)
    {
        if (post.Find<SsaoEffect>() is { } ssao)
        {
            // A world-space distance, and the one post-process number that has to be scaled to the
            // scene: a radius that finds the creases in a 2-unit skull sees nothing on a 1500-unit
            // elephant.
            ssao.Radius = loaded.Radius * 0.06f;
            ssao.Bias = ssao.Radius * 0.04f;
        }

        if (post.Find<SsrEffect>() is { } ssr)
        {
            // World-space too, and for the same reason: how far a reflected ray may travel, and
            // how thick the depth buffer's one recorded layer is taken to be, are both distances
            // in the scene rather than fractions of the frame.
            ssr.MaxDistance = loaded.Radius * 2f;
            ssr.Thickness = loaded.Radius * 0.08f;
        }
    }
}
