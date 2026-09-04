using SoftEngine.Core.Buffers;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// The sampling, depth and pixel-write conventions both triangle fills share, so a scene
/// rasterized either way lands on the same pixels at the same depths with the same colours.
/// </summary>
internal static class RasterMath
{
    /// <summary>
    /// Shades one pixel and offers it to the frame buffer, feeding the debug sinks if it lands.
    /// Both fills arrive here with the same two per-pixel inputs — varying/w and w — so this is the
    /// one place the write order (alpha test, shade, fog, depth-tested put, stats, sinks) is defined.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WritePixel<TVarying, TShader>(
        FrameBuffer surface, int x, int y, int depth, float w,
        in TVarying varyingOverW,
        in TShader shader,
        in RasterState state,
        in PixelSinks sinks,
        ref int drawn,
        ref int behindZ)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var varying = TVarying.Scale(varyingOverW, w);

        if (TShader.HasAlphaTest && !shader.IsCovered(varying))
        {
            return;
        }

        var color = shader.Shade(varying);

        if (state.HasFog)
        {
            color = state.ApplyFog(color, w);
        }

        var written = state.IsOpaque
            ? surface.PutPixel(x, y, depth, color)
            : surface.PutPixelBlend(x, y, depth, color, state.Alpha);

        if (!written)
        {
            behindZ++;
            return;
        }

        drawn++;

        if (sinks.RecordMips)
        {
            surface.RecordMipLevel(x, y, sinks.MipLevel);
        }

        if (sinks.RecordReflectance)
        {
            surface.RecordReflectance(x, y, sinks.Reflectance);
        }
    }

    /// <summary>Pixels are sampled at their centre, so a coordinate covers the first centre at or after it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FirstCenterAtOrAfter(float coordinate) => (int)MathF.Ceiling(coordinate - 0.5f);

    public static readonly float MaxDepth = MathF.BitDecrement(FrameBuffer.DepthResolution);

    public static readonly Vector<float> LaneOffsets = CreateLaneOffsets();

    private static Vector<float> CreateLaneOffsets()
    {
        Span<float> lanes = stackalloc float[Vector<float>.Count];

        for (var i = 0; i < lanes.Length; i++)
        {
            lanes[i] = i;
        }

        return new Vector<float>(lanes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int QuantizeDepth(float z) => (int)System.Math.Clamp(z, 0f, MaxDepth);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> QuantizeDepths(in Vector<float> z) =>
        Vector.ConvertToInt32(Vector.Min(Vector.Max(z, Vector<float>.Zero), new Vector<float>(MaxDepth)));

    /// <summary>Depths for a run of lanes starting at <paramref name="x"/> along a linear gradient.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> DepthRun(float zBase, float dz, int x) =>
        QuantizeDepths(new Vector<float>(zBase) + (new Vector<float>(x) + LaneOffsets) * dz);
}

/// <summary>The debug buffers a written pixel also has to feed, hoisted out of the pixel loop.</summary>
internal readonly struct PixelSinks
{
    public readonly bool RecordMips;
    public readonly int MipLevel;

    public readonly bool RecordReflectance;
    public readonly uint Reflectance;

    public PixelSinks(FrameBuffer surface, in RasterState state)
    {
        RecordMips = surface.IsRecordingMipLevels;
        MipLevel = state.MipLevel;

        RecordReflectance = surface.IsRecordingReflectance;
        Reflectance = state.PackedReflectance;
    }
}
