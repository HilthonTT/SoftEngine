using Silk.NET.OpenGL;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Gpu;

/// <summary>
/// Renders the frame's shadow cascades into a depth texture array.
///
/// <para>
/// Where the cascades go, how wide each one is, which meshes can reach it and how much bias
/// its comparison needs are all <see cref="ShadowCascadePlanner"/>'s answers — the same
/// object the CPU pass uses. Only the rasterizing is here, and it is one depth-only draw per
/// caster per cascade with colour writes switched off entirely.
/// </para>
/// </summary>
public sealed class GpuShadowPass : IDisposable
{
    private readonly GL _gl;
    private readonly ShadowCascadePlanner _planner = new();

    private uint _framebuffer;
    private uint _texture;

    private int _resolution;
    private int _cascades;

    private readonly Matrix4x4[] _matrices = new Matrix4x4[ShadowMap.MaxCascades];
    private readonly Vector2[] _biases = new Vector2[ShadowMap.MaxCascades];

    public GpuShadowPass(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }

    /// <summary>Cascades the last <see cref="Render"/> filled; 0 when the scene casts none.</summary>
    public int CascadeCount { get; private set; }

    public int Resolution => _resolution;

    /// <summary>Triangles rasterized into the map by the last <see cref="Render"/>, over every cascade.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>World space to each cascade's clip space, for the fragment shader's lookup.</summary>
    public ReadOnlySpan<Matrix4x4> Matrices => _matrices.AsSpan(0, CascadeCount);

    /// <summary>Each cascade's constant and slope-scaled bias, in normalized depth units.</summary>
    public ReadOnlySpan<Vector2> Biases => _biases.AsSpan(0, CascadeCount);

    /// <summary>
    /// Fills the cascades for one frame. Returns false — and leaves
    /// <see cref="CascadeCount"/> at zero — when the scene casts nothing, which is what the
    /// shader reads as "no shadows".
    /// </summary>
    public bool Render(
        Scene scene,
        ILight light,
        GpuProgram program,
        Func<IMesh, GpuMesh?> geometry,
        Action<IMesh, GpuProgram> bindCutout)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));
        ArgumentNullException.ThrowIfNull(program, nameof(program));
        ArgumentNullException.ThrowIfNull(geometry, nameof(geometry));
        ArgumentNullException.ThrowIfNull(bindCutout, nameof(bindCutout));

        CascadeCount = 0;
        TriangleCount = 0;

        var settings = scene.Shadows;

        if (settings is null || !settings.Enabled || settings.Strength <= 0f)
        {
            return false;
        }

        // Cascades are slices of the camera's own frustum, so the pass is told what the frame
        // is about to look at. A parallel projection has no frustum to slice, and is left to
        // the single-map path.
        ShadowView? view = null;

        if (settings.CascadeCount > 1 && !scene.Projection.IsOrthographic)
        {
            view = new ShadowView(
                scene.Camera.ViewMatrix,
                scene.Projection.ProjectionMatrix(scene.Surface.Width, scene.Surface.Height),
                scene.Projection.ZNear,
                scene.Projection.ZFar);
        }

        if (!_planner.Plan(scene.World, light, settings, view))
        {
            return false;
        }

        var cascades = _planner.CascadeCount;

        EnsureTarget(settings.Resolution, cascades);

        var meshes = scene.World.Meshes;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)_resolution, (uint)_resolution);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);

        // Shadow casting has no front and back — the CPU pass normalizes the winding away for
        // the same reason — and a mesh that is not closed would otherwise cast from one side
        // only.
        _gl.Disable(EnableCap.CullFace);
        _gl.ColorMask(false, false, false, false);

        program.Use();

        // The one sampler the depth program has, bound once for the whole pass; which texture
        // is on the unit is the per-mesh part, and bindCutout's job.
        program.Set("uAlphaMask", 0);

        for (var cascade = 0; cascade < cascades; cascade++)
        {
            var setup = _planner.SetupOf(cascade);

            _matrices[cascade] = setup.LightViewProjection;
            _biases[cascade] = new Vector2(setup.DepthBias, setup.SlopeBias);

            _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, _texture, 0, cascade);

            _gl.ClearDepth(1.0);
            _gl.Clear(ClearBufferMask.DepthBufferBit);

            // The cascade's own matrix, with the device adjustment that makes OpenGL's window
            // depth equal the normalized depth the CPU's ShadowMap stores — so the two
            // backends compare the same numbers.
            var clip = setup.LightViewProjection * GpuMatrices.DepthZeroToOne;

            foreach (var index in _planner.CastersOf(cascade))
            {
                var mesh = meshes[index];

                if (geometry(mesh) is not { IndexCount: > 0 } uploaded)
                {
                    continue;
                }

                program.Set("uLightViewProjection", mesh.WorldMatrix * clip);

                bindCutout(mesh, program);

                uploaded.Bind();
                uploaded.Draw();

                TriangleCount += mesh.Triangles.Length;
            }
        }

        _gl.ColorMask(true, true, true, true);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        CascadeCount = cascades;
        return true;
    }

    public void Bind(TextureUnit unit)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2DArray, _texture);
    }

    /// <summary>
    /// Copies the cascades into a CPU-side <see cref="ShadowMap"/>, matching what the software
    /// pass would have produced — the same depths, the same matrices, the same biases.
    ///
    /// <para>
    /// Only the shadow-map debug view asks, and it asks because <see cref="Scene.ShadowMap"/>
    /// is where the view reads from. Shading does not go through this: the fragment shader
    /// samples the texture directly, and reading a megabyte of depth back on every shadowed
    /// frame to serve a view nobody has opened would be a strange way to spend it.
    /// </para>
    /// </summary>
    public unsafe ShadowMap? ReadBack(float strength, bool softFilter)
    {
        if (CascadeCount <= 0 || _texture == 0)
        {
            return null;
        }

        if (_readBack is null || _readBack.Resolution != _resolution || _readBack.CascadeCount != CascadeCount)
        {
            _readBack = new ShadowMap(_resolution, CascadeCount);
        }

        _readBack.Begin(strength, softFilter);

        for (var cascade = 0; cascade < CascadeCount; cascade++)
        {
            _readBack.SetCascade(cascade, _matrices[cascade], _biases[cascade].X, _biases[cascade].Y);
        }

        _gl.BindTexture(TextureTarget.Texture2DArray, _texture);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 4);

        fixed (float* pointer = _readBack.Depth)
        {
            _gl.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.DepthComponent, PixelType.Float, pointer);
        }

        // The GPU stores its cascades bottom row first, as OpenGL does; ShadowMap indexes
        // from the top, as everything on the CPU side does. One flip per cascade puts the
        // texels where the view expects them.
        FlipRows(_readBack);

        return _readBack;
    }

    private void FlipRows(ShadowMap map)
    {
        var resolution = map.Resolution;
        var scratch = new float[resolution];

        for (var cascade = 0; cascade < map.CascadeCount; cascade++)
        {
            var texels = map.DepthOf(cascade);

            for (var y = 0; y < resolution / 2; y++)
            {
                var top = texels.Slice(y * resolution, resolution);
                var bottom = texels.Slice((resolution - 1 - y) * resolution, resolution);

                top.CopyTo(scratch);
                bottom.CopyTo(top);
                scratch.AsSpan().CopyTo(bottom);
            }
        }
    }

    private ShadowMap? _readBack;

    /// <summary>
    /// Binds a 1×1×1 array to the unit for a frame that casts no shadows, so the sampler is
    /// pointed at something of the right type. The shader never reads it — the cascade count
    /// is zero — but leaving the unit to some other sampler's texture is undefined behaviour
    /// whether or not any branch takes it.
    /// </summary>
    public unsafe void BindPlaceholder(TextureUnit unit)
    {
        if (_placeholder == 0)
        {
            _placeholder = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2DArray, _placeholder);

            Span<float> far = [1f];

            fixed (float* pointer = far)
            {
                _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.R32f, 1, 1, 1, 0,
                    PixelFormat.Red, PixelType.Float, pointer);
            }

            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }

        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2DArray, _placeholder);
    }

    private uint _placeholder;

    private unsafe void EnsureTarget(int resolution, int cascades)
    {
        if (_texture != 0 && resolution == _resolution && cascades == _cascades)
        {
            return;
        }

        Release();

        _resolution = System.Math.Max(1, resolution);
        _cascades = System.Math.Clamp(cascades, 1, ShadowMap.MaxCascades);

        _texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, _texture);

        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent32f,
            (uint)_resolution, (uint)_resolution, (uint)_cascades, 0,
            PixelFormat.DepthComponent, PixelType.Float, null);

        // Sampled as a plain depth value rather than through a comparison sampler: the
        // filtering the engine wants is its own 3x3 fraction-of-taps-occluded, which needs
        // the stored depths, not a hardware compare that has already averaged them.
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        // Off-map taps have to read as "nothing was drawn here", which is the far plane. The
        // shader rejects them by coordinate anyway; this keeps a driver that clamps instead
        // from casting a shadow off the edge of the map.
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        Span<float> border = [1f, 1f, 1f, 1f];
        fixed (float* pointer = border)
        {
            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, pointer);
        }

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

        // Depth only: there is no colour attachment, so the draw buffer has to say so.
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);

        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment, _texture, 0, 0);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"The shadow map framebuffer is not complete ({status}).");
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

    public void Dispose()
    {
        Release();

        if (_placeholder != 0)
        {
            _gl.DeleteTexture(_placeholder);
            _placeholder = 0;
        }
    }
}
