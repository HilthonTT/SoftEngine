using SoftEngine.Core.Buffers;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

public enum RasterizerMode
{
    /// <summary>Walk two edges down the triangle and fill the span between them.</summary>
    Scanline,

    /// <summary>Classify blocks of pixels against the three edge functions.</summary>
    HalfSpace,
}

/// <summary>
/// Chooses how triangles are filled. Both fills honour the same pixel-centre sampling rule and
/// produce the same coverage, so this is a performance choice rather than a visual one and can be
/// flipped on a running scene.
/// </summary>
public static class Rasterizer
{
    /// <summary>
    /// Which fill to use. The scanline fill is the default because it is, so far, the faster of
    /// the two on this engine: the half-space fill saves the per-span setup and the per-pixel
    /// divide, but it cannot yet spend what it saves, because every pixel is still shaded on its
    /// own. The saving turns into a win once <see cref="Shaders.IPixelShader{TVarying}"/> can
    /// shade a whole vector of pixels at a time, which is what the block traversal was built for.
    /// </summary>
    public static RasterizerMode Mode { get; set; } = RasterizerMode.Scanline;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float invW0, float invW1, float invW2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
        => Fill(surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2, shader, default, ScreenTile.Full);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float invW0, float invW1, float invW2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader,
        in ScreenTile tile)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
        => Fill(surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2, shader, default, tile);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float invW0, float invW1, float invW2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader,
        in RasterState state,
        in ScreenTile tile)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        if (Mode == RasterizerMode.HalfSpace)
        {
            HalfSpaceRasterizer.Fill(surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2, shader, state, tile);
            return;
        }

        ScanlineRasterizer.Fill(surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2, shader, state, tile);
    }
}
