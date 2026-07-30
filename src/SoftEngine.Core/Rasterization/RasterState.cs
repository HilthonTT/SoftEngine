using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Shading;

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
    private readonly LinearColor _fogColor;

    // Offset by one so the default value (0) means "no texture", which is what a state nobody
    // set a level on describes.
    private readonly int _mipLevelPlusOne;

    private RasterState(float transparency, byte fogMode, float fogA, float fogB, LinearColor fogColor, int mipLevelPlusOne)
    {
        _transparency = transparency;
        _fogMode = fogMode;
        _fogA = fogA;
        _fogB = fogB;
        _fogColor = fogColor;
        _mipLevelPlusOne = mipLevelPlusOne;
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
            return new RasterState(0f, FogLinear, fog.End * invRange, -invRange, fog.Color, 0);
        }

        return new RasterState(0f, FogExponential, MathF.Max(fog.Density, 0f), 0f, fog.Color, 0);
    }

    /// <summary>The same fog with a mesh's opacity; 1 keeps the state opaque.</summary>
    public RasterState WithOpacity(float opacity) =>
        new(1f - System.Math.Clamp(opacity, 0f, 1f), _fogMode, _fogA, _fogB, _fogColor, _mipLevelPlusOne);

    /// <summary>
    /// The same state, tagged with the mip level this triangle's textures are sampled from —
    /// for <see cref="Pipeline.Debugging.DebugView.MipLevel"/>, and read by nothing else.
    ///
    /// The level rides here rather than in the shader because it is a property of the
    /// triangle, chosen once by the painter from the screen footprint, and because the
    /// rasterizer is the only thing that knows which pixel a write landed on. A painter that
    /// samples no texture leaves it alone, and the view then reports the surface as untextured
    /// rather than as level 0.
    /// </summary>
    public RasterState WithMipLevel(int level) =>
        new(_transparency, _fogMode, _fogA, _fogB, _fogColor, System.Math.Max(level, 0) + 1);

    /// <summary>The mip level a write from this state samples, or -1 where there is no texture.</summary>
    public int MipLevel => _mipLevelPlusOne - 1;

    public bool IsOpaque => _transparency == 0f;

    public float Alpha => 1f - _transparency;

    public bool HasFog => _fogMode != FogNone;

    /// <summary>
    /// Blends a shaded colour toward the fog colour by the view-space depth
    /// <paramref name="w"/> (the clip-space w recovered by the rasterizer).
    ///
    /// The blend runs in linear light, which also means fog does what it physically does
    /// to an over-bright surface: a specular glint seen through thick fog is attenuated
    /// toward the fog's own brightness rather than clipped to white first.
    /// </summary>
    public LinearColor ApplyFog(LinearColor color, float w)
    {
        // Visibility: 1 keeps the surface colour, 0 is fully fogged.
        var visibility = _fogMode == FogLinear
            ? System.Math.Clamp(_fogA + _fogB * w, 0f, 1f)
            : MathF.Exp(-_fogA * w);

        return LinearColor.Lerp(_fogColor, color, visibility);
    }
}
