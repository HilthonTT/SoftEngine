namespace SoftEngine.Gpu;

public sealed class GpuAdapter
{
    private static readonly string[] SoftwareMarkers =
    [
        "llvmpipe",
        "softpipe",
        "swrast",
        "gdi generic",
        "microsoft basic",
        "microsoft corporation gdi",
        "swiftshader",
        "lavapipe",
        "d3d12 (microsoft basic render driver)",
        "software rasterizer",
        "mesa offscreen",
    ];

    private static readonly string[] IntegratedMarkers =
    [
        "intel(r) hd graphics",
        "intel(r) uhd graphics",
        "intel(r) iris",
        "intel(r) arc",
        "mesa intel",
        "amd radeon(tm) graphics",
        "radeon vega",
        "apple m",
        "adreno",
        "mali",
        "videocore",
    ];

    private static readonly string[] DiscreteOverrides =
    [

        "arc(tm) a",
        "arc(tm) b",
        "arc a",
        "arc b",
        "geforce",
        "quadro",
        "radeon rx",
        "radeon pro",
        "firepro",
        "nvidia",
        "tesla",
    ];

    private static readonly (string Vendor, GpuAdapterKind Kind)[] VendorFallback =
    [
        ("intel", GpuAdapterKind.Integrated),
        ("nvidia", GpuAdapterKind.Discrete),
        ("apple", GpuAdapterKind.Integrated),
        ("qualcomm", GpuAdapterKind.Integrated),
        ("arm", GpuAdapterKind.Integrated),
        ("imagination", GpuAdapterKind.Integrated),
        ("broadcom", GpuAdapterKind.Integrated),
    ];

    public GpuAdapter(string vendor, string renderer, string version, string shadingLanguage)
    {
        Vendor = vendor ?? string.Empty;
        Renderer = renderer ?? string.Empty;
        Version = version ?? string.Empty;
        ShadingLanguage = shadingLanguage ?? string.Empty;
        Kind = Classify(Vendor, Renderer);
    }

    public string Vendor { get; }

    public string Renderer { get; }

    public string Version { get; }

    public string ShadingLanguage { get; }

    public GpuAdapterKind Kind { get; }

    public bool IsHardwareAccelerated => Kind != GpuAdapterKind.Software;

    public string Describe() => Kind switch
    {
        GpuAdapterKind.Discrete => $"{Renderer} (discrete GPU)",
        GpuAdapterKind.Integrated => $"{Renderer} (integrated GPU)",
        GpuAdapterKind.Software => $"{Renderer} (software OpenGL — not a GPU)",
        _ => $"{Renderer} ({Vendor})",
    };

    public override string ToString() => $"{Describe()}, {Version}";

    public static GpuAdapterKind KindOf(string? vendor, string? renderer) =>
        Classify(vendor ?? string.Empty, renderer ?? string.Empty);

    private static GpuAdapterKind Classify(string vendor, string renderer)
    {
        var haystack = $"{vendor} {renderer}".ToLowerInvariant();

        foreach (var marker in SoftwareMarkers)
        {
            if (haystack.Contains(marker, StringComparison.Ordinal))
            {
                return GpuAdapterKind.Software;
            }
        }

        foreach (var marker in DiscreteOverrides)
        {
            if (haystack.Contains(marker, StringComparison.Ordinal))
            {
                return GpuAdapterKind.Discrete;
            }
        }

        foreach (var marker in IntegratedMarkers)
        {
            if (haystack.Contains(marker, StringComparison.Ordinal))
            {
                return GpuAdapterKind.Integrated;
            }
        }

        var lowerVendor = vendor.ToLowerInvariant();

        foreach (var (name, kind) in VendorFallback)
        {
            if (lowerVendor.Contains(name, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return GpuAdapterKind.Unknown;
    }
}
