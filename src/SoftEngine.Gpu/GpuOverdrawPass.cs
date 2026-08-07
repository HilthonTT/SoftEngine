using Silk.NET.OpenGL;
using SoftEngine.Core.Buffers;

namespace SoftEngine.Gpu;

/// <summary>
/// Counts how many times the frame tried to write each pixel, for
/// <see cref="Core.Pipeline.Debugging.DebugView.Overdraw"/>.
///
/// <para>
/// The software rasterizer gets this for free: every write goes through
/// <see cref="FrameBuffer.PutPixel"/>, which increments a counter on the way past. A graphics
/// adapter has no such choke point — the depth test happens inside the hardware, and a
/// fragment it rejects leaves no trace anywhere the CPU can read. So the count has to be
/// asked for explicitly, with a second pass over the same geometry that writes 1 per fragment
/// into a float target with additive blending and the depth test off. The sum is the number
/// of fragments that reached each pixel.
/// </para>
///
/// <para>
/// It is not free, which is why it runs only while the view is being shown: a whole extra
/// pass over the frame's geometry, plus a read-back. That is the same bargain the CPU side
/// strikes — its counters are allocated and incremented only while something is asking for
/// them — and the view is a debugging tool, not something a frame pays for in passing.
/// </para>
/// </summary>
public sealed class GpuOverdrawPass : IDisposable
{
    private readonly GL _gl;

    private uint _framebuffer;
    private uint _texture;

    private uint _depthTexture;

    private int _width;
    private int _height;

    private float[] _readback = [];
    private int[] _counts = [];

    public GpuOverdrawPass(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }

    /// <summary>
    /// Draws the frame's geometry into the counter target and hands the totals to
    /// <paramref name="surface"/>. <paramref name="draw"/> is invoked once with the program
    /// bound and is expected to issue the frame's draws, setting the model-view-projection
    /// uniform per mesh.
    /// </summary>
    /// <param name="depthTexture">
    /// The finished frame's depth, borrowed so the sky can be counted: it owns exactly the
    /// pixels still at the clear value, and the software renderer's own sky pass counts a
    /// write on each of them.
    /// </param>
    /// <param name="backFaceCulling">
    /// Whether to drop back faces, following the frame's own setting. It has to match: the
    /// counters on the CPU side are incremented by <see cref="FrameBuffer.PutPixel"/>, which
    /// a back-facing triangle never reaches when culling is on. Counting both sides would
    /// report every closed mesh as twice the overdraw it cost.
    /// </param>
    public void Render(
        FrameBuffer surface,
        GpuProgram program,
        GpuProgram skyProgram,
        uint depthTexture,
        uint emptyVertexArray,
        bool backFaceCulling,
        Action draw)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));
        ArgumentNullException.ThrowIfNull(program, nameof(program));
        ArgumentNullException.ThrowIfNull(draw, nameof(draw));

        Resize(surface.Width, surface.Height, depthTexture);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);

        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Every fragment counts, including the ones the depth test would have thrown away —
        // the question is what the frame paid for, not what survived. The depth attachment is
        // borrowed and must not be written.
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);

        if (backFaceCulling)
        {
            _gl.Enable(EnableCap.CullFace);
            _gl.CullFace(TriangleFace.Back);
            _gl.FrontFace(FrontFaceDirection.CW);
        }
        else
        {
            _gl.Disable(EnableCap.CullFace);
        }

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        program.Use();
        draw();

        // The sky, on exactly the pixels nothing covered.
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Equal);

        skyProgram.Use();
        _gl.BindVertexArray(emptyVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.Disable(EnableCap.Blend);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);

        ReadBack(surface);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private unsafe void ReadBack(FrameBuffer surface)
    {
        var pixels = _width * _height;

        if (_readback.Length < pixels)
        {
            _readback = new float[pixels];
            _counts = new int[pixels];
        }

        _gl.ReadBuffer(ReadBufferMode.ColorAttachment0);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 4);

        fixed (float* pointer = _readback)
        {
            _gl.ReadPixels(0, 0, (uint)_width, (uint)_height, PixelFormat.Red, PixelType.Float, pointer);
        }

        for (var i = 0; i < pixels; i++)
        {
            _counts[i] = (int)_readback[i];
        }

        surface.WriteOverdraw(_counts.AsSpan(0, pixels));
    }

    private unsafe void Resize(int width, int height, uint depthTexture)
    {
        if (_framebuffer != 0 && width == _width && height == _height && depthTexture == _depthTexture)
        {
            return;
        }

        Release();

        _width = System.Math.Max(1, width);
        _height = System.Math.Max(1, height);
        _depthTexture = depthTexture;

        _texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _texture);

        // A single float channel: the count has to survive summing well past what an 8-bit
        // target could hold, and the view's own ceiling is what decides where the ramp tops
        // out rather than the format.
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R32f,
            (uint)_width, (uint)_height, 0, PixelFormat.Red, PixelType.Float, null);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _texture, 0);

        // Borrowed, never written — the scene's own depth, so the sky pass can find the
        // pixels it owns without the geometry being drawn again.
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _depthTexture, 0);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"The overdraw counter target is not complete ({status}).");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void Release()
    {
        if (_framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }

        if (_texture != 0)
        {
            _gl.DeleteTexture(_texture);
            _texture = 0;
        }
    }

    public void Dispose() => Release();
}
