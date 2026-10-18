using Silk.NET.OpenGL;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;
using FogMode = SoftEngine.Core.Scenes.FogMode;
using Texture = SoftEngine.Core.Textures.Texture;

namespace SoftEngine.Gpu;

public sealed class GpuRenderer : IRenderer, IDisposable
{
    public const int MaxLights = 16;

    private const int MeshCacheGrace = 120;

    private readonly GpuContext _context;
    private readonly bool _ownsContext;
    private readonly GL _gl;

    private readonly GpuProgram _sceneProgram;
    private readonly GpuProgram _depthProgram;
    private readonly GpuProgram _skyProgram;
    private readonly GpuProgram _overlayProgram;
    private readonly GpuProgram _overdrawProgram;
    private readonly GpuProgram _overdrawSkyProgram;

    private GpuOverdrawPass? _overdraw;

    private readonly GpuRenderTarget _target;
    private readonly GpuShadowPass _shadows;
    private readonly GpuTextureCache _textures;

    private readonly Dictionary<IMesh, CachedMesh> _meshes = [];
    private readonly List<IMesh> _evicted = [];

    private readonly uint _emptyVertexArray;

    private readonly uint _pixelQuery;

    private float[] _vertexScratch = [];
    private uint[] _indexScratch = [];

    private ShaderLight[] _lightStorage = [];

    private readonly Vector3[] _lightVector = new Vector3[MaxLights];
    private readonly Vector3[] _lightAxis = new Vector3[MaxLights];
    private readonly Vector3[] _lightColor = new Vector3[MaxLights];
    private readonly Vector4[] _lightParams = new Vector4[MaxLights];

    private readonly List<int> _opaque = [];
    private readonly List<(int Mesh, float Depth)> _transparent = [];

    private CubeMap? _ambientSource;
    private float _ambientIntensity = float.NaN;
    private AmbientCube _ambientCube;

    private BufferVisualizer? _visualizer;

    private long _frame;
    private bool _disposed;

    private GpuRenderer(GpuContext context, bool ownsContext)
    {
        _context = context;
        _ownsContext = ownsContext;
        _gl = context.Gl;

        _sceneProgram = GpuProgram.Create(_gl, "scene.vert", "scene.frag", includeCommon: true);
        _depthProgram = GpuProgram.Create(_gl, "depth.vert", "depth.frag", includeCommon: false);
        _skyProgram = GpuProgram.Create(_gl, "fullscreen.vert", "sky.frag", includeCommon: false);
        _overlayProgram = GpuProgram.Create(_gl, "overlay.vert", "overlay.frag", includeCommon: false);
        _overdrawProgram = GpuProgram.Create(_gl, "overlay.vert", "overdraw.frag", includeCommon: false);
        _overdrawSkyProgram = GpuProgram.Create(_gl, "fullscreen.vert", "overdraw.frag", includeCommon: false);

        _target = new GpuRenderTarget(_gl);
        _shadows = new GpuShadowPass(_gl);
        _textures = new GpuTextureCache(_gl);

        _emptyVertexArray = _gl.GenVertexArray();
        _pixelQuery = _gl.GenQuery();
    }

    public static bool TryCreate(out GpuRenderer? renderer, out string? error)
    {
        renderer = null;

        if (!GpuContext.TryCreate(out var context, out error))
        {
            return false;
        }

        try
        {
            renderer = new GpuRenderer(context!, ownsContext: true);
            return true;
        }
        catch (InvalidOperationException exception)
        {
            error = $"The GPU backend could not start on {context!.Adapter.Renderer}: {exception.Message}";

            context.Dispose();
            return false;
        }
    }

    public static GpuRenderer On(GpuContext context) => new(context, ownsContext: false);

    public GpuAdapter Adapter => _context.Adapter;

    public RendererSettings Settings { get; set; } = new();

    public PostProcessStack? PostProcess { get; set; }

    public RenderStats Stats { get; } = new();

    public RenderDiagnostics Diagnostics { get; } = new();

    public void Render(Scene scene, IPainter? painter)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));
        ObjectDisposedException.ThrowIf(_disposed, this);

        _context.MakeCurrent();
        _frame++;

        var surface = scene.Surface;
        var settings = Settings;
        var events = Diagnostics.Events;

        Stats.Clear();
        Stats.CalculationTime();

        Diagnostics.FrameNumber++;
        events.Clear();
        events.Add(GraphicsEventKind.FrameBegin, -1, Diagnostics.FrameNumber);
        events.Add(GraphicsEventKind.RendererSetViewport, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

        Diagnostics.PixelHistory = null;

        var projection = scene.Projection;

        surface.SetHighDynamicRange(scene.HighDynamicRange);

        var countOverdraw = settings.DebugView == DebugView.Overdraw;
        surface.SetOverdrawCounting(countOverdraw);

        surface.SetMipLevelRecording(false);

        surface.SetReflectanceRecording(false);

        if (projection.IsOrthographic)
        {
            surface.SetLinearDepthRange();
        }
        else
        {
            surface.SetDepthRange(projection.ZNear, projection.ZFar);
        }

        events.Add(GraphicsEventKind.FrameBufferSetDepthRange, SceneObjectIds.DepthBuffer, projection.ZNear, projection.ZFar);

        var mode = GpuShading.From(painter);

        var viewMatrix = scene.Camera.ViewMatrix;
        var projectionMatrix = projection.ProjectionMatrix(surface.Width, surface.Height);
        var viewProjection = viewMatrix * projectionMatrix;

        var eye = Matrix4x4.Invert(viewMatrix, out var inverseView)
            ? inverseView.Translation
            : scene.Camera.Position;

        PrepareGeometry(scene, mode);

        var lights = LightSet.Build(scene.World, painter?.FallbackLight, ref _lightStorage);
        var shadowLight = FlattenLights(lights);

        Cull(scene, viewMatrix, projectionMatrix);

        Stats.PaintTime();

        _target.Resize(surface.Width, surface.Height, surface.IsHighDynamicRange);

        var castsShadow = shadowLight >= 0 && _shadows.Render(
            scene,
            SceneLights.Resolve(scene.World, painter?.FallbackLight),
            _depthProgram,
            mesh => _meshes.TryGetValue(mesh, out var cached) ? cached.Mesh : null,
            BindShadowCutout);

        if (castsShadow)
        {
            events.Add(GraphicsEventKind.ShadowMapRender, SceneObjectIds.ShadowMap,
                _shadows.Resolution, _shadows.TriangleCount, _shadows.CascadeCount);
        }

        scene.ShadowMap = castsShadow && settings.DebugView == DebugView.ShadowMap
            ? _shadows.ReadBack(scene.Shadows.Strength, scene.Shadows.SoftFilter)
            : null;

        events.Add(GraphicsEventKind.PainterPrepare, SceneObjectIds.Painter);

        _target.Bind();

        var clearEvent = events.Add(GraphicsEventKind.FrameBufferClearRenderTarget, SceneObjectIds.RenderTarget, surface.Width, surface.Height);
        events.Add(GraphicsEventKind.FrameBufferClearDepthBuffer, SceneObjectIds.DepthBuffer, surface.Width, surface.Height);
        surface.RecordProbeClear(clearEvent);

        _target.Clear();

        _gl.BeginQuery(QueryTarget.SamplesPassed, _pixelQuery);

        if (mode != GpuShadingMode.None)
        {
            BindFrameUniforms(scene, mode, painter, eye, viewProjection, lights.Count, shadowLight, castsShadow);

            DrawOpaque(scene, mode, painter, viewMatrix, viewProjection, events);
        }

        if (scene.ShowSky && scene.Environment is { } environment && !projection.IsOrthographic)
        {
            events.Add(GraphicsEventKind.SkyRender, SceneObjectIds.RenderTarget, surface.Width, surface.Height);
            DrawSky(scene, environment, projectionMatrix, inverseView, surface.IsHighDynamicRange);
        }

        if (mode != GpuShadingMode.None && _transparent.Count > 0)
        {
            BindFrameUniforms(scene, mode, painter, eye, viewProjection, lights.Count, shadowLight, castsShadow);

            DrawTransparent(scene, mode, painter, viewMatrix, viewProjection, events);
        }

        if (settings.ShowTriangles)
        {
            events.Add(GraphicsEventKind.WireFrameOverlayDraw, -1, _opaque.Count + _transparent.Count);
            DrawWireframe(scene, viewProjection, WireframeColor, meshIndex: -1, surface.IsHighDynamicRange);
        }

        if (settings.HighlightedMesh >= 0)
        {
            events.Add(GraphicsEventKind.WireFrameOverlayDraw, MeshId(scene) + settings.HighlightedMesh);
            DrawWireframe(scene, viewProjection, HighlightColor, settings.HighlightedMesh, surface.IsHighDynamicRange);
        }

        _gl.EndQuery(QueryTarget.SamplesPassed);

        if (countOverdraw)
        {
            CountOverdraw(scene, viewProjection);

            _target.Bind();
        }

        var needsDepth = NeedsDepthReadBack(scene, settings);

        _target.ReadBack(surface, needsDepth);

        if (!needsDepth)
        {
            surface.ClearDepth();
        }

        _gl.GetQueryObject(_pixelQuery, QueryObjectParameterName.Result, out uint drawnPixels);
        Stats.AddPixelCounts((int)System.Math.Min(drawnPixels, int.MaxValue), 0);

        SceneOverlayPass.DrawWorldGizmos(scene, settings, viewProjection, events, recordProbeContext: false);
        SceneOverlayPass.DrawTransformGizmo(scene, settings, viewProjection, events, recordProbeContext: false);

        FrameResolvePass.Resolve(surface, projection, PostProcess, events);

        if (settings.DebugView != DebugView.Off)
        {
            FrameResolvePass.RenderDebugView(
                ref _visualizer,
                surface,
                scene,
                projection,
                events,
                settings.DebugView,
                occlusion: null,
                velocity: null);
        }

        Stats.StopTime();

        events.Add(GraphicsEventKind.FramePresent, SceneObjectIds.RenderTarget, Stats.DrawnPixelCount, Stats.BehindZPixelCount);

        Diagnostics.CaptureFrame(Stats);

        EvictStaleMeshes();
    }

    private static readonly Vector3 HighlightColor = new(255f / 255f, 190f / 255f, 60f / 255f);

    private static readonly Vector3 WireframeColor = new(1f, 0f, 1f);

    private static int MeshId(Scene scene) => SceneObjectIds.Mesh(scene.World.Lights.Count, 0);

    #region Geometry

    private void PrepareGeometry(Scene scene, GpuShadingMode mode)
    {
        var meshes = scene.World.Meshes;

        var deforming = scene.World.IsAnimated;

        var wantsTangents = mode.UsesTangents();

        var maxVertices = 0;
        var maxIndices = 0;

        foreach (var mesh in meshes)
        {
            maxVertices = System.Math.Max(maxVertices, mesh.Vertices.Length);
            maxIndices = System.Math.Max(maxIndices, mesh.Triangles.Length * 3);
        }

        if (_vertexScratch.Length < maxVertices * GpuMesh.Stride)
        {
            _vertexScratch = new float[maxVertices * GpuMesh.Stride];
        }

        if (_indexScratch.Length < maxIndices)
        {
            _indexScratch = new uint[maxIndices];
        }

        foreach (var mesh in meshes)
        {
            if (mesh.Triangles.Length == 0)
            {
                continue;
            }

            if (wantsTangents && mesh.Material is { NeedsTangents: true })
            {
                mesh.EnsureTangents();
            }

            if (!_meshes.TryGetValue(mesh, out var cached))
            {
                cached = new CachedMesh(new GpuMesh(_gl));
                _meshes[mesh] = cached;
            }

            cached.LastSeen = _frame;

            cached.Mesh.Upload(mesh, _vertexScratch, _indexScratch, force: deforming);

            cached.Mesh.UploadTriangleColors(mesh);
        }
    }

    private void Cull(Scene scene, in Matrix4x4 viewMatrix, in Matrix4x4 projectionMatrix)
    {
        _opaque.Clear();
        _transparent.Clear();

        Span<Vector4> frustum = stackalloc Vector4[Frustum.PlaneCount];
        Frustum.Build(projectionMatrix, frustum);

        var meshes = scene.World.Meshes;

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            Stats.TotalTriangleCount += mesh.Triangles.Length;

            if (!mesh.Visible || mesh.Opacity <= 0f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var worldMatrix = mesh.WorldMatrix;
            var modelView = worldMatrix * viewMatrix;
            var viewCenter = Vector3.Transform(Vector3.Zero, modelView);

            var radius = mesh.WorldBoundingRadius(worldMatrix);

            if (float.IsFinite(radius) && Frustum.IsSphereOutside(frustum, viewCenter, radius))
            {
                Stats.OutOfViewTriangleCount += mesh.Triangles.Length;
                continue;
            }

            Stats.DrawnTriangleCount += mesh.Triangles.Length;

            if (mesh.Opacity < 1f)
            {
                _transparent.Add((i, viewCenter.Z));
            }
            else
            {
                _opaque.Add(i);
            }
        }

        _transparent.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
    }

    private void EvictStaleMeshes()
    {
        if (_meshes.Count == 0)
        {
            return;
        }

        _evicted.Clear();

        foreach (var (mesh, cached) in _meshes)
        {
            if (_frame - cached.LastSeen > MeshCacheGrace)
            {
                _evicted.Add(mesh);
            }
        }

        foreach (var mesh in _evicted)
        {
            _meshes[mesh].Mesh.Dispose();
            _meshes.Remove(mesh);
        }
    }

    private sealed class CachedMesh(GpuMesh mesh)
    {
        public GpuMesh Mesh { get; } = mesh;

        public long LastSeen { get; set; }
    }

    #endregion

    #region Lights and per-frame uniforms

    private int FlattenLights(LightSet lights)
    {
        var count = System.Math.Min(lights.Count, MaxLights);
        var shadowLight = -1;

        for (var i = 0; i < count; i++)
        {
            ref readonly var light = ref lights[i];

            _lightVector[i] = light.Vector;
            _lightAxis[i] = light.Axis;
            _lightColor[i] = new Vector3(light.Color.R, light.Color.G, light.Color.B);
            _lightParams[i] = new Vector4(
                light.InverseRangeSquared,
                light.CosOuter,
                light.InverseConeFalloff,
                light.IsDirectional ? 1f : 0f);

            if (light.CastsShadow)
            {
                shadowLight = i;
            }
        }

        return shadowLight;
    }

    private AmbientCube ResolveAmbient(Scene scene, IPainter? painter)
    {
        var level = painter?.AmbientLevel ?? 0f;

        if (scene.Environment is not { } environment || !scene.AmbientFromEnvironment)
        {
            return new AmbientCube(level);
        }

        if (!ReferenceEquals(environment, _ambientSource) || scene.AmbientIntensity != _ambientIntensity)
        {
            _ambientSource = environment;
            _ambientIntensity = scene.AmbientIntensity;
            _ambientCube = AmbientCube.FromEnvironment(environment, scene.AmbientIntensity);
        }

        return _ambientCube;
    }

    private void BindFrameUniforms(
        Scene scene,
        GpuShadingMode mode,
        IPainter? painter,
        Vector3 eye,
        in Matrix4x4 viewProjection,
        int lightCount,
        int shadowLight,
        bool castsShadow)
    {
        var program = _sceneProgram;
        program.Use();

        var count = System.Math.Min(lightCount, MaxLights);

        program.Set("uMode", (int)mode);
        program.Set("uEye", eye);
        program.Set("uGammaCorrect", scene.GammaCorrect);
        program.Set("uHighDynamicRange", scene.Surface.IsHighDynamicRange);
        program.Set("uViewProjection", viewProjection * GpuMatrices.ScreenSpace);

        program.Set("uLightCount", count);
        program.SetArray("uLightVector", _lightVector.AsSpan(0, System.Math.Max(count, 1)));
        program.SetArray("uLightAxis", _lightAxis.AsSpan(0, System.Math.Max(count, 1)));
        program.SetArray("uLightColor", _lightColor.AsSpan(0, System.Math.Max(count, 1)));
        program.SetArray("uLightParams", _lightParams.AsSpan(0, System.Math.Max(count, 1)));
        program.Set("uShadowLight", castsShadow ? shadowLight : -1);

        var ambient = ResolveAmbient(scene, painter);

        Span<Vector3> faces =
        [
            ToVector(ambient[CubeFace.PositiveX]),
            ToVector(ambient[CubeFace.NegativeX]),
            ToVector(ambient[CubeFace.PositiveY]),
            ToVector(ambient[CubeFace.NegativeY]),
            ToVector(ambient[CubeFace.PositiveZ]),
            ToVector(ambient[CubeFace.NegativeZ]),
        ];

        program.SetArray("uAmbient", faces);

        program.Set("uShadowMap", 6);
        program.Set("uEnvironment", 5);

        if (castsShadow)
        {
            program.Set("uShadowCascades", _shadows.CascadeCount);
            program.Set("uShadowStrength", System.Math.Clamp(scene.Shadows.Strength, 0f, 1f));
            program.Set("uShadowResolution", _shadows.Resolution);
            program.Set("uShadowSoft", scene.Shadows.SoftFilter);

            Span<Matrix4x4> matrices = stackalloc Matrix4x4[_shadows.CascadeCount];
            _shadows.Matrices.CopyTo(matrices);

            program.SetArray("uShadowMatrix", matrices);
            program.SetArray("uShadowBias", _shadows.Biases);

            _shadows.Bind(TextureUnit.Texture6);
        }
        else
        {
            program.Set("uShadowCascades", 0);
            program.Set("uShadowStrength", 0f);
            _shadows.BindPlaceholder(TextureUnit.Texture6);
        }

        var fog = scene.Fog;

        if (fog is { Enabled: true })
        {
            if (fog.Mode == FogMode.Linear)
            {
                var inverseRange = 1f / MathF.Max(fog.End - fog.Start, 1e-6f);

                program.Set("uFogMode", 1);
                program.Set("uFogA", fog.End * inverseRange);
                program.Set("uFogB", -inverseRange);
            }
            else
            {
                program.Set("uFogMode", 2);
                program.Set("uFogA", MathF.Max(fog.Density, 0f));
                program.Set("uFogB", 0f);
            }

            LinearColor color = fog.Color;
            program.Set("uFogColor", new Vector3(color.R, color.G, color.B));
        }
        else
        {
            program.Set("uFogMode", 0);
        }

        if (mode == GpuShadingMode.PhysicallyBased && scene.Environment is { } environment && scene.AmbientFromEnvironment)
        {
            _textures.BindCube(TextureUnit.Texture5, _textures.GetCube(environment));

            program.Set("uHasEnvironment", true);
            program.Set("uEnvironmentMaxLod", GpuTextureCache.MaxLevelOf(environment));
            program.Set("uAmbientIntensity", scene.AmbientIntensity);
        }
        else
        {
            _textures.BindCube(TextureUnit.Texture5, _textures.WhiteCube);
            program.Set("uHasEnvironment", false);
        }

        program.Set("uAlbedoMap", 0);
        program.Set("uNormalMap", 1);
        program.Set("uSpecularMap", 2);
        program.Set("uMetallicMap", 3);
        program.Set("uRoughnessMap", 4);
        program.Set("uEmissiveMap", 7);

        program.Set("uTriangleColors", 8);
    }

    private static Vector3 ToVector(LinearColor color) => new(color.R, color.G, color.B);

    #endregion

    #region Draw passes

    private void DrawOpaque(
        Scene scene,
        GpuShadingMode mode,
        IPainter? painter,
        in Matrix4x4 viewMatrix,
        in Matrix4x4 viewProjection,
        GraphicsEventLog events)
    {
        _gl.Enable(EnableCap.DepthTest);

        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);

        SetCulling(Settings.BackFaceCulling);

        var meshes = scene.World.Meshes;
        var idBase = MeshId(scene);

        foreach (var index in _opaque)
        {
            var mesh = meshes[index];

            events.Add(GraphicsEventKind.PainterDrawTriangles, idBase + index, mesh.Triangles.Length);

            DrawMesh(mesh, mode, painter, viewMatrix, opacity: 1f);
        }
    }

    private void DrawTransparent(
        Scene scene,
        GpuShadingMode mode,
        IPainter? painter,
        in Matrix4x4 viewMatrix,
        in Matrix4x4 viewProjection,
        GraphicsEventLog events)
    {
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        SetCulling(Settings.BackFaceCulling);

        var meshes = scene.World.Meshes;
        var idBase = MeshId(scene);

        foreach (var (index, _) in _transparent)
        {
            var mesh = meshes[index];

            events.Add(GraphicsEventKind.PainterDrawTriangles, idBase + index, mesh.Triangles.Length);

            DrawMesh(mesh, mode, painter, viewMatrix, mesh.Opacity);
        }

        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(true);
    }

    private void DrawMesh(IMesh mesh, GpuShadingMode mode, IPainter? painter, in Matrix4x4 viewMatrix, float opacity)
    {
        if (!_meshes.TryGetValue(mesh, out var cached) || cached.Mesh.IndexCount == 0)
        {
            return;
        }

        var program = _sceneProgram;
        var worldMatrix = mesh.WorldMatrix;

        program.Set("uModel", worldMatrix);
        program.Set("uModelView", worldMatrix * viewMatrix);
        program.Set("uOpacity", System.Math.Clamp(opacity, 0f, 1f));

        BindMaterial(mesh, cached.Mesh, mode, painter);

        cached.Mesh.Bind();
        cached.Mesh.Draw();
    }

    private void BindMaterial(IMesh mesh, GpuMesh geometry, GpuShadingMode mode, IPainter? painter)
    {
        var program = _sceneProgram;
        var material = mesh.Material;

        var readsMaterialColor = mode is GpuShadingMode.Material or GpuShadingMode.PhysicallyBased;

        var useTriangleColors = geometry.HasTriangleColors && !(readsMaterialColor && material is not null);

        program.Set("uHasTriangleColors", useTriangleColors);

        if (useTriangleColors)
        {
            geometry.BindTriangleColors(TextureUnit.Texture8);
        }
        else
        {
            var fallback = mesh.TriangleColors.Length > 0 ? mesh.TriangleColors[0] : ColorRGB.Gray;
            var diffuse = readsMaterialColor ? material?.Diffuse ?? fallback : fallback;

            program.Set("uBaseColor", new Vector3(diffuse.R / 255f, diffuse.G / 255f, diffuse.B / 255f));
        }

        var filtering = painter?.Filtering ?? TextureFiltering.Bilinear;
        var mipMaps = painter?.UseMipMaps ?? true;

        var textured = mode.UsesTextures() && mesh.TexCoords is not null;

        var albedo = mode == GpuShadingMode.Textured ? mesh.Texture : material?.DiffuseMap;

        BindMap(TextureUnit.Texture0, "uHasAlbedoMap", textured ? albedo : null, filtering, mipMaps);

        program.Set("uAlphaCutoff",
            textured && albedo is not null && material is { IsCutout: true } ? material.AlphaCutoff : 0f);

        if (mode is GpuShadingMode.Material or GpuShadingMode.PhysicallyBased)
        {
            var hasTangents = mesh.Tangents is not null;

            BindMap(TextureUnit.Texture1, "uHasNormalMap",
                textured && hasTangents ? material?.NormalMap : null, filtering, mipMaps);

            program.Set("uNormalStrength", material?.NormalStrength ?? 1f);
        }
        else
        {
            program.Set("uHasNormalMap", false);
        }

        if (mode == GpuShadingMode.Material)
        {
            BindMap(TextureUnit.Texture2, "uHasSpecularMap", textured ? material?.SpecularMap : null, filtering, mipMaps);

            var defaults = painter as Core.Rasterization.Painters.MaterialPainter;

            program.Set("uSpecularStrength", material?.SpecularStrength ?? defaults?.DefaultSpecularStrength ?? 0.35f);
            program.Set("uShininess", MathF.Max(material?.Shininess ?? defaults?.DefaultShininess ?? 32f, 1e-3f));
        }
        else if (mode == GpuShadingMode.Phong)
        {
            program.Set("uHasSpecularMap", false);

            program.Set("uSpecularStrength", 0.35f);
            program.Set("uShininess", 32f);
        }
        else
        {
            program.Set("uHasSpecularMap", false);
            program.Set("uSpecularStrength", 0f);
            program.Set("uShininess", 32f);
        }

        if (mode == GpuShadingMode.PhysicallyBased)
        {
            var defaults = painter as Core.Rasterization.Painters.PbrPainter;

            BindMap(TextureUnit.Texture3, "uHasMetallicMap", textured ? material?.MetallicMap : null, filtering, mipMaps);
            BindMap(TextureUnit.Texture4, "uHasRoughnessMap", textured ? material?.RoughnessMap : null, filtering, mipMaps);
            BindMap(TextureUnit.Texture7, "uHasEmissiveMap", textured ? material?.EmissiveMap : null, filtering, mipMaps);

            program.Set("uMetallic", material?.Metallic ?? defaults?.DefaultMetallic ?? 0f);
            program.Set("uRoughness", material?.Roughness ?? defaults?.DefaultRoughness ?? 0.5f);

            LinearColor emissive = material?.Emissive ?? ColorRGB.Black;
            var strength = material?.EmissiveStrength ?? 1f;

            program.Set("uEmissive", new Vector3(emissive.R, emissive.G, emissive.B) * strength);
        }
        else
        {
            program.Set("uHasMetallicMap", false);
            program.Set("uHasRoughnessMap", false);
            program.Set("uHasEmissiveMap", false);
        }
    }

    private void BindShadowCutout(IMesh mesh, GpuProgram program)
    {
        var cutout = mesh.Material is { IsCutout: true } material && mesh.TexCoords is not null
            ? material
            : null;

        if (cutout?.DiffuseMap is not { } mask)
        {
            _textures.Bind(TextureUnit.Texture0, _textures.White);
            program.Set("uAlphaCutoff", 0f);
            return;
        }

        _textures.Bind(TextureUnit.Texture0, _textures.Get(mask, TextureFiltering.Nearest, mipMaps: false));
        program.Set("uAlphaCutoff", cutout.AlphaCutoff);
    }

    private void BindMap(TextureUnit unit, string flag, Texture? texture, TextureFiltering filtering, bool mipMaps)
    {
        if (texture is null)
        {
            _textures.Bind(unit, _textures.White);
            _sceneProgram.Set(flag, false);
            return;
        }

        _textures.Bind(unit, _textures.Get(texture, filtering, mipMaps));
        _sceneProgram.Set(flag, true);
    }

    private void DrawSky(Scene scene, CubeMap environment, in Matrix4x4 projectionMatrix, in Matrix4x4 inverseView, bool highDynamicRange)
    {
        var scaleX = projectionMatrix.M11;
        var scaleY = projectionMatrix.M22;

        if (MathF.Abs(scaleX) < 1e-9f || MathF.Abs(scaleY) < 1e-9f)
        {
            return;
        }

        var surface = scene.Surface;

        _skyProgram.Use();

        _textures.BindCube(TextureUnit.Texture0, _textures.GetCube(environment));
        _skyProgram.Set("uEnvironment", 0);

        _skyProgram.SetMatrix3("uInverseViewRotation", inverseView);
        _skyProgram.Set("uInverseProjectionScale", new Vector2(1f / scaleX, 1f / scaleY));
        _skyProgram.Set("uPixelToNdc", new Vector2(
            2f / MathF.Max(surface.Width - 1, 1),
            2f / MathF.Max(surface.Height - 1, 1)));
        _skyProgram.Set("uIntensity", MathF.Max(0f, scene.SkyIntensity));
        _skyProgram.Set("uHighDynamicRange", highDynamicRange);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Equal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);

        _gl.BindVertexArray(_emptyVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
    }

    private void DrawWireframe(Scene scene, in Matrix4x4 viewProjection, Vector3 color, int meshIndex, bool highDynamicRange)
    {
        _overlayProgram.Use();
        _overlayProgram.Set("uColor", color);
        _overlayProgram.Set("uHighDynamicRange", highDynamicRange);

        _gl.Enable(EnableCap.DepthTest);

        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.Blend);

        SetCulling(Settings.BackFaceCulling);

        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);

        var clip = viewProjection * GpuMatrices.ScreenSpace;
        var meshes = scene.World.Meshes;

        foreach (var index in EnumerateDrawn())
        {
            if (meshIndex >= 0 && index != meshIndex)
            {
                continue;
            }

            var mesh = meshes[index];

            if (!_meshes.TryGetValue(mesh, out var cached))
            {
                continue;
            }

            _overlayProgram.Set("uModelViewProjection", mesh.WorldMatrix * clip);

            cached.Mesh.Bind();
            cached.Mesh.Draw();
        }

        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
    }

    private void CountOverdraw(Scene scene, in Matrix4x4 viewProjection)
    {
        _overdraw ??= new GpuOverdrawPass(_gl);

        var clip = viewProjection * GpuMatrices.ScreenSpace;
        var meshes = scene.World.Meshes;

        _overdraw.Render(
            scene.Surface,
            _overdrawProgram,
            _overdrawSkyProgram,
            _target.DepthTexture,
            _emptyVertexArray,
            Settings.BackFaceCulling,
            () =>
        {
            foreach (var index in EnumerateDrawn())
            {
                if (!_meshes.TryGetValue(meshes[index], out var cached))
                {
                    continue;
                }

                _overdrawProgram.Set("uModelViewProjection", meshes[index].WorldMatrix * clip);

                cached.Mesh.Bind();
                cached.Mesh.Draw();
            }
        });
    }

    private IEnumerable<int> EnumerateDrawn()
    {
        foreach (var index in _opaque)
        {
            yield return index;
        }

        foreach (var (index, _) in _transparent)
        {
            yield return index;
        }
    }

    private void SetCulling(bool enabled)
    {
        if (!enabled)
        {
            _gl.Disable(EnableCap.CullFace);
            return;
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        _gl.FrontFace(FrontFaceDirection.CW);
    }

    #endregion

    #region Passes over the finished image

    private bool NeedsDepthReadBack(Scene scene, RendererSettings settings) =>
        settings.ShowXZGrid
        || settings.ShowAxes
        || settings.ShowSkeleton
        || settings.Gizmo is { IsActive: true }
        || settings.DebugView != DebugView.Off
        || PostProcess is { HasEffects: true }
        || Diagnostics.IsProbing;

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _context.MakeCurrent();

        foreach (var cached in _meshes.Values)
        {
            cached.Mesh.Dispose();
        }

        _meshes.Clear();

        _gl.DeleteVertexArray(_emptyVertexArray);
        _gl.DeleteQuery(_pixelQuery);

        _textures.Dispose();
        _shadows.Dispose();
        _target.Dispose();

        _sceneProgram.Dispose();
        _depthProgram.Dispose();
        _skyProgram.Dispose();
        _overlayProgram.Dispose();
        _overdrawProgram.Dispose();
        _overdrawSkyProgram.Dispose();
        _overdraw?.Dispose();

        if (_ownsContext)
        {
            _context.Dispose();
        }
    }
}
