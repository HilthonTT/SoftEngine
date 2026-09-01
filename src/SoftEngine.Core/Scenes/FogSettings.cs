using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Scenes;

public sealed class FogSettings
{
    public bool Enabled { get; set; }

    public FogMode Mode { get; set; } = FogMode.Linear;

    public ColorRGB Color { get; set; }

    public float Start { get; set; } = 10f;

    public float End { get; set; } = 100f;

    public float Density { get; set; } = 0.02f;
}
