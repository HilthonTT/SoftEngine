using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Per-triangle state the rasterizer applies after the pixel shader: distance fog
/// blended by view-space depth, and alpha blending for transparent meshes.
/// <c>default(RasterState)</c> is fully opaque with no fog, so callers that don't
/// opt in behave exactly as before.
/// </summary>
public readonly struct RasterState
{
    private const byte FogNone = 0;
    private const byte FogLinear = 1;
    private const byte FogExponential = 2;

    // Stored as transparency (1 - alpha) so the default value means opaque.
    private readonly float _transparency;
    private readonly byte _fogMode;
    private readonly float _fogA; // linear: End / (End - Start); exponential: density
    private readonly float _fogB; // linear: -1 / (End - Start)
    private readonly ColorRGB _fogColor;

    private RasterState(float transparency, byte fogMode, float fogA, float fogB, ColorRGB fogColor)
    {
        _transparency = transparency;
        _fogMode = fogMode;
        _fogA = fogA;
        _fogB = fogB;
        _fogColor = fogColor;
    }

    /// <summary>Builds the fog part from a scene; opacity is applied per mesh via <see cref="WithOpacity"/>.</summary>
    public static RasterState From(Scene scene) => From(scene.Fog);

    public static RasterState From(FogSettings? fog)
    {
        if (fog is null || !fog.Enabled)
        {
            return default;
        }

        if (fog.Mode == FogMode.Linear)
        {
            var invRange = 1f / MathF.Max(fog.End - fog.Start, 1e-6f);
            return new RasterState(0f, FogLinear, fog.End * invRange, -invRange, fog.Color);
        }

        return new RasterState(0f, FogExponential, MathF.Max(fog.Density, 0f), 0f, fog.Color);
    }

    /// <summary>The same fog with a mesh's opacity; 1 keeps the state opaque.</summary>
    public RasterState WithOpacity(float opacity) =>
        new(1f - System.Math.Clamp(opacity, 0f, 1f), _fogMode, _fogA, _fogB, _fogColor);

    public bool IsOpaque => _transparency == 0f;

    public float Alpha => 1f - _transparency;

    public bool HasFog => _fogMode != FogNone;

    /// <summary>
    /// Blends a shaded colour toward the fog colour by the view-space depth
    /// <paramref name="w"/> (the clip-space w recovered by the rasterizer).
    /// </summary>
    public ColorRGB ApplyFog(ColorRGB color, float w)
    {
        // Visibility: 1 keeps the surface colour, 0 is fully fogged.
        var visibility = _fogMode == FogLinear
            ? System.Math.Clamp(_fogA + _fogB * w, 0f, 1f)
            : MathF.Exp(-_fogA * w);

        return ColorRGB.Lerp(_fogColor, color, visibility);
    }
}
