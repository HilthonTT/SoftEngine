namespace SoftEngine.Core.Scenes;

public enum FogMode
{
    /// <summary>Fog ramps linearly from none at <see cref="FogSettings.Start"/> to full at <see cref="FogSettings.End"/>.</summary>
    Linear,

    /// <summary>Fog thickens exponentially with distance: visibility = e^(-<see cref="FogSettings.Density"/> · distance).</summary>
    Exponential,
}
