using Silk.NET.OpenGL;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Gpu;

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

    public int CascadeCount { get; private set; }

    public int Resolution => _resolution;

    public int TriangleCount { get; private set; }

    public ReadOnlySpan<Matrix4x4> Matrices => _matrices.AsSpan(0, CascadeCount);

    public ReadOnlySpan<Vector2> Biases => _biases.AsSpan(0, CascadeCount);

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

        _gl.Disable(EnableCap.CullFace);
        _gl.ColorMask(false, false, false, false);

        program.Use();

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

        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        Span<float> border = [1f, 1f, 1f, 1f];
        fixed (float* pointer = border)
        {
            _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, pointer);
        }

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

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
