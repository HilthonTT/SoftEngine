namespace SoftEngine.Gpu;

/// <summary>
/// The device an OpenGL context turned out to be running on, as the driver describes itself,
/// plus the one judgement this engine actually needs from those strings: whether there is a
/// graphics processor behind them at all.
///
/// <para>
/// The classification is by name because OpenGL offers nothing better. There is no query for
/// "are you hardware" — a software implementation reports a vendor and a renderer exactly as
/// a graphics card does, and it is the renderer string that gives it away. The lists below
/// are therefore the specific implementations that exist rather than a general rule, and
/// anything unrecognized is treated as hardware: a driver this doesn't know the name of is
/// far likelier to be a new graphics card than a new CPU rasterizer.
/// </para>
/// </summary>
public sealed class GpuAdapter
{
    /// <summary>
    /// Renderer strings that mean a CPU is doing the rasterizing. Matched as substrings,
    /// case-insensitively, against <see cref="Renderer"/> and <see cref="Vendor"/>.
    /// </summary>
    private static readonly string[] SoftwareMarkers =
    [
        "llvmpipe",          // Mesa's LLVM-JIT rasterizer, the usual Linux fallback
        "softpipe",          // Mesa's reference rasterizer
        "swrast",            // Mesa's older software path
        "gdi generic",       // Windows' OpenGL 1.1 fallback when no ICD is installed
        "microsoft basic",   // Microsoft Basic Render Driver / Basic Display Adapter
        "microsoft corporation gdi",
        "swiftshader",       // Google's CPU implementation, what a headless Chrome gets
        "lavapipe",          // llvmpipe's Vulkan sibling, reachable through Zink
        "d3d12 (microsoft basic render driver)",
        "software rasterizer",
        "mesa offscreen",
    ];

    /// <summary>
    /// Renderer strings that mean a graphics processor sharing the CPU's memory. Integrated
    /// is still hardware and still worth using — it is typically several times faster than
    /// this engine's software rasterizer — so this only ever changes what is reported, never
    /// whether the backend is offered.
    /// </summary>
    private static readonly string[] IntegratedMarkers =
    [
        "intel(r) hd graphics",
        "intel(r) uhd graphics",
        "intel(r) iris",
        "intel(r) arc",       // Arc is discrete, but the iGPU-era naming overlaps; see below
        "mesa intel",
        "amd radeon(tm) graphics",
        "radeon vega",
        "apple m",
        "adreno",
        "mali",
        "videocore",
    ];

    /// <summary>Names that are discrete despite matching a broader integrated marker.</summary>
    private static readonly string[] DiscreteOverrides =
    [
        // Intel Arc add-in cards. Both spellings, because the driver writes the trademark
        // into the middle of the name — "Intel(R) Arc(TM) A770 Graphics" — and the series
        // letter is what separates a card from the Arc-branded integrated parts.
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

    /// <summary>
    /// Last resort, on the vendor alone. Drivers name their parts inconsistently and keep
    /// renaming them — this machine's reports itself as the bare "Intel(R) Graphics" — so a
    /// vendor that only ever ships one kind of part settles it when the model name doesn't.
    /// AMD is absent on purpose: it ships both, and guessing would be worse than
    /// <see cref="GpuAdapterKind.Unknown"/>, which already means "hardware, kind unclear".
    /// </summary>
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

    /// <summary>GL_VENDOR — who wrote the driver ("NVIDIA Corporation", "Intel", "Mesa/X.org").</summary>
    public string Vendor { get; }

    /// <summary>GL_RENDERER — the device itself ("NVIDIA GeForce RTX 4070/PCIe/SSE2").</summary>
    public string Renderer { get; }

    /// <summary>GL_VERSION — the OpenGL version and driver build.</summary>
    public string Version { get; }

    /// <summary>GL_SHADING_LANGUAGE_VERSION.</summary>
    public string ShadingLanguage { get; }

    public GpuAdapterKind Kind { get; }

    /// <summary>
    /// Whether a graphics processor is doing the work. False only for the CPU
    /// implementations named in <see cref="SoftwareMarkers"/> — see the type summary for
    /// why an unrecognized device counts as hardware.
    /// </summary>
    public bool IsHardwareAccelerated => Kind != GpuAdapterKind.Software;

    /// <summary>One line naming the device and what kind it is, for a status bar or a log.</summary>
    public string Describe() => Kind switch
    {
        GpuAdapterKind.Discrete => $"{Renderer} (discrete GPU)",
        GpuAdapterKind.Integrated => $"{Renderer} (integrated GPU)",
        GpuAdapterKind.Software => $"{Renderer} (software OpenGL — not a GPU)",
        _ => $"{Renderer} ({Vendor})",
    };

    public override string ToString() => $"{Describe()}, {Version}";

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

        // Checked before the integrated list, because a discrete card can carry a vendor
        // name that also ships integrated parts.
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
