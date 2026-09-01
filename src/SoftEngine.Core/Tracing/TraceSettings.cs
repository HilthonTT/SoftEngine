namespace SoftEngine.Core.Tracing;

public sealed class TraceSettings
{
    public int SamplesPerPixel { get; set; } = 16;

    public int MaxBounces { get; set; } = 3;

    public int RouletteDepth { get; set; } = 2;

    public bool LightFromEnvironment { get; set; } = true;

    public float DirectLightScale { get; set; } = MathF.PI;

    public bool Accumulate { get; set; }

    public uint Seed { get; set; } = 0x9E3779B9;

    public float RayOffset { get; set; } = 1e-4f;
}
