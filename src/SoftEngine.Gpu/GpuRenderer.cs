using Silk.NET.OpenGL;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;
using FogMode = SoftEngine.Core.Scenes.FogMode;
using Texture = SoftEngine.Core.Geometry.Texture;

namespace SoftEngine.Gpu;

/// <summary>
/// The same pipeline as <see cref="Renderer"/>, with the fill on a graphics card.
///
/// <para>
/// It is a drop-in for the software renderer, not a different program: the same
/// <see cref="Scene"/>, the same <see cref="IPainter"/> choosing the shading model, the same
/// <see cref="RendererSettings"/>, and a finished frame in the same
/// <see cref="FrameBuffer"/>. What changes is where the triangles are rasterized — and,
/// consequently, how the cost of a frame scales. The software rasterizer pays per pixel per
/// triangle on a handful of cores; a graphics card pays for the same work on hundreds, which
/// is worth an order of magnitude on a dense scene at a large viewport and worth very little
/// on a cube.
/// </para>
///
/// <para>
/// The division of labour is deliberate. Everything that scales with triangles times pixels —
/// the shadow cascades, the opaque fill, the sky, the transparent blend, the wireframe — runs
/// on the GPU. Everything that runs once over the finished image and already exists on the
/// CPU — the post-process stack, the debug views, the gizmos and the grid — is left where it
/// is, over a frame read back into the engine's own buffers. Porting those too would double
/// them, and they are not what a frame's time goes on.
/// </para>
///
/// <para><b>What it does not do.</b> The per-pixel history the graphics debugger records is a
/// log of every write the software rasterizer attempted, including the ones the depth test
/// rejected. A GPU discards those inside the hardware and has nowhere to write them down, so
/// a probed pixel reports nothing here. The event list and the frame statistics are recorded
/// as usual.
/// </para>
///
/// <para>
/// An OpenGL context belongs to one thread. Construct and call this from the same thread
/// throughout — the UI thread in the viewer, the main thread in the command-line renderer.
/// </para>
/// </summary>
public sealed class GpuRenderer : IRenderer, IDisposable
{
    /// <summary>As many lights as the fragment shader declares room for.</summary>
    public const int MaxLights = 16;

    /// <summary>Frames a cached mesh may go unused before its buffers are released.</summary>
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

    // Built on the first frame that shows the overdraw view, and never otherwise.
    private GpuOverdrawPass? _overdraw;

    private readonly GpuRenderTarget _target;
    private readonly GpuShadowPass _shadows;
    private readonly GpuTextureCache _textures;

    private readonly Dictionary<IMesh, CachedMesh> _meshes = [];
    private readonly List<IMesh> _evicted = [];

    // A vertex array with nothing in it, for the draws that generate their own geometry from
    // the vertex index. A core profile refuses to draw without one bound.
    private readonly uint _emptyVertexArray;

    // Counts fragments that survived the depth test, which is the GPU's answer to the
    // software rasterizer's drawn-pixel counter.
    private readonly uint _pixelQuery;

    private float[] _vertexScratch = [];
    private uint[] _indexScratch = [];

    private ShaderLight[] _lightStorage = [];

    private readonly Vector3[] _lightVector = new Vector3[MaxLights];
    private readonly Vector3[] _lightAxis = new Vector3[MaxLights];
    private readonly Vector3[] _lightColor = new Vector3[MaxLights];
    private readonly Vector4[] _lightParams = new Vector4[MaxLights];

    // Draw order for one frame, rebuilt each time: opaque in world order, transparent sorted
    // farthest first so the blends land in the right order.
    private readonly List<int> _opaque = [];
    private readonly List<(int Mesh, float Depth)> _transparent = [];

    // The last environment reduced to an ambient cube, and what it was reduced with. The
    // reduction walks every texel of six faces, so it is kept until the scene changes one.
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

    /// <summary>
    /// Creates a renderer on its own context, or explains why it could not.
    /// <paramref name="error"/> carries a message fit to show a user — no graphics driver and
    /// no display are ordinary situations, and both mean the CPU renderer is the answer.
    /// </summary>
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
            // A driver that reports 3.3 and then fails to compile the shaders is a real
            // situation on old integrated parts, and it has to read as "no GPU here" rather
            // than as a crash.
            error = $"The GPU backend could not start on {context!.Adapter.Renderer}: {exception.Message}";

            context.Dispose();
            return false;
        }
    }

    /// <summary>Creates a renderer on a context the caller owns and will dispose.</summary>
    public static GpuRenderer On(GpuContext context) => new(context, ownsContext: false);

    /// <summary>The device this renderer is running on.</summary>
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

        // A GPU discards a failed depth test inside the hardware, where there is nothing to
        // record. Rather than report a partial history that would be read as the whole one,
        // the probe reports nothing at all.
        Diagnostics.PixelHistory = null;

        var projection = scene.Projection;

        surface.SetHighDynamicRange(scene.HighDynamicRange);

        // Counting costs a whole extra pass over the frame's geometry, so it is on only
        // while the view that reads it is being shown — as on the CPU, where the counters
        // are allocated and incremented for the same reason.
        var countOverdraw = settings.DebugView == DebugView.Overdraw;
        surface.SetOverdrawCounting(countOverdraw);

        // The mip level is chosen per triangle inside the software painters; here it is chosen
        // by the hardware's own sampler, per pixel, with nowhere to write it down. Switched
        // off rather than left alone: a frame drawn on the CPU before the backend was switched
        // would otherwise leave its levels in the buffer, and the view would present them as
        // this frame's.
        surface.SetMipLevelRecording(false);

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

        // Camera.Position is the translation fed into the view matrix, not the eye's world
        // position — invert the view matrix to get the true eye point, as the lit painters do.
        var eye = Matrix4x4.Invert(viewMatrix, out var inverseView)
            ? inverseView.Translation
            : scene.Camera.Position;

        PrepareGeometry(scene, mode);

        var lights = LightSet.Build(scene.World, painter?.FallbackLight, ref _lightStorage);
        var shadowLight = FlattenLights(lights);

        Cull(scene, viewMatrix, projectionMatrix);

        Stats.PaintTime();

        _target.Resize(surface.Width, surface.Height, surface.IsHighDynamicRange);

        // The shadow pass first: every subsequent shade reads the map, so it has to be
        // complete before any of them start.
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

        // The shadow-map view reads Scene.ShadowMap, which on this backend lives in a texture.
        // Copying it back is only worth doing when that view is open — shading samples the
        // texture directly and never needs it here.
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

        // The sky fills whatever the opaque pass left untouched. It has to run between the two
        // fills — after the opaque one so it only shades pixels no surface covered, before the
        // transparent one because that blends without writing depth, and a sky drawn afterwards
        // would paint over the glass rather than behind it.
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

            // Back to the scene target: the counter pass drew into its own.
            _target.Bind();
        }

        var needsDepth = NeedsDepthReadBack(scene, settings);

        _target.ReadBack(surface, needsDepth);

        if (!needsDepth)
        {
            // Nothing this frame is going to read the z-buffer, so it was not transferred —
            // but leaving last frame's depth in it, or the zeroes a freshly allocated one
            // starts at, is a buffer that lies: zero is the near plane, so every pixel would
            // read as having something right in front of the camera. Resetting it to the
            // cleared value says what is true, which is that this frame recorded no depth.
            surface.ClearDepth();
        }

        _gl.GetQueryObject(_pixelQuery, QueryObjectParameterName.Result, out uint drawnPixels);
        Stats.AddPixelCounts((int)System.Math.Min(drawnPixels, int.MaxValue), 0);

        // From here on the frame is an ordinary FrameBuffer again, and the passes that work
        // over a finished image are the engine's own.
        DrawCpuOverlays(scene, settings, viewProjection, events);

        ResolveFrame(surface, projection, events);

        if (settings.DebugView != DebugView.Off)
        {
            RenderDebugView(surface, scene, projection, events, settings.DebugView);
        }

        Stats.StopTime();

        events.Add(GraphicsEventKind.FramePresent, SceneObjectIds.RenderTarget, Stats.DrawnPixelCount, Stats.BehindZPixelCount);

        Diagnostics.CaptureFrame(Stats);

        EvictStaleMeshes();
    }

    /// <summary>The colour a picked mesh is outlined in, matching the software renderer's.</summary>
    private static readonly Vector3 HighlightColor = new(255f / 255f, 190f / 255f, 60f / 255f);

    private static readonly Vector3 WireframeColor = new(1f, 0f, 1f);

    private static int MeshId(Scene scene) => SceneObjectIds.Mesh(scene.World.Lights.Count, 0);

    #region Geometry

    /// <summary>
    /// Brings every mesh's buffers up to date and builds the tangent frames the mode is going
    /// to read. Both have to happen before anything is drawn: a tangent array built after the
    /// upload would not be in it.
    /// </summary>
    private void PrepareGeometry(Scene scene, GpuShadingMode mode)
    {
        var meshes = scene.World.Meshes;

        // A world that animates rewrites its vertices in place every frame, so the reference
        // check that spares a static mesh its upload cannot see the change.
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

            // Only the modes that read a base colour need them, and only a world that
            // actually varies colour per face has any.
            cached.Mesh.UploadTriangleColors(mesh);
        }
    }

    /// <summary>
    /// Splits the world into what will be drawn opaque and what will be blended, rejecting
    /// meshes whose bounding sphere is entirely outside the frustum.
    ///
    /// <para>
    /// Frustum culling survives the move to the GPU because it removes draw calls, which are
    /// the thing a GPU frame is actually short of. The occlusion pass does not: it exists to
    /// spare the software rasterizer the fill of geometry it cannot see, and the hardware's
    /// own early-depth rejection does that job without a pre-pass over the frame.
    /// </para>
    /// </summary>
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
                // Negated so an ascending sort puts the farthest mesh first. The view looks
                // down -Z, so a farther mesh has the more negative centre.
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

    /// <summary>
    /// Flattens the frame's lights into the arrays the shader reads, and returns the index of
    /// the one the shadow map was rendered from — or -1 when nothing casts.
    /// </summary>
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

    /// <summary>The frame's ambient light, reduced from the environment when it has one.</summary>
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

        // Shadows.
        //
        // The sampler is pointed at unit 6 whether or not the frame casts, and the cube map
        // at unit 5 whether or not there is one. Two samplers of different types left on the
        // same unit — which is what happens when an unused one keeps its default of 0 while
        // the albedo map is bound there — is undefined behaviour, and a driver is entitled to
        // fail the whole draw over a branch the shader never takes.
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

        // Fog
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

        // Environment, for the physically-based path's reflections.
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

        // Every sampler gets a texture unit, whether or not this frame's mode reads it: an
        // unbound sampler is undefined behaviour even on a branch nothing takes.
        program.Set("uAlbedoMap", 0);
        program.Set("uNormalMap", 1);
        program.Set("uSpecularMap", 2);
        program.Set("uMetallicMap", 3);
        program.Set("uRoughnessMap", 4);
        program.Set("uEmissiveMap", 7);
        // Unit 8 is the buffer sampler's alone, so it never collides with another type even
        // on a frame where no mesh binds one.
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

        // Less-or-equal, not less. FrameBuffer.PutPixel admits a pixel at exactly the stored
        // depth — `z <= previousDepth` — so the last coplanar surface drawn is the one that
        // shows. A strict test would let the first one win instead, and two backends that
        // disagree about which of two coplanar faces is visible disagree visibly.
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
        // Depth-tested but never depth-written, so transparent surfaces do not occlude what
        // is drawn after them — the same rule PutPixelBlend follows.
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

        // Where the base colour comes from, following the painters exactly.
        //
        // Only the material and physically-based paths read Material.Diffuse. The older modes
        // are handed the triangle's own colour by the renderer and never look at the material
        // at all — which is not an oversight but the thing that distinguishes them, and a mesh
        // carrying a dark material renders in its triangle colours under Gouraud and in the
        // material's under Material. Reading the material in both would quietly turn every
        // mode into the material one.
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

        // A mesh with no UVs shades from the flat colour: sampling a map without them would
        // read texel (0, 0) across the whole surface.
        var textured = mode.UsesTextures() && mesh.TexCoords is not null;

        // The textured mode predates materials and reads the mesh's own texture; the material
        // and physically-based modes read the material's maps.
        var albedo = mode == GpuShadingMode.Textured ? mesh.Texture : material?.DiffuseMap;

        BindMap(TextureUnit.Texture0, "uHasAlbedoMap", textured ? albedo : null, filtering, mipMaps);

        // The cutout reads the albedo map's own alpha, so it needs the same map bound and the
        // UVs to read it at. Zero is no cutout — the shader's branch then never samples.
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

            // PhongPainter shades every mesh with one highlight, whatever the material says —
            // it is the mode that predates them.
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

    /// <summary>
    /// Points the shadow pass's depth program at one caster's alpha mask, or tells it there
    /// isn't one. Lives here rather than in <see cref="GpuShadowPass"/> because the texture
    /// cache does, and uploading a map is the one thing the shadow pass would otherwise need
    /// to know about textures at all.
    ///
    /// Nearest and un-mipped, matching what the software pass samples: the shadow map's texel
    /// density has nothing to do with the camera's, so there is no screen footprint here to
    /// choose a mip level from.
    /// </summary>
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

        // Drawn at the far plane against an equality test, so it lands on exactly the pixels
        // the opaque pass left cleared — the GPU's version of asking the depth buffer what is
        // still at its clear value.
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

    /// <summary>
    /// The wireframe overlay and the picked-mesh outline, as line-mode polygons over the
    /// finished image. <paramref name="meshIndex"/> of -1 draws every mesh the frame drew.
    /// </summary>
    private void DrawWireframe(Scene scene, in Matrix4x4 viewProjection, Vector3 color, int meshIndex, bool highDynamicRange)
    {
        _overlayProgram.Use();
        _overlayProgram.Set("uColor", color);
        _overlayProgram.Set("uHighDynamicRange", highDynamicRange);

        _gl.Enable(EnableCap.DepthTest);

        // The lines sit exactly on the surfaces they outline, so an exclusive test would
        // reject every one of them. The software renderer's own z-test admits an equal depth
        // for the same reason.
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.Blend);

        // Culled the same way the fill was. The software renderer outlines the triangles in
        // its own draw list, which back-face culling has already been applied to, so a closed
        // mesh is outlined on the side facing you and not on the side facing away.
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

    /// <summary>
    /// Re-draws the frame's geometry into the overdraw counters. Every mesh the frame drew,
    /// opaque and transparent alike, because both wrote pixels and both cost what they cost.
    /// </summary>
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

        // The projection flips Y so the frame reads back in the software renderer's row
        // order, and flipping Y reverses the winding of every triangle with it. Declaring
        // clockwise faces front-facing puts it back.
        _gl.FrontFace(FrontFaceDirection.CW);
    }

    #endregion

    #region Passes over the finished image

    /// <summary>
    /// Whether anything downstream is going to read the z-buffer. The transfer is the same
    /// size as the colour one, so a frame that draws no overlays, runs no effects and shows
    /// no buffer skips it.
    /// </summary>
    private bool NeedsDepthReadBack(Scene scene, RendererSettings settings) =>
        settings.ShowXZGrid
        || settings.ShowAxes
        || settings.ShowSkeleton
        || settings.Gizmo is { IsActive: true }
        || settings.DebugView != DebugView.Off
        || PostProcess is { HasEffects: true }
        || Diagnostics.IsProbing;

    /// <summary>
    /// The overlays that are not part of the scene — the ground grid, the world axes, the
    /// skeleton and the transform handles. They are drawn on the CPU, into the frame that has
    /// just been read back, because each is a handful of depth-tested lines and reproducing
    /// them on the GPU would buy nothing measurable.
    /// </summary>
    private void DrawCpuOverlays(Scene scene, RendererSettings settings, in Matrix4x4 viewProjection, GraphicsEventLog events)
    {
        var surface = scene.Surface;

        if (settings.ShowXZGrid)
        {
            const float gridFrom = -10f;
            const float gridTo = 10f;

            var gridLines = ((int)(gridTo - gridFrom) + 1) * 2;

            events.Add(GraphicsEventKind.GizmoDrawGrid, -1, gridLines, gridFrom, gridTo);
            GizmoRenderer.DrawGrid(surface, viewProjection, gridFrom, gridTo);
        }

        if (settings.ShowAxes)
        {
            events.Add(GraphicsEventKind.GizmoDrawAxes);
            GizmoRenderer.DrawAxes(surface, viewProjection);
        }

        if (settings.ShowSkeleton && scene.World.Root is { } root)
        {
            var joints = 0;
            foreach (var _ in root.SelfAndDescendants())
            {
                joints++;
            }

            events.Add(GraphicsEventKind.GizmoDrawSkeleton, -1, joints);
            GizmoRenderer.DrawSkeleton(surface, viewProjection, root, settings.SkeletonTickSize);
        }

        if (settings.Gizmo is { IsActive: true } gizmo)
        {
            var origin = gizmo.Origin;
            var scale = TransformGizmo.HandleScale(scene, origin);

            events.Add(GraphicsEventKind.GizmoDrawTransform, -1, (int)gizmo.Mode, scale);

            GizmoRenderer.DrawTransformGizmo(
                surface,
                viewProjection,
                gizmo.Mode,
                origin,
                scale,
                gizmo.IsDragging ? gizmo.ActiveAxis : gizmo.HoveredAxis);
        }
    }

    /// <summary>
    /// The post-process stack, or — with no stack — the encode an HDR target still needs.
    /// Identical to <see cref="Renderer"/>'s, over the same buffers.
    /// </summary>
    private void ResolveFrame(FrameBuffer surface, IProjection projection, GraphicsEventLog events)
    {
        var stack = PostProcess is { HasEffects: true } candidate ? candidate : null;

        if (stack is null && !surface.IsHighDynamicRange)
        {
            return;
        }

        events.Add(GraphicsEventKind.PostProcessApply, SceneObjectIds.PostProcess,
            stack?.EnabledCount ?? 0, surface.Width, surface.Height);

        if (stack is not null)
        {
            stack.Apply(surface, projection);
        }
        else
        {
            surface.ResolveToScreen();
        }
    }

    private void RenderDebugView(FrameBuffer surface, Scene scene, IProjection projection, GraphicsEventLog events, DebugView view)
    {
        _visualizer ??= new BufferVisualizer();

        // The occlusion pyramid is the software renderer's own pre-pass and does not exist
        // here, so that view reports having nothing to show rather than presenting a stale one.
        var drawn = _visualizer.Render(surface, projection, scene.ShadowMap, view, occlusion: null);

        events.Add(GraphicsEventKind.DebugViewRender, SceneObjectIds.RenderTarget, (int)view, drawn ? 1f : 0f);
    }

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
