using Silk.NET.OpenGL;
using SoftEngine.Core.Buffers;

namespace SoftEngine.Gpu;

public sealed class GpuRenderTarget : IDisposable
{
    private readonly GL _gl;

    private uint _framebuffer;
    private uint _color;
    private uint _depth;

    private int _width;
    private int _height;
    private bool _highDynamicRange;

    private Half[] _halfPixels = [];
    private int[] _colorPacked = [];
    private float[] _depthFloats = [];

    private const int WidenBandRows = 32;

    private static void WidenToHdr(Half[] source, float[] destination, int width, int height)
    {
        var bands = (height + WidenBandRows - 1) / WidenBandRows;

        if (bands <= 1 || Environment.ProcessorCount <= 1)
        {
            WidenBand(source, destination, width, 0, height);
            return;
        }

        Parallel.For(0, bands, band =>
        {
            var from = band * WidenBandRows;
            WidenBand(source, destination, width, from, System.Math.Min(from + WidenBandRows, height));
        });
    }

    private static void WidenBand(Half[] source, float[] destination, int width, int rowFrom, int rowTo)
    {
        for (var y = rowFrom; y < rowTo; y++)
        {
            var read = y * width * 4;
            var write = y * width * 3;

            for (var x = 0; x < width; x++, read += 4, write += 3)
            {
                destination[write] = (float)source[read];
                destination[write + 1] = (float)source[read + 1];
                destination[write + 2] = (float)source[read + 2];
            }
        }
    }

    public GpuRenderTarget(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }

    public int Width => _width;

    public int Height => _height;

    public uint DepthTexture => _depth;

    public unsafe void Resize(int width, int height, bool highDynamicRange)
    {
        if (_framebuffer != 0 && width == _width && height == _height && highDynamicRange == _highDynamicRange)
        {
            return;
        }

        Release();

        _width = System.Math.Max(1, width);
        _height = System.Math.Max(1, height);
        _highDynamicRange = highDynamicRange;

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

        _color = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _color);
        _gl.TexImage2D(TextureTarget.Texture2D, 0,
            highDynamicRange ? InternalFormat.Rgba16f : InternalFormat.Rgba8,
            (uint)_width, (uint)_height, 0, PixelFormat.Rgba,
            highDynamicRange ? PixelType.Float : PixelType.UnsignedByte, null);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _color, 0);

        _depth = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _depth);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32f,
            (uint)_width, (uint)_height, 0, PixelFormat.DepthComponent, PixelType.Float, null);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _depth, 0);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException(
                $"The {_width}x{_height} render target is not complete ({status}).");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
    }

    public void Clear()
    {
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.ClearDepth(1.0);

        _gl.DepthMask(true);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public unsafe void ReadBack(FrameBuffer surface, bool withDepth)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.ReadBuffer(ReadBufferMode.ColorAttachment0);

        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        var pixels = _width * _height;

        if (_highDynamicRange)
        {
            if (_halfPixels.Length < pixels * 4)
            {
                _halfPixels = new Half[pixels * 4];
            }

            fixed (Half* pointer = _halfPixels)
            {
                _gl.ReadPixels(0, 0, (uint)_width, (uint)_height, PixelFormat.Rgba, PixelType.HalfFloat, pointer);
            }

            WidenToHdr(_halfPixels, surface.HdrColor, _width, _height);
        }
        else
        {
            if (_colorPacked.Length < pixels)
            {
                _colorPacked = new int[pixels];
            }

            fixed (int* pointer = _colorPacked)
            {
                _gl.ReadPixels(0, 0, (uint)_width, (uint)_height,
                    PixelFormat.Bgra, PixelType.UnsignedInt8888Rev, pointer);
            }

            _colorPacked.AsSpan(0, pixels).CopyTo(surface.Screen.AsSpan(0, pixels));
        }

        if (!withDepth)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            return;
        }

        if (_depthFloats.Length < pixels)
        {
            _depthFloats = new float[pixels];
        }

        fixed (float* pointer = _depthFloats)
        {
            _gl.ReadPixels(0, 0, (uint)_width, (uint)_height,
                PixelFormat.DepthComponent, PixelType.Float, pointer);
        }

        surface.WriteNormalizedDepth(_depthFloats.AsSpan(0, pixels));

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void Release()
    {
        if (_framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }

        if (_color != 0)
        {
            _gl.DeleteTexture(_color);
            _color = 0;
        }

        if (_depth != 0)
        {
            _gl.DeleteTexture(_depth);
            _depth = 0;
        }
    }

    public void Dispose() => Release();
}
