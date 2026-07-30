using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Scenes;

/// <summary>
/// Distance fog for a scene: pixels blend toward <see cref="Color"/> with view-space
/// depth. Painters pick this up once per frame in Prepare; the blend itself happens
/// per pixel in the rasterizer, after shading.
/// </summary>
public sealed class FogSettings
{
    public bool Enabled { get; set; }

    public FogMode Mode { get; set; } = FogMode.Linear;

    /// <summary>The colour fully fogged pixels converge to — usually the background.</summary>
    public ColorRGB Color { get; set; }

    /// <summary>View-space distance where linear fog begins.</summary>
    public float Start { get; set; } = 10f;

    /// <summary>View-space distance where linear fog is total.</summary>
    public float End { get; set; } = 100f;

    /// <summary>Thickness of exponential fog per unit of distance.</summary>
    public float Density { get; set; } = 0.02f;
}
