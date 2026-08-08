using SoftEngine.Core.Textures;
using SoftEngine.Gpu;
using System.Numerics;

namespace SoftEngine.Cli.Options;

/// <summary>
/// Everything the command line can say about one render.
///
/// <para>
/// This type is the answer and not the reading of it: <see cref="RenderOptionsParser"/> fills it,
/// <see cref="RenderOptionsValidation"/> decides whether what it holds is renderable, and
/// <see cref="UsageText"/> is what the flags are called in prose. Keeping the four apart is what
/// stops a new flag from being added to three of them and forgotten in the fourth.
/// </para>
/// </summary>
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

    /// <summary>The filtering mode <see cref="Filtering"/> names, bilinear if it names nothing.</summary>
    public TextureFiltering ResolveFiltering() =>
        TextureFilterNames.TryParse(Filtering, out var filtering) ? filtering : TextureFiltering.Bilinear;

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
}
