namespace SoftEngine.Cli.Options;

internal static class RenderOptionsValidation
{
    public static void Validate(RenderOptions options)
    {
        if (options.ShowHelp || options.ShowGpuInfo)
        {
            return;
        }

        if (options.Input is null)
        {
            options.Errors.Add("no input file — name a model (.obj, .dae, .gltf, .glb) or a scene (.json)");
        }
        else if (!File.Exists(options.Input))
        {
            options.Errors.Add($"'{options.Input}' does not exist");
        }

        if (options.ScenePath is { } scene && !File.Exists(scene))
        {
            options.Errors.Add($"'{scene}' does not exist");
        }

        if (options.Width is < 1 or > 16384 || options.Height is < 1 or > 16384)
        {
            options.Errors.Add("width and height must be between 1 and 16384");
        }

        if (options.SuperSampling is < 1 or > 4)
        {
            options.Errors.Add("--ss must be between 1 and 4");
        }

        if (options.Cascades is < 1 or > 4)
        {
            options.Errors.Add("--cascades must be between 1 and 4");
        }

        if (options.Frames is < 1 or > 100000)
        {
            options.Errors.Add("--frames must be between 1 and 100000");
        }

        if (options.Fps is <= 0f or > 1000f)
        {
            options.Errors.Add("--fps must be between 0 and 1000");
        }

        if (options.Shutter is < 0f or > 4f)
        {
            options.Errors.Add("--shutter must be between 0 and 4");
        }

        if (options.Samples is < 1 or > 65536)
        {
            options.Errors.Add("--samples must be between 1 and 65536");
        }

        if (options.Bounces is < 0 or > 64)
        {
            options.Errors.Add("--bounces must be between 0 and 64");
        }

        if (options.BakeResolution is < 2 or > 64)
        {
            options.Errors.Add("--bake-resolution must be between 2 and 64");
        }

        if (options.BakeRays is < 1 or > 65536)
        {
            options.Errors.Add("--bake-rays must be between 1 and 65536");
        }

        if (options.BakeBounces is < 0 or > 64)
        {
            options.Errors.Add("--bake-bounces must be between 0 and 64");
        }

        if (options.EnvironmentPath is { } environment && !File.Exists(environment))
        {
            options.Errors.Add($"'{environment}' does not exist");
        }

        if (options.EnvironmentSize is not 0 and (< 8 or > 512))
        {
            options.Errors.Add("--environment-size must be between 8 and 512");
        }

        foreach (var effect in options.Post)
        {
            if (effect is not ("ssr" or "ssao" or "bloom" or "tonemap" or "fxaa" or "vignette"))
            {
                options.Errors.Add($"unknown post effect '{effect}'");
            }
        }
    }
}
