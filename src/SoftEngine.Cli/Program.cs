using SoftEngine.Cli;
using SoftEngine.Core.Baking;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Scenes.Serialization;
using SoftEngine.Core.Tracing;
using SoftEngine.Gpu;
using System.Diagnostics;
using System.Numerics;

var options = RenderOptions.Parse(args);

if (options.ShowHelp || args.Length == 0)
{
    RenderOptions.PrintUsage();
    return options.ShowHelp ? 0 : 1;
}

if (options.ShowGpuInfo)
{
    return ReportGpu();
}

if (options.Errors.Count > 0)
{
    foreach (var error in options.Errors)
    {
        Console.Error.WriteLine($"softengine: {error}");
    }

    Console.Error.WriteLine("Try --help.");
    return 1;
}

try
{
    return Render(options);
}
catch (Exception exception) when (
    exception is IOException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
{
    // The failures a user can actually cause — a missing file, a format nothing here reads, a
    // directory that cannot be written. Anything else is a bug and deserves its stack trace.
    Console.Error.WriteLine($"softengine: {exception.Message}");
    return 1;
}

static int ReportGpu()
{
    if (GpuAvailability.Probe(out var adapter, out var error))
    {
        Console.WriteLine($"  adapter   {adapter!.Renderer}");
        Console.WriteLine($"  vendor    {adapter.Vendor}");
        Console.WriteLine($"  kind      {adapter.Kind}");
        Console.WriteLine($"  opengl    {adapter.Version}");
        Console.WriteLine($"  glsl      {adapter.ShadingLanguage}");

        return 0;
    }

    Console.WriteLine("  no graphics adapter is available for rendering.");
    Console.WriteLine($"  {error}");

    // Not an error: "there is no GPU here" is a true answer to the question that was asked.
    return 0;
}

static int Render(RenderOptions options)
{
    var input = options.Input!;

    // A scene document may be the input outright, or applied over a model named on the command
    // line. The first is "render this saved setup"; the second is "render this saved setup
    // against that model", which is what makes a document survive its model being re-exported.
    var document = IsSceneDocument(input) ? SceneSerializer.Load(input) : null;

    if (options.ScenePath is { } scenePath)
    {
        document = SceneSerializer.Load(scenePath);
    }

    var modelPath = document is not null && !IsSceneDocument(input)
        ? input
        : document?.World?.File ?? (document is null ? input : null);

    if (modelPath is null)
    {
        Console.Error.WriteLine(
            "softengine: the scene names no model file to render — it was saved from one of the " +
            "viewer's built-in worlds, which this program cannot build. Pass a model as well.");
        return 1;
    }

    if (!File.Exists(modelPath))
    {
        Console.Error.WriteLine($"softengine: the scene's model is not there: {modelPath}");
        return 1;
    }

    var loadStart = Stopwatch.GetTimestamp();
    var loaded = WorldLoader.Load(modelPath);
    var loadTime = Stopwatch.GetElapsedTime(loadStart);

    var factor = SuperSampler.ClampFactor(options.SuperSampling);

    // Only the software rasterizer reads an irradiance volume — the GPU backend holds its ambient
    // light in six uniforms, which is a cube and not a grid. So "pick a backend for me" must not
    // pick the one that would quietly ignore what was asked for; an explicit --gpu still gets what
    // it asked for, and is told what it costs.
    var requested = options.Bake && options.Backend == RenderBackend.Automatic
        ? RenderBackend.Cpu
        : options.Backend;

    var backend = RenderBackends.Create(requested);
    var renderer = backend.Renderer;

    // Whether the bake is worth doing at all: the two other backends ignore a volume, one because
    // it cannot hold one and one because it is busy computing the thing a volume approximates.
    // Baking anyway would spend minutes on something nothing will read.
    var bakes = options.Bake && renderer is Renderer;

    if (options.Bake && !bakes)
    {
        Console.Error.WriteLine(renderer is PathTracer
            ? "softengine: the path tracer computes indirect light as it goes — nothing to bake."
            : "softengine: this backend holds its ambient light as six values and cannot read a " +
              "volume; the frame will be lit by the environment instead.");
    }

    // Said before the render rather than after it, so a fallback explains the frame time that
    // is about to follow instead of arriving too late to.
    if (backend.Fallback is { } fallback)
    {
        Console.Error.WriteLine($"softengine: {fallback}");
        Console.Error.WriteLine("softengine: rendering on the CPU instead.");
    }

    // The event log allocates nothing but does real work per mesh, and none of it can reach a
    // pixel. A batch render should be a recording of the renderer, not of its debugger.
    renderer.Diagnostics.CaptureEvents = false;

    if (renderer is PathTracer tracer)
    {
        tracer.Trace.SamplesPerPixel = options.Samples;
        tracer.Trace.MaxBounces = options.Bounces;
        tracer.Trace.DirectLightScale = options.PhysicalExposure ? 1f : MathF.PI;
    }

    renderer.Settings.BackFaceCulling = options.BackFaceCulling;
    renderer.Settings.OrderIndependentTransparency = options.OrderIndependentTransparency;
    renderer.Settings.ShowTriangles = options.Wireframe;
    renderer.Settings.ShowXZGrid = options.Grid;
    renderer.Settings.ShowAxes = options.Axes;
    renderer.Settings.SkeletonTickSize = loaded.Radius * 0.05f;

    if (options.DebugView is { } view)
    {
        // The names people type are not always the enum's. "occlusion" and "mip" are what the
        // usage text has always offered, and a flag that documents one spelling and accepts
        // another is a worse failure than an unknown view — it looks like the view is broken.
        var named = view.Trim().ToLowerInvariant() switch
        {
            "occlusion" => nameof(DebugView.OcclusionBuffer),
            "mip" or "mips" or "mipmap" => nameof(DebugView.MipLevel),
            "shadow" => nameof(DebugView.ShadowMap),
            _ => view,
        };

        if (!Enum.TryParse<DebugView>(named, ignoreCase: true, out var parsed))
        {
            Console.Error.WriteLine($"softengine: unknown buffer view '{view}'");
            return 1;
        }

        renderer.Settings.DebugView = parsed;
    }

    const float fieldOfView = 40f * MathF.PI / 180f;

    var camera = new OrbitCamera { Target = loaded.Center };

    // The distance at which a sphere of that radius exactly fills the frame's vertical extent is
    // r / sin(fov/2) — solved rather than guessed at with a multiplier, because the multiplier
    // that frames one model crops the next. The margin is the air around it.
    const float margin = 1.08f;

    var distance = MathF.Max(loaded.Radius / MathF.Sin(fieldOfView * 0.5f) * margin * options.Zoom, 1e-3f);

    camera.Orbit(options.Yaw, options.Pitch, distance);

    if (options.Camera is { } position)
    {
        camera.Position = position;
    }

    var scene = new Scene
    {
        Surface = new FrameBuffer(options.Width * factor, options.Height * factor) { Stats = renderer.Stats },
        Camera = camera,
        World = loaded.World,

        // The far plane contains the model from wherever the camera ended up, with headroom: a
        // far plane closer than the geometry slices the model visibly, and nothing about a
        // one-shot render gives the user a chance to notice and fix it.
        Projection = new PerspectiveProjection(
            fieldOfView,
            MathF.Max(loaded.Radius * 0.001f, 1e-4f),
            MathF.Max((camera.Position - loaded.Center).Length() + loaded.Radius * 4f, 100f)),

        GammaCorrect = true,
        HighDynamicRange = true,
    };

    scene.Shadows.Enabled = options.Shadows;
    scene.Shadows.CascadeCount = options.Cascades;
    scene.Shadows.Resolution = options.Width > 1280 ? 2048 : 1024;

    if (options.EnvironmentPath is { } environmentPath)
    {
        try
        {
            scene.Environment = EnvironmentLoader.Load(environmentPath, options.EnvironmentSize);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            // A panorama that will not decode is worth saying out loud and worth continuing past:
            // the frame is still renderable, it is just lit by nothing but its lights.
            Console.Error.WriteLine($"softengine: could not read '{environmentPath}': {error.Message}");
        }
    }
    else if (options.Sky)
    {
        // The sun goes where the world's key light points. A sky whose sun is somewhere other
        // than where the shadows come from is the one thing that reads as obviously wrong.
        var sun = loaded.World.Lights.OfType<DirectionalLight>().FirstOrDefault()?.Direction
            ?? new Vector3(-0.35f, -0.6f, -1f);

        scene.Environment = options.HighDynamicRangeSky
            ? SkyBox.HighDynamicRangeGradient(sun)
            : SkyBox.Gradient(sun);
    }

    var post = PostProcessStack.CreateDefault();

    foreach (var effect in post.Effects)
    {
        effect.Enabled = false;
    }

    if (post.Find<SsaoEffect>() is { } ssao)
    {
        // A world-space distance, and the one post-process number that has to be scaled to the
        // scene: a radius that finds the creases in a 2-unit skull sees nothing on a 1500-unit
        // elephant.
        ssao.Radius = loaded.Radius * 0.06f;
        ssao.Bias = ssao.Radius * 0.04f;
    }

    foreach (var name in options.Post)
    {
        var effect = name.ToLowerInvariant() switch
        {
            "ssao" => post.Find<SsaoEffect>() as IPostEffect,
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

    renderer.PostProcess = post;

    var painter = BuildPainter(options.Painter, options.ResolveFiltering());

    // Last, so it wins over everything derived above — which is the point of naming a document:
    // it carries the settings somebody actually chose.
    if (document is not null)
    {
        SceneSerializer.Apply(document, scene, renderer.Settings, post);

        if (document.Rendering is { Painter: { Length: > 0 } named })
        {
            painter = BuildPainter(named, options.ResolveFiltering());
        }
    }

    if (options.Shutter > 0f)
    {
        // Motion blur needs two frames to have anything to measure, which a sequence has and a
        // single render does not — so it is only offered alongside one, and the flag says so.
        renderer.Settings.MotionBlur = true;

        if (renderer is Renderer cpu)
        {
            cpu.MotionBlur.ShutterFraction = options.Shutter;
        }
    }

    var frames = System.Math.Max(1, options.Frames);
    var interval = options.Fps > 0f ? 1f / options.Fps : 0f;
    var output = options.ResolveOutput();

    var renderTime = TimeSpan.Zero;
    var bakeTime = TimeSpan.Zero;

    if (bakes)
    {
        // Posed first: a bake measures light bouncing off the geometry where it stands, and a rig
        // that has never been updated stands wherever its nodes were constructed. A sequence bakes
        // once, at its first frame — an irradiance volume is a statement about an arrangement of a
        // world, and rebaking it per frame would cost more than the frames do.
        loaded.World.Update(MathF.Max(options.Time, 0f));

        var bakeStart = Stopwatch.GetTimestamp();

        scene.Irradiance = IrradianceBaker.Bake(scene, new BakeSettings
        {
            Resolution = options.BakeResolution,
            Rays = options.BakeRays,
            Bounces = options.BakeBounces,
        });

        bakeTime = Stopwatch.GetElapsedTime(bakeStart);

        if (options.Stats)
        {
            var volume = scene.Irradiance;

            Console.WriteLine(
                $"baked {volume.CountX}×{volume.CountY}×{volume.CountZ} probes " +
                $"({volume.ValidCount} outside geometry) in {bakeTime.TotalMilliseconds:F0} ms");
        }
    }

    for (var frame = 0; frame < frames; frame++)
    {
        // Where this frame sits in the sequence, in [0, 1). Open at the top on purpose: a turntable
        // whose last frame repeats its first stutters when it loops.
        var progress = frames > 1 ? frame / (float)frames : 0f;

        // Animations are advanced before the frame rather than during it: rendering must not move
        // time. This runs even at t = 0, because the hierarchy still has to be posed once — a rig
        // that has never been updated renders at whatever its nodes happened to be constructed
        // with. On a static model it walks two empty lists.
        loaded.World.Update(MathF.Max(options.Time, 0f) + frame * interval);

        if (options.Turntable != 0f)
        {
            // The camera walks the arc rather than the model turning: a scene has lights and a sky
            // in it, and spinning the geometry inside them looks like the lighting is spinning too.
            camera.Orbit(options.Yaw + options.Turntable * progress, options.Pitch, distance);
        }

        var renderStart = Stopwatch.GetTimestamp();
        renderer.Render(scene, painter);
        renderTime += Stopwatch.GetElapsedTime(renderStart);

        int[] pixels;

        if (factor == 1)
        {
            pixels = scene.Surface.Screen;
        }
        else
        {
            pixels = new int[options.Width * options.Height];
            SuperSampler.Resolve(scene.Surface, pixels, options.Width, options.Height, factor);
        }

        // Cleared background pixels are 0x00000000, which would save as transparent — honest for a
        // compositing workflow and surprising for everyone else, who asked for a picture.
        var opaque = new int[options.Width * options.Height];

        for (var i = 0; i < opaque.Length; i++)
        {
            opaque[i] = pixels[i] | unchecked((int)0xFF000000);
        }

        var path = frames > 1 ? Numbered(output, frame) : output;

        PngCodec.Save(path, opaque, options.Width, options.Height);

        if (frames > 1)
        {
            // One line per frame, overwritten: a hundred-frame render should not scroll the reason
            // it was slow off the top of the terminal.
            Console.Write($"\r  frame {frame + 1}/{frames} → {Path.GetFileName(path)}   ");
        }
    }

    // The GPU renderer owns a context, a window and a pile of buffers; the CPU one owns
    // nothing and does not implement IDisposable.
    (renderer as IDisposable)?.Dispose();

    if (frames > 1)
    {
        Console.WriteLine();
        Console.WriteLine($"{Numbered(output, 0)} … {Numbered(output, frames - 1)}  {options.Width}×{options.Height}" +
            (factor > 1 ? $" (rendered {factor}×)" : string.Empty));
        Console.WriteLine($"  {frames} frames at {options.Fps:0.##} fps — {frames / MathF.Max(options.Fps, 1e-3f):0.##} s of animation");
    }
    else
    {
        Console.WriteLine($"{output}  {options.Width}×{options.Height}" +
            (factor > 1 ? $" (rendered {factor}×)" : string.Empty));
    }

    if (loaded.SkippedTextures > 0)
    {
        Console.WriteLine(
            $"  {loaded.SkippedTextures} texture(s) could not be decoded — this renderer reads PNG only, " +
            "so those surfaces are untextured.");
    }

    if (options.Stats)
    {
        var stats = renderer.Stats;

        Console.WriteLine($"  drawn by {backend.Describe()}");
        Console.WriteLine($"  load    {loadTime.TotalMilliseconds:0.#} ms");
        Console.WriteLine($"  render  {renderTime.TotalMilliseconds:0.#} ms");
        Console.WriteLine($"  meshes  {loaded.World.Meshes.Count} ({stats.OccludedMeshCount} hidden behind {stats.OccluderMeshCount} occluder(s))");
        Console.WriteLine($"  tris    {stats.TotalTriangleCount} total, {stats.DrawnTriangleCount} drawn");
        Console.WriteLine($"  pixels  {stats.DrawnPixelCount} drawn");

        // Only when the frame actually stored fragments. The overflow count goes with them
        // because it is the one number that says the resolve was approximate.
        if (stats.TransparentFragmentCount > 0)
        {
            Console.WriteLine(
                $"  glass   {stats.TransparentFragmentCount} fragments over {stats.TransparentPixelCount} pixels" +
                (stats.TransparentOverflowCount > 0
                    ? $", {stats.TransparentOverflowCount} merged past the per-pixel limit"
                    : string.Empty));
        }
    }

    return 0;
}

static bool IsSceneDocument(string path) =>
    Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);

/// <summary>
/// One frame's path: the output name with a four-digit index before its extension.
///
/// Zero-padded and fixed-width because every tool that reads a sequence — ffmpeg, an image viewer's
/// "open as animation", a shell glob — sorts the names as text, and <c>frame.10.png</c> sorts before
/// <c>frame.2.png</c>.
/// </summary>
static string Numbered(string output, int frame)
{
    var directory = Path.GetDirectoryName(output);
    var name = Path.GetFileNameWithoutExtension(output);
    var extension = Path.GetExtension(output);

    var numbered = $"{name}.{frame:D4}{extension}";

    return string.IsNullOrEmpty(directory) ? numbered : Path.Combine(directory, numbered);
}

static IPainter? BuildPainter(string name, TextureFiltering filtering)
{
    // Filtering is on for every painter that samples a texture: a still image has no shimmer to
    // trade away, so there is nothing to gain by turning it off and detail to lose. Mip maps go
    // with it — a nearest fill was asked for the unfiltered image, chain and all.
    var mipMaps = filtering != TextureFiltering.Nearest;

    switch (name.ToLowerInvariant())
    {
        case "none":
            return null;

        case "classic":
            return new ClassicPainter();

        case "flat":
            return new FlatPainter();

        case "phong":
            return new PhongPainter();

        case "textured":
            return new TexturedPainter { Filtering = filtering, UseMipMaps = mipMaps };

        case "material":
            return new MaterialPainter { Filtering = filtering, UseMipMaps = mipMaps };

        case "pbr" or "physicallybased":
            return new PbrPainter { Filtering = filtering, UseMipMaps = mipMaps };

        default:
            return new GouraudPainter();
    }
}
