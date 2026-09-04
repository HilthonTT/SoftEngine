using SoftEngine.Gpu;
using System.Globalization;
using System.Numerics;

namespace SoftEngine.Cli.Options;

internal static class RenderOptionsParser
{
    public static RenderOptions Parse(string[] args)
    {
        var options = new RenderOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "--output" or "-o":
                    options.Output = Next(args, ref i, options, arg);
                    break;

                case "--scene":
                    options.ScenePath = Next(args, ref i, options, arg);
                    break;

                case "--width" or "-w":
                    options.Width = Int(args, ref i, options, arg, options.Width);
                    break;

                case "--height" or "-h":
                    options.Height = Int(args, ref i, options, arg, options.Height);
                    break;

                case "--painter" or "-p":
                    options.Painter = Next(args, ref i, options, arg) ?? options.Painter;
                    break;

                case "--fill" or "--rasterizer":
                {
                    var name = Next(args, ref i, options, arg);

                    if (name is null)
                    {
                        break;
                    }

                    if (RasterizerModeNames.TryParse(name, out var fill))
                    {
                        options.Fill = fill;
                    }
                    else
                    {
                        options.Errors.Add($"unknown fill '{name}' — expected scanline or half-space");
                    }
                }

                break;

                case "--filter" or "--filtering":
                {
                    var name = Next(args, ref i, options, arg);

                    if (name is null)
                    {
                        break;
                    }

                    if (TextureFilterNames.TryParse(name, out _))
                    {
                        options.Filtering = name;
                    }
                    else
                    {
                        options.Errors.Add($"unknown texture filter '{name}' — expected nearest, bilinear, trilinear or anisotropic");
                    }
                }

                break;

                case "--ss" or "--supersample":
                    options.SuperSampling = Int(args, ref i, options, arg, options.SuperSampling);
                    break;

                case "--no-cull":
                    options.BackFaceCulling = false;
                    break;

                case "--oit":
                    options.OrderIndependentTransparency = true;
                    break;

                case "--wireframe":
                    options.Wireframe = true;
                    break;

                case "--grid":
                    options.Grid = true;
                    break;

                case "--axes":
                    options.Axes = true;
                    break;

                case "--no-sky":
                    options.Sky = false;
                    break;

                case "--environment" or "--env":
                    options.EnvironmentPath = Next(args, ref i, options, arg);
                    break;

                case "--environment-size":
                    options.EnvironmentSize = Int(args, ref i, options, arg, options.EnvironmentSize);
                    break;

                case "--hdr-sky":
                    options.HighDynamicRangeSky = true;
                    break;

                case "--shadows":
                    options.Shadows = true;
                    break;

                case "--cascades":
                    options.Cascades = Int(args, ref i, options, arg, options.Cascades);
                    options.Shadows = true;
                    break;

                case "--view":
                    options.DebugView = Next(args, ref i, options, arg);
                    break;

                case "--post":
                    foreach (var effect in (Next(args, ref i, options, arg) ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        options.Post.Add(effect);
                    }

                    break;

                case "--camera":
                    options.Camera = Vector(args, ref i, options, arg);
                    break;

                case "--yaw":
                    options.Yaw = Degrees(args, ref i, options, arg, options.Yaw);
                    break;

                case "--pitch":
                    options.Pitch = Degrees(args, ref i, options, arg, options.Pitch);
                    break;

                case "--zoom":
                    options.Zoom = Float(args, ref i, options, arg, options.Zoom);
                    break;

                case "--time" or "-t":
                    options.Time = Float(args, ref i, options, arg, options.Time);
                    break;

                case "--frames":
                    options.Frames = Int(args, ref i, options, arg, options.Frames);
                    break;

                case "--fps":
                    options.Fps = Float(args, ref i, options, arg, options.Fps);
                    break;

                case "--turntable":

                    options.Turntable = Float(args, ref i, options, arg, 360f);
                    break;

                case "--shutter":
                    options.Shutter = Float(args, ref i, options, arg, 0.5f);
                    break;

                case "--stats":
                    options.Stats = true;
                    break;

                case "--backend":
                {
                    var name = Next(args, ref i, options, arg);

                    if (name is null)
                    {
                        break;
                    }

                    if (RenderBackends.TryParse(name, out var backend))
                    {
                        options.Backend = backend;
                    }
                    else
                    {
                        options.Errors.Add($"unknown backend '{name}' — expected auto, cpu, gpu or trace");
                    }
                }

                break;

                case "--gpu":
                    options.Backend = RenderBackend.Gpu;
                    break;

                case "--adapter":
                {
                    var name = Next(args, ref i, options, arg);

                    if (name is null)
                    {
                        break;
                    }

                    if (GpuPreferences.TryParse(name, out var preference))
                    {
                        options.GpuPreference = preference;

                        options.Backend = RenderBackend.Gpu;
                    }
                    else
                    {
                        options.Errors.Add($"unknown adapter '{name}' — expected auto, high or low");
                    }
                }

                break;

                case "--cpu":
                    options.Backend = RenderBackend.Cpu;
                    break;

                case "--trace":
                    options.Backend = RenderBackend.Trace;
                    break;

                case "--samples":
                    options.Samples = Int(args, ref i, options, arg, options.Samples);
                    options.Backend = RenderBackend.Trace;
                    break;

                case "--bounces":
                    options.Bounces = Int(args, ref i, options, arg, options.Bounces);
                    options.Backend = RenderBackend.Trace;
                    break;

                case "--physical":
                    options.PhysicalExposure = true;
                    break;

                case "--bake":
                    options.Bake = true;
                    break;

                case "--bake-resolution":
                    options.BakeResolution = Int(args, ref i, options, arg, options.BakeResolution);
                    options.Bake = true;
                    break;

                case "--bake-rays":
                    options.BakeRays = Int(args, ref i, options, arg, options.BakeRays);
                    options.Bake = true;
                    break;

                case "--bake-bounces":
                    options.BakeBounces = Int(args, ref i, options, arg, options.BakeBounces);
                    options.Bake = true;
                    break;

                case "--gpu-info":
                    options.ShowGpuInfo = true;
                    break;

                case "--help" or "-?":
                    options.ShowHelp = true;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        options.Errors.Add($"unknown option '{arg}'");
                    }
                    else if (options.Input is null)
                    {
                        options.Input = arg;
                    }
                    else
                    {
                        options.Errors.Add($"unexpected argument '{arg}' — only one input file is taken");
                    }

                    break;
            }
        }

        RenderOptionsValidation.Validate(options);

        return options;
    }

    #region Argument reading

    private static string? Next(string[] args, ref int i, RenderOptions options, string flag)
    {
        if (i + 1 >= args.Length)
        {
            options.Errors.Add($"{flag} needs a value");
            return null;
        }

        return args[++i];
    }

    private static int Int(string[] args, ref int i, RenderOptions options, string flag, int fallback)
    {
        var text = Next(args, ref i, options, flag);

        if (text is null)
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            options.Errors.Add($"{flag} expects a whole number, got '{text}'");
            return fallback;
        }

        return value;
    }

    private static float Float(string[] args, ref int i, RenderOptions options, string flag, float fallback)
    {
        var text = Next(args, ref i, options, flag);

        if (text is null)
        {
            return fallback;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            options.Errors.Add($"{flag} expects a number, got '{text}'");
            return fallback;
        }

        return value;
    }

    private static float Degrees(string[] args, ref int i, RenderOptions options, string flag, float fallback) =>
        Float(args, ref i, options, flag, fallback * 180f / MathF.PI) * MathF.PI / 180f;

    private static Vector3? Vector(string[] args, ref int i, RenderOptions options, string flag)
    {
        var text = Next(args, ref i, options, flag);

        if (text is null)
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 3 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            options.Errors.Add($"{flag} expects three numbers like 0,2,-8; got '{text}'");
            return null;
        }

        return new Vector3(x, y, z);
    }

    #endregion
}
