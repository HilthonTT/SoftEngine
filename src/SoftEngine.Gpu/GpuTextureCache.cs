using Silk.NET.OpenGL;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Textures;
using Texture = SoftEngine.Core.Textures.Texture;

namespace SoftEngine.Gpu;

public sealed class GpuTextureCache : IDisposable
{
    private readonly GL _gl;

    private readonly Dictionary<Texture, uint> _textures = [];
    private readonly Dictionary<CubeMap, uint> _cubes = [];

    private uint _white;

    public GpuTextureCache(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }

    public unsafe uint White
    {
        get
        {
            if (_white != 0)
            {
                return _white;
            }

            _white = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _white);

            Span<byte> texel = [255, 255, 255, 255];

            fixed (byte* pointer = texel)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pointer);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            return _white;
        }
    }

    public unsafe uint WhiteCube
    {
        get
        {
            if (_whiteCube != 0)
            {
                return _whiteCube;
            }

            _whiteCube = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.TextureCubeMap, _whiteCube);

            Span<byte> texel = [255, 255, 255, 255];

            for (var face = 0; face < 6; face++)
            {
                fixed (byte* pointer = texel)
                {
                    _gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, InternalFormat.Rgba8, 1, 1, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, pointer);
                }
            }

            _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            return _whiteCube;
        }
    }

    private uint _whiteCube;

    public unsafe uint Get(Texture texture, TextureFiltering filtering, bool mipMaps)
    {
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));

        if (_textures.TryGetValue(texture, out var existing))
        {
            return existing;
        }

        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);

        fixed (int* pixels = texture.Pixels)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)texture.Width, (uint)texture.Height, 0,
                PixelFormat.Bgra, PixelType.UnsignedInt8888Rev, pixels);
        }

        if (mipMaps)
        {
            _gl.GenerateMipmap(TextureTarget.Texture2D);
        }

        var magnify = filtering == TextureFiltering.Nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear;

        var minify = (filtering, mipMaps) switch
        {
            (TextureFiltering.Trilinear, true) => TextureMinFilter.LinearMipmapLinear,
            (TextureFiltering.Bilinear or TextureFiltering.Trilinear, true) => TextureMinFilter.LinearMipmapNearest,
            (TextureFiltering.Bilinear or TextureFiltering.Trilinear, false) => TextureMinFilter.Linear,
            (_, true) => TextureMinFilter.NearestMipmapNearest,
            _ => TextureMinFilter.Nearest,
        };

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magnify);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minify);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        _textures[texture] = handle;
        return handle;
    }

    public unsafe uint GetCube(CubeMap environment)
    {
        ArgumentNullException.ThrowIfNull(environment, nameof(environment));

        if (_cubes.TryGetValue(environment, out var existing))
        {
            return existing;
        }

        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.TextureCubeMap, handle);

        for (var face = 0; face < 6; face++)
        {
            var texture = environment[(CubeFace)face];

            fixed (int* pixels = texture.Pixels)
            {
                _gl.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + face, 0, InternalFormat.Rgba8,
                    (uint)texture.Width, (uint)texture.Height, 0,
                    PixelFormat.Bgra, PixelType.UnsignedInt8888Rev, pixels);
            }
        }

        _gl.GenerateMipmap(TextureTarget.TextureCubeMap);

        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        _gl.Enable(EnableCap.TextureCubeMapSeamless);

        _cubes[environment] = handle;
        return handle;
    }

    public static float MaxLevelOf(CubeMap environment)
    {
        var size = environment[CubeFace.PositiveX].Width;
        var levels = 0;

        while (size > 1)
        {
            size >>= 1;
            levels++;
        }

        return levels;
    }

    public void Bind(TextureUnit unit, uint handle)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, handle);
    }

    public void BindCube(TextureUnit unit, uint handle)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.TextureCubeMap, handle);
    }

    public void Dispose()
    {
        foreach (var handle in _textures.Values)
        {
            _gl.DeleteTexture(handle);
        }

        foreach (var handle in _cubes.Values)
        {
            _gl.DeleteTexture(handle);
        }

        _textures.Clear();
        _cubes.Clear();

        if (_white != 0)
        {
            _gl.DeleteTexture(_white);
            _white = 0;
        }

        if (_whiteCube != 0)
        {
            _gl.DeleteTexture(_whiteCube);
            _whiteCube = 0;
        }
    }
}
