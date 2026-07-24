using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Scenes;

public enum FogMode
{
    /// <summary>Fog ramps linearly from none at <see cref="FogSettings.Start"/> to full at <see cref="FogSettings.End"/>.</summary>
    Linear,

    /// <summary>Fog thickens exponentially with distance: visibility = e^(-<see cref="FogSettings.Density"/> · distance).</summary>
    Exponential,
}

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
