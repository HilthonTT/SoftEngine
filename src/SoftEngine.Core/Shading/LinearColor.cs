using SoftEngine.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct LinearColor(float r, float g, float b)
{
    public readonly float R = r;
    public readonly float G = g;
    public readonly float B = b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LinearColor(ColorRGB color) =>
        new(ColorSpace.ToLinear(color.R), ColorSpace.ToLinear(color.G), ColorSpace.ToLinear(color.B));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorRGB ToColorRGB() => new(ColorSpace.ToSrgb(R), ColorSpace.ToSrgb(G), ColorSpace.ToSrgb(B));

    public float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator *(float f, LinearColor color) =>
        new(f * color.R, f * color.G, f * color.B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator *(LinearColor color, float f) =>
        new(f * color.R, f * color.G, f * color.B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator +(LinearColor x, LinearColor y) =>
        new(x.R + y.R, x.G + y.G, x.B + y.B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator *(LinearColor x, LinearColor y) =>
        new(x.R * y.R, x.G * y.G, x.B * y.B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorRGB ScaleBytes(ColorRGB color) => new(
        Saturate(R * color.R),
        Saturate(G * color.G),
        Saturate(B * color.B));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Saturate(float channel) => (byte)System.Math.Clamp(channel, 0f, 255f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor Lerp(LinearColor from, LinearColor to, float t) =>
        new(from.R + (to.R - from.R) * t,
            from.G + (to.G - from.G) * t,
            from.B + (to.B - from.B) * t);

    public static readonly LinearColor Black = new(0f, 0f, 0f);

    public static readonly LinearColor White = new(1f, 1f, 1f);
}
