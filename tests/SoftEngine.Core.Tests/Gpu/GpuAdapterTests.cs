using SoftEngine.Gpu;

namespace SoftEngine.Core.Tests.Gpu;

public class GpuAdapterTests
{
    private static GpuAdapter Adapter(string vendor, string renderer) =>
        new(vendor, renderer, "4.6.0", "4.60");

    [Theory]

    [InlineData("Mesa/X.org", "llvmpipe (LLVM 15.0.7, 256 bits)")]
    [InlineData("VMware, Inc.", "softpipe")]
    [InlineData("Mesa Project", "Software Rasterizer")]

    [InlineData("Microsoft Corporation", "GDI Generic")]
    [InlineData("Microsoft Corporation", "D3D12 (Microsoft Basic Render Driver)")]

    [InlineData("Google Inc.", "SwiftShader Device (Subzero)")]
    public void Classify_SoftwareImplementation_IsNotHardware(string vendor, string renderer)
    {
        var adapter = Adapter(vendor, renderer);

        Assert.Equal(GpuAdapterKind.Software, adapter.Kind);
        Assert.False(adapter.IsHardwareAccelerated);
    }

    [Theory]
    [InlineData("NVIDIA Corporation", "NVIDIA GeForce RTX 4070/PCIe/SSE2")]
    [InlineData("NVIDIA Corporation", "Quadro P2000/PCIe/SSE2")]
    [InlineData("ATI Technologies Inc.", "AMD Radeon RX 7900 XTX")]
    [InlineData("ATI Technologies Inc.", "Radeon Pro W6800")]
    public void Classify_GraphicsCard_IsDiscrete(string vendor, string renderer)
    {
        var adapter = Adapter(vendor, renderer);

        Assert.Equal(GpuAdapterKind.Discrete, adapter.Kind);
        Assert.True(adapter.IsHardwareAccelerated);
    }

    [Theory]
    [InlineData("Intel", "Intel(R) UHD Graphics 620")]
    [InlineData("Intel", "Intel(R) Iris(R) Xe Graphics")]

    [InlineData("Intel", "Intel(R) Graphics")]
    [InlineData("Apple", "Apple M2 Pro")]
    [InlineData("Qualcomm", "Adreno (TM) 740")]
    public void Classify_OnPackageGraphics_IsIntegrated(string vendor, string renderer)
    {
        var adapter = Adapter(vendor, renderer);

        Assert.Equal(GpuAdapterKind.Integrated, adapter.Kind);
        Assert.True(adapter.IsHardwareAccelerated);
    }

    [Fact]
    public void Classify_UnknownDevice_IsTreatedAsHardware()
    {
        var adapter = Adapter("Some Vendor", "Model 9000 Graphics Processor");

        Assert.Equal(GpuAdapterKind.Unknown, adapter.Kind);
        Assert.True(adapter.IsHardwareAccelerated);
    }

    [Fact]
    public void Classify_DiscreteNameInsideIntegratedVendor_PrefersDiscrete()
    {
        Assert.Equal(GpuAdapterKind.Discrete, Adapter("Intel", "Intel(R) Arc(TM) A770 Graphics").Kind);
    }

    [Fact]
    public void Describe_NamesTheDeviceAndWhatKindItIs()
    {
        Assert.Contains("discrete GPU", Adapter("NVIDIA Corporation", "NVIDIA GeForce RTX 4070").Describe());
        Assert.Contains("integrated GPU", Adapter("Intel", "Intel(R) UHD Graphics 620").Describe());
        Assert.Contains("not a GPU", Adapter("Mesa/X.org", "llvmpipe").Describe());
    }

    [Theory]
    [InlineData("auto", RenderBackend.Automatic)]
    [InlineData("", RenderBackend.Automatic)]
    [InlineData("cpu", RenderBackend.Cpu)]
    [InlineData("software", RenderBackend.Cpu)]
    [InlineData("GPU", RenderBackend.Gpu)]
    [InlineData("OpenGL", RenderBackend.Gpu)]
    public void TryParse_KnownName_Resolves(string name, RenderBackend expected)
    {
        Assert.True(RenderBackends.TryParse(name, out var backend));
        Assert.Equal(expected, backend);
    }

    [Fact]
    public void TryParse_UnknownName_Fails()
    {
        Assert.False(RenderBackends.TryParse("vulkan", out _));
    }

    [Fact]
    public void Create_Cpu_UsesTheSoftwareRendererWithoutProbing()
    {
        var result = RenderBackends.Create(RenderBackend.Cpu);

        Assert.Equal(RenderBackend.Cpu, result.Backend);
        Assert.IsType<Core.Pipeline.Renderer>(result.Renderer);
        Assert.Null(result.Adapter);
        Assert.Null(result.Fallback);
        Assert.Contains("software", result.Describe(), StringComparison.OrdinalIgnoreCase);
    }
}
