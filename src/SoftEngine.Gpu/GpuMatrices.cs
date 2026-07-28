using System.Numerics;

namespace SoftEngine.Gpu;

/// <summary>
/// The two clip-space corrections that let OpenGL and the software rasterizer agree, pixel
/// for pixel, about a scene built from the same matrices.
///
/// <para>
/// They exist because the engine's projections come from <see cref="Matrix4x4"/>, whose
/// perspective and orthographic builders follow Direct3D's conventions: depth runs from 0 at
/// the near plane to 1 at the far, and the framebuffer's Y grows downward from the top row.
/// OpenGL's clip space puts depth in [-1, 1] and its framebuffer's Y grows upward from the
/// bottom. Rather than reach for <c>glClipControl</c> — which is OpenGL 4.5, and this backend
/// targets 3.3 so it runs on the integrated parts that need it most — the fix goes into the
/// matrix, where it costs nothing at all.
/// </para>
/// </summary>
public static class GpuMatrices
{
    /// <summary>
    /// Maps Direct3D-style clip depth onto OpenGL's: <c>z' = 2z - w</c>, which after the
    /// perspective divide and the default depth range lands the window depth back on exactly
    /// the value the projection produced.
    ///
    /// That equality is the whole point. <see cref="Core.Buffers.FrameBuffer"/> quantizes the
    /// same number into its z-buffer, so a depth read back off the GPU can be written straight
    /// into it, and a shadow map rendered on the GPU stores what the CPU's
    /// <see cref="Core.Shading.ShadowMap"/> would have stored.
    /// </summary>
    public static readonly Matrix4x4 DepthZeroToOne = new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 2f, 0f,
        0f, 0f, -1f, 1f);

    /// <summary>
    /// <see cref="DepthZeroToOne"/> with the Y axis flipped as well.
    ///
    /// <para>
    /// Flipping in the projection rather than flipping the image after the read-back is what
    /// makes the transfer a straight copy: OpenGL reads its framebuffer bottom row first, and
    /// with Y already inverted that bottom row is the top row of the picture — the order
    /// <see cref="Core.Buffers.FrameBuffer.Screen"/> stores. It also means
    /// <c>gl_FragCoord.y</c> counts down from the top, which is the row index the sky pass
    /// works in.
    /// </para>
    ///
    /// It does reverse the winding of every triangle, which the renderer answers by asking
    /// OpenGL to treat clockwise faces as front-facing.
    /// </summary>
    public static readonly Matrix4x4 ScreenSpace = new(
        1f, 0f, 0f, 0f,
        0f, -1f, 0f, 0f,
        0f, 0f, 2f, 0f,
        0f, 0f, -1f, 1f);
}
