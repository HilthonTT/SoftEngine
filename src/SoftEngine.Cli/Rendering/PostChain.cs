using SoftEngine.Cli.Loading;
using SoftEngine.Cli.Options;
using SoftEngine.Core.Pipeline.PostProcess;

namespace SoftEngine.Cli.Rendering;

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

    private static void ScaleToScene(PostProcessStack post, LoadedWorld loaded)
    {
        if (post.Find<SsaoEffect>() is { } ssao)
        {
            ssao.Radius = loaded.Radius * 0.06f;
            ssao.Bias = ssao.Radius * 0.04f;
        }

        if (post.Find<SsrEffect>() is { } ssr)
        {
            ssr.MaxDistance = loaded.Radius * 2f;
            ssr.Thickness = loaded.Radius * 0.08f;
        }
    }
}
