using Silk.NET.OpenGL;
using SoftEngine.Core.Buffers;

namespace SoftEngine.Gpu;

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

        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R32f,
            (uint)_width, (uint)_height, 0, PixelFormat.Red, PixelType.Float, null);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _texture, 0);

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
