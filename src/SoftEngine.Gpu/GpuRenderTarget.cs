using Silk.NET.OpenGL;
using SoftEngine.Core.Buffers;

namespace SoftEngine.Gpu;

/// <summary>
/// The off-screen framebuffer a GPU frame is drawn into, and the read-back that hands it to
/// the rest of the engine.
///
/// <para>
/// Reading a frame back off the GPU is the one thing a GPU renderer is supposed to avoid, and
/// it is deliberate here. Everything downstream of the fill — the post-process stack, the
/// screen-space effects that need a view distance, the debug views, the supersample resolve,
/// the PNG encoder, the WinForms bitmap the viewer presents through — already exists, already
/// works, and reads a <see cref="FrameBuffer"/>. Reproducing all of it in GLSL would be a
/// second implementation of each, free to disagree with the first. Handing the pixels back
/// instead costs one transfer of the finished image and buys every one of those passes,
/// unchanged, over a frame the CPU never rasterized — and the fill, which is what actually
/// scales with triangles times pixels, still happened on the GPU.
/// </para>
///
/// <para>
/// Depth comes back only when something is going to read it. A frame with no overlays, no
/// depth-reading effect and no debug view never looks at the z-buffer, and the transfer is
/// the same size as the colour one.
/// </para>
/// </summary>
public sealed class GpuRenderTarget : IDisposable
{
    private readonly GL _gl;

    private uint _framebuffer;
    private uint _color;
    private uint _depth;

    private int _width;
    private int _height;
    private bool _highDynamicRange;

    // Read-back staging. Kept across frames: at 1080p these are megabytes, and reallocating
    // them per frame would put more pressure on the collector than the render does.
    private Half[] _halfPixels = [];
    private int[] _colorPacked = [];
    private float[] _depthFloats = [];

    /// <summary>Rows one worker widens at a time; see <see cref="WidenToHdr"/>.</summary>
    private const int WidenBandRows = 32;

    /// <summary>
    /// Turns the RGBA half-float read-back into the RGB float triples
    /// <see cref="FrameBuffer.HdrColor"/> is laid out as, dropping the alpha the render
    /// target carries and the engine has no use for.
    /// </summary>
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

    /// <summary>
    /// The depth attachment, so a later pass can test against the frame's own depth without
    /// re-rendering it — which is how the overdraw counter finds the pixels the sky owns.
    /// </summary>
    public uint DepthTexture => _depth;

    /// <summary>
    /// Sizes the framebuffer and picks its colour format. A high-dynamic-range frame gets a
    /// half-float target, which is what lets a highlight above white survive to the tone map
    /// — the same reason <see cref="FrameBuffer.SetHighDynamicRange"/> exists on the CPU side.
    /// Rebuilds only when something actually changed.
    /// </summary>
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

    /// <summary>
    /// Clears colour to transparent black and depth to the far plane — the same state
    /// <see cref="FrameBuffer.Clear"/> leaves, so a pixel nothing draws over reads as
    /// background to everything downstream.
    /// </summary>
    public void Clear()
    {
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.ClearDepth(1.0);

        // A depth mask left off by the transparent pass would silently turn the depth clear
        // into a no-op, and the next frame would be composited onto the last one's z-buffer.
        _gl.DepthMask(true);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// Copies the finished frame into <paramref name="surface"/>: colour always, depth only
    /// when <paramref name="withDepth"/> — see the type summary.
    /// </summary>
    public unsafe void ReadBack(FrameBuffer surface, bool withDepth)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.ReadBuffer(ReadBufferMode.ColorAttachment0);

        // Rows are tightly packed and the frame is drawn Y-flipped, so a read-back lands in
        // exactly the row order the software renderer stores.
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        var pixels = _width * _height;

        if (_highDynamicRange)
        {
            // Read in the attachment's own format — four halves per pixel — rather than as
            // three floats.
            //
            // Asking for RGB float makes the driver drop a channel and widen the other three
            // on the way out, which is not a path any of them have a fast route for; asking
            // for RGBA half is a straight copy of what is already in memory, and two thirds
            // of the bytes. The widening still has to happen, but here it is a loop over
            // packed data that splits across the cores, instead of whatever the driver does
            // on one.
            if (_halfPixels.Length < pixels * 4)
            {
                _halfPixels = new Half[pixels * 4];
            }

            fixed (Half* pointer = _halfPixels)
            {
                _gl.ReadPixels(0, 0, (uint)_width, (uint)_height, PixelFormat.Rgba, PixelType.HalfFloat, pointer);
            }

            // HdrColor is the render target itself while HDR is on, so the frame lands where
            // the post-process stack already expects to find it.
            WidenToHdr(_halfPixels, surface.HdrColor, _width, _height);
        }
        else
        {
            if (_colorPacked.Length < pixels)
            {
                _colorPacked = new int[pixels];
            }

            // Straight into the packed ARGB ints the framebuffer stores, with the driver
            // doing the swizzle.
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
