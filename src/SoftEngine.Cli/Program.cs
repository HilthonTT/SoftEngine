using SoftEngine.Cli;
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
using System.Diagnostics;
using System.Numerics;

var options = RenderOptions.Parse(args);

if (options.ShowHelp || args.Length == 0)
{
    RenderOptions.PrintUsage();
    return options.ShowHelp ? 0 : 1;
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

    var renderer = new Renderer();

    // The event log allocates nothing but does real work per mesh, and none of it can reach a
    // pixel. A batch render should be a recording of the renderer, not of its debugger.
    renderer.Diagnostics.CaptureEvents = false;

    renderer.Settings.BackFaceCulling = options.BackFaceCulling;
    renderer.Settings.ShowTriangles = options.Wireframe;
    renderer.Settings.ShowXZGrid = options.Grid;
    renderer.Settings.ShowAxes = options.Axes;
    renderer.Settings.SkeletonTickSize = loaded.Radius * 0.05f;

    if (options.DebugView is { } view)
    {
        if (!Enum.TryParse<DebugView>(view, ignoreCase: true, out var parsed))
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

    if (options.Sky)
    {
        // The sun goes where the world's key light points. A sky whose sun is somewhere other
        // than where the shadows come from is the one thing that reads as obviously wrong.
        var sun = loaded.World.Lights.OfType<DirectionalLight>().FirstOrDefault()?.Direction
            ?? new Vector3(-0.35f, -0.6f, -1f);

        scene.Environment = SkyBox.Gradient(sun);
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

    var painter = BuildPainter(options.Painter);

    // Last, so it wins over everything derived above — which is the point of naming a document:
    // it carries the settings somebody actually chose.
    if (document is not null)
    {
        SceneSerializer.Apply(document, scene, renderer.Settings, post);

        if (document.Rendering is { Painter: { Length: > 0 } named })
        {
            painter = BuildPainter(named);
        }
    }

    // Animations are advanced before the frame rather than during it: rendering must not move
    // time, and a batch render has exactly one moment to show. This runs even at t = 0, because
    // the hierarchy still has to be posed once — a rig that has never been updated renders at
    // whatever its nodes happened to be constructed with. On a static model it walks two empty
    // lists.
    loaded.World.Update(MathF.Max(options.Time, 0f));

    var renderStart = Stopwatch.GetTimestamp();
    renderer.Render(scene, painter);
    var renderTime = Stopwatch.GetElapsedTime(renderStart);

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

    var output = options.ResolveOutput();

    PngCodec.Save(output, opaque, options.Width, options.Height);

    Console.WriteLine($"{output}  {options.Width}×{options.Height}" +
        (factor > 1 ? $" (rendered {factor}×)" : string.Empty));

    if (loaded.SkippedTextures > 0)
    {
        Console.WriteLine(
            $"  {loaded.SkippedTextures} texture(s) could not be decoded — this renderer reads PNG only, " +
            "so those surfaces are untextured.");
    }

    if (options.Stats)
    {
        var stats = renderer.Stats;

        Console.WriteLine($"  load    {loadTime.TotalMilliseconds:0.#} ms");
        Console.WriteLine($"  render  {renderTime.TotalMilliseconds:0.#} ms");
        Console.WriteLine($"  meshes  {loaded.World.Meshes.Count} ({stats.OccludedMeshCount} hidden behind {stats.OccluderMeshCount} occluder(s))");
        Console.WriteLine($"  tris    {stats.TotalTriangleCount} total, {stats.DrawnTriangleCount} drawn");
        Console.WriteLine($"  pixels  {stats.DrawnPixelCount} drawn");
    }

    return 0;
}

static bool IsSceneDocument(string path) =>
    Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);

static IPainter? BuildPainter(string name)
{
    // Filtering is on for every painter that samples a texture: a still image has no shimmer to
    // trade away, so there is nothing to gain by turning it off and detail to lose.
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
            return new TexturedPainter { Filtering = TextureFiltering.Bilinear, UseMipMaps = true };

        case "material":
            return new MaterialPainter { Filtering = TextureFiltering.Bilinear, UseMipMaps = true };

        case "pbr" or "physicallybased":
            return new PbrPainter { Filtering = TextureFiltering.Bilinear, UseMipMaps = true };

        default:
            return new GouraudPainter();
    }
}
