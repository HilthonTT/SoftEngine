using SoftEngine.Core.Geometry;
using SoftEngine.Gpu;
using System.Globalization;
using System.Numerics;

namespace SoftEngine.Cli;

/// <summary>Everything the command line can say about one render.</summary>
internal sealed class RenderOptions
{
    /// <summary>The model or scene file to render. Required unless <see cref="ShowHelp"/>.</summary>
    public string? Input { get; set; }

    /// <summary>Where the PNG goes. Defaults to the input's name with a .png extension.</summary>
    public string? Output { get; set; }

    /// <summary>A scene document applied over the loaded model, when one was named separately.</summary>
    public string? ScenePath { get; set; }

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public string Painter { get; set; } = "gouraud";

    /// <summary>
    /// How the painters that sample textures filter them: <c>nearest</c>, <c>bilinear</c> or
    /// <c>trilinear</c>. Anything else is reported rather than guessed at.
    /// </summary>
    public string Filtering { get; set; } = "bilinear";

    public int SuperSampling { get; set; } = 1;

    public bool BackFaceCulling { get; set; } = true;

    /// <summary>
    /// Whether transparent surfaces are resolved per pixel rather than by sorting the triangles
    /// that produced them. Matters only where transparent geometry overlaps itself.
    /// </summary>
    public bool OrderIndependentTransparency { get; set; }

    public bool Wireframe { get; set; }

    public bool Grid { get; set; }

    public bool Axes { get; set; }

    public bool Sky { get; set; } = true;

    /// <summary>A panorama to light and surround the scene with, instead of the procedural sky.</summary>
    public string? EnvironmentPath { get; set; }

    /// <summary>Cube face resolution the panorama is projected onto. Zero derives it from the source.</summary>
    public int EnvironmentSize { get; set; }

    /// <summary>
    /// Whether the procedural sky is built in linear light with a sun some hundreds of times
    /// brighter than paper white, rather than clipped into bytes.
    /// </summary>
    public bool HighDynamicRangeSky { get; set; }

    public bool Shadows { get; set; }

    public int Cascades { get; set; } = 1;

    public string? DebugView { get; set; }

    /// <summary>Post effects named on the command line, lower-cased.</summary>
    public HashSet<string> Post { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An explicit camera position, or null to frame the model automatically.</summary>
    public Vector3? Camera { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; } = 15f * MathF.PI / 180f;

    /// <summary>Multiplies the automatically framed distance. Ignored when <see cref="Camera"/> is set.</summary>
    public float Zoom { get; set; } = 1f;

    /// <summary>
    /// How far into the model's animation to render, in seconds.
    ///
    /// A single number rather than a frame index, because a clip's own time is in seconds and a
    /// "frame" would need a rate this program has no reason to invent. Zero renders the rest pose,
    /// which is what a static model has anyway.
    /// </summary>
    public float Time { get; set; }

    /// <summary>
    /// How many frames to render. Above 1 writes a numbered sequence and advances the animation by
    /// <see cref="Fps"/> between them.
    /// </summary>
    public int Frames { get; set; } = 1;

    /// <summary>Frames per second the sequence represents — what sets the step between them.</summary>
    public float Fps { get; set; } = 30f;

    /// <summary>
    /// Degrees of yaw swept across the whole sequence. 360 is a turntable; 0 leaves the camera where
    /// <see cref="Yaw"/> put it.
    /// </summary>
    public float Turntable { get; set; }

    /// <summary>
    /// Shutter fraction for motion blur, or 0 for none. Only means anything in a sequence: a single
    /// frame has no previous one to have moved from.
    /// </summary>
    public float Shutter { get; set; }

    public bool Stats { get; set; }

    /// <summary>Which rasterizer draws the frame. Automatic takes a GPU when there is one.</summary>
    public RenderBackend Backend { get; set; } = RenderBackend.Automatic;

    /// <summary>Paths per pixel, when the path tracer is drawing the frame.</summary>
    public int Samples { get; set; } = 16;

    /// <summary>Bounces of indirect light the path tracer follows. 0 is direct lighting only.</summary>
    public int Bounces { get; set; } = 3;

    /// <summary>
    /// Whether the path tracer puts direct and bounced light on the same scale, rather than
    /// matching the rasterizer's π exposure correction for direct light.
    /// </summary>
    public bool PhysicalExposure { get; set; }

    /// <summary>
    /// Whether indirect light is measured into an irradiance volume before the frame is rasterized,
    /// instead of standing in for it with the environment's six directional averages.
    /// </summary>
    public bool Bake { get; set; }

    /// <summary>Probes along the world's longest axis.</summary>
    public int BakeResolution { get; set; } = 12;

    /// <summary>Paths traced out of each probe.</summary>
    public int BakeRays { get; set; } = 128;

    /// <summary>Bounces each of those paths may take.</summary>
    public int BakeBounces { get; set; } = 2;

    /// <summary>Print what graphics adapter is available and exit, rendering nothing.</summary>
    public bool ShowGpuInfo { get; set; }

    public bool ShowHelp { get; set; }

    /// <summary>Anything the parser could not make sense of, reported all at once.</summary>
    public List<string> Errors { get; } = [];

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

                case "--filter" or "--filtering":
                {
                    var name = Next(args, ref i, options, arg);

                    if (name is null)
                    {
                        break;
                    }

                    if (TryParseFiltering(name, out _))
                    {
                        options.Filtering = name;
                    }
                    else
                    {
                        options.Errors.Add($"unknown texture filter '{name}' — expected nearest, bilinear or trilinear");
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
                    // Degrees here rather than radians: this one is swept across a sequence and the
                    // number people mean by it is 360.
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

        Validate(options);

        return options;
    }

    private static void Validate(RenderOptions options)
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

        // Supersampling multiplies both dimensions, so an unclamped factor turns a modest request
        // into an allocation nothing can serve. The engine clamps it too; saying so here is what
        // stops the file being silently smaller than the flag asked for.
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

        // The engine clamps these too. Saying so here is what keeps a typo from quietly baking
        // something other than what was asked for — a bake is minutes, not a frame you re-render.
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

        // Zero means "derive it from the panorama". Anything above 512 costs the split-sum
        // prefilter six faces of that size at 128 samples a texel, which is minutes, not seconds.
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

    /// <summary>The filtering mode <see cref="Filtering"/> names, bilinear if it names nothing.</summary>
    public TextureFiltering ResolveFiltering() =>
        TryParseFiltering(Filtering, out var filtering) ? filtering : TextureFiltering.Bilinear;

    private static bool TryParseFiltering(string name, out TextureFiltering filtering)
    {
        switch (name.ToLowerInvariant())
        {
            case "nearest" or "point" or "none":
                filtering = TextureFiltering.Nearest;
                return true;

            case "bilinear" or "linear":
                filtering = TextureFiltering.Bilinear;
                return true;

            case "trilinear":
                filtering = TextureFiltering.Trilinear;
                return true;

            default:
                filtering = TextureFiltering.Bilinear;
                return false;
        }
    }

    /// <summary>The output path, derived from the input when none was given.</summary>
    public string ResolveOutput()
    {
        if (Output is { Length: > 0 } output)
        {
            return output;
        }

        var name = Path.GetFileNameWithoutExtension(Input ?? "frame");

        // ".scene.json" leaves ".scene" behind, which would make every scene render into a file
        // whose name still claims to be one.
        if (name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".scene".Length];
        }

        return $"{name}.png";
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

    /// <summary>An angle typed in degrees, which is the only unit anybody types one in.</summary>
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

    public static void PrintUsage()
    {
        Console.WriteLine("""
            SoftEngine headless renderer — renders a model or a saved scene to a PNG.

              softengine <input> [options]

            The input is a model (.obj, .dae, .gltf, .glb) or a scene document (.json) written
            by the viewer's "Save scene as…".

            Output
              -o, --output <path>   PNG to write (default: the input's name with .png)
              -w, --width <px>      render width  (default 1920)
              -h, --height <px>     render height (default 1080)
                  --ss <n>          supersample n× and average down, 1-4 (default 1)
                  --oit             resolve transparency per pixel instead of by sorting the
                                    transparent triangles — correct where they intersect each
                                    other, and where a small one straddles a large one
                  --stats           print triangle, pixel and timing counts

            Where it renders
                  --backend <name>  auto, cpu, gpu or trace (default auto)
                  --gpu             shorthand for --backend gpu
                  --cpu             shorthand for --backend cpu
                  --gpu-info        print the graphics adapter, if any, and exit

              auto uses a graphics adapter when one is there and the software rasterizer when
              it is not. gpu says so explicitly and falls back with a reason — an OpenGL served
              by a CPU implementation (llvmpipe, GDI Generic, SwiftShader) is reported as no
              adapter, since rendering through one is slower than rendering without it.

            Reference rendering
                  --trace           path-trace the frame instead of rasterizing it: real
                                    interreflection, real ambient occlusion, ray-traced
                                    shadows with no bias to tune — and minutes, not
                                    milliseconds
                  --samples <n>     paths per pixel (default 16); implies --trace
                  --bounces <n>     bounces of indirect light (default 3, 0 for direct
                                    lighting only); implies --trace
                  --physical        put direct and bounced light on the same scale, instead
                                    of matching the rasterizer's exposure for direct light

            Baked indirect light
                  --bake            measure the scene's bounce light into a grid of probes
                                    before rasterizing, instead of standing in for it with
                                    the environment's six directional averages
                  --bake-resolution <n>
                                    probes along the world's longest axis (default 12)
                  --bake-rays <n>   paths traced out of each probe (default 128)
                  --bake-bounces <n>
                                    bounces each of those paths may take (default 2)

            Shading
              -p, --painter <name>  none, classic, flat, gouraud, phong, textured, material, pbr
                                    (default gouraud)
                  --filter <mode>   texture filtering: nearest, bilinear (default), or
                                    trilinear, which blends the two mip levels a surface
                                    falls between instead of stepping between them
                  --post <list>     comma-separated: ssr, ssao, bloom, tonemap, fxaa,
                                    vignette. ssr reflects the scene in the surfaces that
                                    reflect it, and needs the cpu backend to record what
                                    each one is made of
                  --shadows         render a shadow map from the scene's first light
                  --cascades <n>    shadows fitted to n slices of the view distance, 1-4
                  --no-sky          leave the background cleared instead of drawing a sky
                  --env <path>      light the scene with a panorama: .hdr keeps its full
                                    range, .png is projected as the 8-bit image it is
                  --environment-size <n>
                                    cube face resolution for --env (default: derived)
                  --hdr-sky         build the procedural sky in linear light, with a sun
                                    hundreds of times brighter than white instead of a
                                    white disc
                  --view <name>     present a buffer instead of the shaded image:
                                    depth, normals, overdraw, shadow, occlusion, mip

            Camera
                  --camera x,y,z    an explicit camera position
                  --yaw <deg>       bearing around the model  (default 0)
                  --pitch <deg>     elevation above it        (default 15)
                  --zoom <factor>   multiplies the framed distance; below 1 moves closer
              -t, --time <seconds>  how far into the model's animation to render

            Sequences
                  --frames <n>      render n frames into a numbered sequence
                                    (frame.0000.png, frame.0001.png, …)
                  --fps <rate>      frames per second the sequence represents (default 30),
                                    which is how far the animation advances between frames
                  --turntable <deg> degrees of yaw swept across the whole sequence; 360 is a
                                    full turn
                  --shutter <f>     motion-blur each frame by this fraction of its own motion
                                    (0.5 is a film shutter); needs --frames to have anything
                                    to measure

              A sequence is one PNG per frame. Turning it into a video is ffmpeg's job:
                ffmpeg -framerate 30 -i frame.%04d.png -pix_fmt yuv420p out.mp4

            Overlays
                  --wireframe       draw triangle edges over the shading
                  --grid            draw the ground grid
                  --axes            draw the world axes
                  --no-cull         draw back faces too

            A scene document may also be applied over a model with --scene <path>, which is how
            you render the same saved setup against a re-exported version of its model.

            Textures decode from PNG only: this front-end supplies the engine's own codec rather
            than a platform image library, so a model with JPEG maps renders untextured and says
            how many it skipped.
            """);
    }
}
