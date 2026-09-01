using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization;

public readonly struct RasterState
{
    private const byte FogNone = 0;
    private const byte FogLinear = 1;
    private const byte FogExponential = 2;

    private readonly float _transparency;
    private readonly byte _fogMode;
    private readonly float _fogA;
    private readonly float _fogB;
    private readonly LinearColor _fogColor;

    private readonly int _mipLevelPlusOne;

    private readonly uint _reflectance;

    private RasterState(
        float transparency,
        byte fogMode,
        float fogA,
        float fogB,
        LinearColor fogColor,
        int mipLevelPlusOne,
        uint reflectance)
    {
        _transparency = transparency;
        _fogMode = fogMode;
        _fogA = fogA;
        _fogB = fogB;
        _fogColor = fogColor;
        _mipLevelPlusOne = mipLevelPlusOne;
        _reflectance = reflectance;
    }

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
            return new RasterState(0f, FogLinear, fog.End * invRange, -invRange, fog.Color, 0, 0u);
        }

        return new RasterState(0f, FogExponential, MathF.Max(fog.Density, 0f), 0f, fog.Color, 0, 0u);
    }

    public RasterState WithOpacity(float opacity) =>
        new(1f - System.Math.Clamp(opacity, 0f, 1f), _fogMode, _fogA, _fogB, _fogColor, _mipLevelPlusOne, _reflectance);

    public RasterState WithMipLevel(int level) =>
        new(_transparency, _fogMode, _fogA, _fogB, _fogColor, System.Math.Max(level, 0) + 1, _reflectance);

    public int MipLevel => _mipLevelPlusOne - 1;

    public RasterState WithReflectance(SurfaceReflectance reflectance) =>
        new(_transparency, _fogMode, _fogA, _fogB, _fogColor, _mipLevelPlusOne, reflectance.Packed);

    public SurfaceReflectance Reflectance => SurfaceReflectance.FromPacked(_reflectance);

    public uint PackedReflectance => _reflectance;

    public bool IsOpaque => _transparency == 0f;

    public float Alpha => 1f - _transparency;

    public bool HasFog => _fogMode != FogNone;

    public LinearColor ApplyFog(LinearColor color, float w)
    {
        var visibility = _fogMode == FogLinear
            ? System.Math.Clamp(_fogA + _fogB * w, 0f, 1f)
            : MathF.Exp(-_fogA * w);

        return LinearColor.Lerp(_fogColor, color, visibility);
    }
}
