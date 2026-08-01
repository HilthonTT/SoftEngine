using Silk.NET.OpenGL;
using SoftEngine.Core.Geometry;
using Texture = SoftEngine.Core.Geometry.Texture;

namespace SoftEngine.Gpu;

/// <summary>
/// The engine's CPU-side <see cref="Texture"/> objects, mirrored into OpenGL and kept there.
///
/// <para>
/// Keyed by reference rather than by content: a texture is loaded once by an importer and
/// handed to every mesh that uses it, so reference identity is what "the same texture" means
/// here, and a table keyed on it uploads a shared albedo map once for a model that has fifty
/// meshes wearing it.
/// </para>
///
/// <para>
/// Everything is uploaded as plain 8-bit RGBA — never as <c>GL_SRGB8_ALPHA8</c>, which would
/// have the sampler decode colour textures for free. The decode has to stay in the shader
/// because the engine has two shading paths and only one of them wants it: with
/// <c>Scene.GammaCorrect</c> off the CPU painters multiply the encoded bytes directly, and a
/// sampler that had already linearized them would make the two backends disagree about the
/// one setting whose whole purpose is to show the difference.
/// </para>
/// </summary>
public sealed class GpuTextureCache : IDisposable
{
    private readonly GL _gl;

    // Neither type overrides Equals, so the default comparer already is reference identity.
    private readonly Dictionary<Texture, uint> _textures = [];
    private readonly Dictionary<CubeMap, uint> _cubes = [];

    // Bound wherever a sampler in the program is not otherwise used: OpenGL is entitled to
    // complain about an incomplete texture even on a sampler no branch of the shader reads,
    // and one 1x1 white texel is cheaper than proving which those are.
    private uint _white;

    public GpuTextureCache(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }

    /// <summary>A 1×1 opaque white texture, for samplers this frame has nothing to put in.</summary>
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

    /// <summary>
    /// A 1×1 white cube map, for the frames whose shading mode declares a <c>samplerCube</c>
    /// it will not read. The unit still has to hold a cube map: two samplers of different
    /// types on one unit is undefined behaviour even when no branch of the shader takes the
    /// one that would read it.
    /// </summary>
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

    /// <summary>
    /// The GL name for a texture, uploading it on first use. <paramref name="filtering"/> and
    /// <paramref name="mipMaps"/> follow the painter's own settings, so a scene rendered with
    /// filtering off looks as blocky on the GPU as it does on the CPU.
    /// </summary>
    public unsafe uint Get(Texture texture, TextureFiltering filtering, bool mipMaps)
    {
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));

        if (_textures.TryGetValue(texture, out var existing))
        {
            return existing;
        }

        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);

        // The engine stores texels as packed ARGB ints, which on a little-endian machine is
        // B, G, R, A in memory — GL_BGRA with the reversed-int packing, and no repacking pass.
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

        // Bilinear takes one level and trilinear blends two, on this backend as on the other —
        // GL says which in the minification filter's second half. Mapping bilinear to
        // LinearMipmapLinear would give the CPU's per-triangle level a smoothness the CPU
        // cannot produce, and hide the difference the mode was added to make.
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

    /// <summary>
    /// The GL name for an environment cube map, uploading it and building its mip chain on
    /// first use. The chain is what the physically-based path reflects out of: a rougher
    /// surface reads a smaller level, which is a box-filtered stand-in for the CPU's
    /// properly convolved <c>PrefilteredEnvironment</c>.
    /// </summary>
    public unsafe uint GetCube(CubeMap environment)
    {
        ArgumentNullException.ThrowIfNull(environment, nameof(environment));

        if (_cubes.TryGetValue(environment, out var existing))
        {
            return existing;
        }

        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.TextureCubeMap, handle);

        // CubeMap stores its faces in OpenGL's own order and orientation, so each one goes
        // straight to the matching target.
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

        // Sampling across a face boundary otherwise reads whatever the clamp gives, which
        // leaves a visible seam down every edge of a low-resolution sky.
        _gl.Enable(EnableCap.TextureCubeMapSeamless);

        _cubes[environment] = handle;
        return handle;
    }

    /// <summary>How many mip levels a cube map has, which is the top of the roughness ramp.</summary>
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
