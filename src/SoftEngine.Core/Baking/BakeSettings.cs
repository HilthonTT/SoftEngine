namespace SoftEngine.Core.Baking;

public sealed class BakeSettings
{
    private int _resolution = 12;
    private int _rays = 128;

    public int Resolution
    {
        get => _resolution;
        set => _resolution = System.Math.Clamp(value, 2, 64);
    }

    public int Rays
    {
        get => _rays;
        set => _rays = System.Math.Max(1, value);
    }

    public int Bounces { get; set; } = 2;

    public float Padding { get; set; } = 0.05f;

    public float InsideThreshold { get; set; } = 0.6f;

    public float Intensity { get; set; } = 1f;

    public float MaxRadiance { get; set; }

    public bool LightFromEnvironment { get; set; } = true;

    public float DirectLightScale { get; set; } = MathF.PI;

    public uint Seed { get; set; } = 0x9E3779B9;
}
