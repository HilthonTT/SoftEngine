using SoftEngine.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

public static class ColorSpace
{
    private const int EncodeResolution = 4096;

    private static readonly float[] _srgbToLinear = BuildDecodeTable();
    private static readonly byte[] _linearToSrgb = BuildEncodeTable();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToLinear(byte srgb) => _srgbToLinear[srgb];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToSrgb(float linear)
    {
        var index = (int)(System.Math.Clamp(linear, 0f, 1f) * (EncodeResolution - 1) + 0.5f);
        return _linearToSrgb[index];
    }

    public static ColorRGB ScaleLinear(ColorRGB color, float intensity) => new(
        ToSrgb(intensity * _srgbToLinear[color.R]),
        ToSrgb(intensity * _srgbToLinear[color.G]),
        ToSrgb(intensity * _srgbToLinear[color.B]));

    private static float[] BuildDecodeTable()
    {
        var table = new float[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255f;
            table[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return table;
    }

    private static byte[] BuildEncodeTable()
    {
        var table = new byte[EncodeResolution];
        for (var i = 0; i < EncodeResolution; i++)
        {
            var l = i / (float)(EncodeResolution - 1);
            var s = l <= 0.0031308f ? 12.92f * l : 1.055f * MathF.Pow(l, 1f / 2.4f) - 0.055f;
            table[i] = (byte)System.Math.Clamp(s * 255f + 0.5f, 0f, 255f);
        }
        return table;
    }
}
