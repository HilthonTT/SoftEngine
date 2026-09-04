using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Textures;
using SoftEngine.Gpu;
using System.Numerics;

namespace SoftEngine.Cli.Options;

internal sealed class RenderOptions
{
    public string? Input { get; set; }

    public string? Output { get; set; }

    public string? ScenePath { get; set; }

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public string Painter { get; set; } = "gouraud";

    public string Filtering { get; set; } = "bilinear";

    public RasterizerMode Fill { get; set; } = RasterizerMode.Scanline;

    public int SuperSampling { get; set; } = 1;

    public bool BackFaceCulling { get; set; } = true;

    public bool OrderIndependentTransparency { get; set; }

    public bool Wireframe { get; set; }

    public bool Grid { get; set; }

    public bool Axes { get; set; }

    public bool Sky { get; set; } = true;

    public string? EnvironmentPath { get; set; }

    public int EnvironmentSize { get; set; }

    public bool HighDynamicRangeSky { get; set; }

    public bool Shadows { get; set; }

    public int Cascades { get; set; } = 1;

    public string? DebugView { get; set; }

    public HashSet<string> Post { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Vector3? Camera { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; } = 15f * MathF.PI / 180f;

    public float Zoom { get; set; } = 1f;

    public float Time { get; set; }

    public int Frames { get; set; } = 1;

    public float Fps { get; set; } = 30f;

    public float Turntable { get; set; }

    public float Shutter { get; set; }

    public bool Stats { get; set; }

    public RenderBackend Backend { get; set; } = RenderBackend.Automatic;

    public GpuPreference? GpuPreference { get; set; }

    public int Samples { get; set; } = 16;

    public int Bounces { get; set; } = 3;

    public bool PhysicalExposure { get; set; }

    public bool Bake { get; set; }

    public int BakeResolution { get; set; } = 12;

    public int BakeRays { get; set; } = 128;

    public int BakeBounces { get; set; } = 2;

    public bool ShowGpuInfo { get; set; }

    public bool ShowHelp { get; set; }

    public List<string> Errors { get; } = [];

    public TextureFiltering ResolveFiltering() =>
        TextureFilterNames.TryParse(Filtering, out var filtering) ? filtering : TextureFiltering.Bilinear;

    public string ResolveOutput()
    {
        if (Output is { Length: > 0 } output)
        {
            return output;
        }

        var name = Path.GetFileNameWithoutExtension(Input ?? "frame");

        if (name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".scene".Length];
        }

        return $"{name}.png";
    }
}
