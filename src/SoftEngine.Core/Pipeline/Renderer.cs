using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Pipeline.Temporal;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SoftEngine.Core.Pipeline;

public sealed partial class Renderer : IRenderer
{
    /// <summary>
    /// Below this many triangle-in-tile pairs, scheduling costs more than the parallel fill
    /// saves and the frame is filled on the calling thread.
    ///
    /// <para>
    /// Measured in tile coverage rather than in triangles, because that is the unit the fill
    /// is divided into and the unit its cost is in. The threshold used to be a triangle
    /// count, which is the same thing only while triangles are small: sixteen that each cover
    /// the viewport are fourteen million pixels of fill and were being drawn on one thread,
    /// because sixteen is fewer than thirty-two. A tile is 32×32, so this is about sixty-five
    /// thousand pixels of coverage — roughly where the join stops dominating.
    /// </para>
    /// </summary>
    private const int ParallelFillThreshold = 64;

    // How many triangles a tile draws before it re-reads its farthest depth. Rescanning
    // after every triangle would cost more than the rejections it buys.
    private const int DepthBoundRefreshInterval = 4;

    private readonly WireFramePainter _internalWireFramePainter = new();

    // Visible (mesh, triangle) pairs collected by the sequential cull phase and
    // consumed by the parallel paint phase. Reused across frames.
    private readonly List<(int MeshIndex, int TriangleIndex)> _visible = [];

    // Triangles of meshes with Opacity < 1. They skip the opaque fill and are drawn
    // afterwards, sorted back-to-front, with depth-tested but non-depth-writing blends.
    private readonly List<(int MeshIndex, int TriangleIndex)> _transparent = [];
    private float[] _transparentKeys = [];
    private (int MeshIndex, int TriangleIndex)[] _transparentOrder = [];

    // Which triangles reach which screen tile, for the parallel fill phase. Opaque and
    // transparent geometry are drawn in two passes, so each keeps its own bins.
    private readonly TileBinner _opaqueBins = new();
    private readonly TileBinner _transparentBins = new();

    // Per-pixel transparent fragments, for the order-independent path. Allocates nothing until
    // a frame both switches it on and has something transparent to store.
    private readonly FragmentBuffer _fragments = new();

    // Per-mesh index of the PainterDrawTriangles event, so a probed pixel write can point
    // back at the event that produced it. Grown to the mesh count, reused across frames.
    private int[] _meshDrawEvent = [];

    // The order phase 1 walks the meshes in, and the world matrix it composed for each while
    // deciding that order. Both are indexed by mesh and reused across frames.
    private int[] _meshOrder = [];
    private float[] _meshDepth = [];
    private Matrix4x4[] _meshWorld = [];

    // Reused across frames; rebuilt only when the world stops fitting it.
    private WorldBuffer? _worldBuffer;

    // Created on the first frame that actually needs a shadow map, and kept afterwards:
    // it owns the depth buffer and the projected-vertex scratch.
    private ShadowMapRenderer? _shadowRenderer;

    // Created on the first frame that presents a buffer instead of the image; owns the
    // depth read-back it works from.
    private BufferVisualizer? _visualizer;

    // Allocates nothing until a frame actually runs the pass, so an always-off renderer
    // pays for one empty object.
    private readonly OcclusionCuller _occlusion = new();

    // Where everything was last frame, and the buffer of per-pixel motion derived from it. Both
    // stay empty until a frame asks for something temporal.
    private readonly MotionState _motion = new();
    private readonly VelocityPass _velocityPass = new();
    private readonly VelocityBuffer _velocity = new();

    private TemporalResolver? _temporal;

    public RendererSettings Settings { get; set; } = new();

    /// <summary>
    /// Per-pixel motion since the previous frame, as the last frame that needed one left it. Empty
    /// — <see cref="VelocityBuffer.IsFilled"/> false — on any frame that asked for neither temporal
    /// antialiasing nor motion blur.
    /// </summary>
    public VelocityBuffer Velocity => _velocity;

    /// <summary>
    /// The history buffer and blend weights behind <see cref="RendererSettings.TemporalAntiAliasing"/>.
    /// Created on the first frame that switches it on.
    /// </summary>
    public TemporalResolver Temporal => _temporal ??= new TemporalResolver();

    /// <summary>The shutter and sample count behind <see cref="RendererSettings.MotionBlur"/>.</summary>
    public MotionBlur MotionBlur { get; } = new();

    /// <summary>
    /// The per-pixel fragment lists behind
    /// <see cref="RendererSettings.OrderIndependentTransparency"/> — how many fragments a pixel
    /// may hold, and what the last frame stored in them.
    /// </summary>
    public FragmentBuffer Fragments => _fragments;

    /// <summary>
    /// Forgets what the previous frame looked like and where everything was in it.
    ///
    /// A caller that has changed the scene out from under the renderer — loaded a world, resized the
    /// target, jumped the camera — should say so: temporal techniques will otherwise spend a few
    /// frames blending a picture of somewhere else into this one.
    /// </summary>
    public void ResetHistory()
    {
        _motion.Reset();
        _velocityPass.Reset();
        _temporal?.Reset();
    }

    /// <summary>
    /// The pass that rejects meshes hidden behind other meshes, and the knobs that decide what
    /// it is willing to rasterize to do it. Switched on and off through
    /// <see cref="RendererSettings.OcclusionCulling"/>.
    /// </summary>
    public OcclusionCuller Occlusion => _occlusion;

    /// <summary>
    /// Full-screen effects applied to the finished render target, in order. Null (the
    /// default) skips the pass entirely; an empty stack costs nothing either.
    /// </summary>
    public PostProcessStack? PostProcess { get; set; }

    public RenderStats Stats { get; } = new();

    public RenderDiagnostics Diagnostics { get; } = new();

    public void Render(Scene scene, IPainter? painter)
    {
        FrameBuffer surface = scene.Surface;
        ICamera camera = scene.Camera;
        IProjection projection = scene.Projection;
        IWorld world = scene.World;
        RendererSettings rendererSettings = Settings;

        RenderDiagnostics diagnostics = Diagnostics;
        GraphicsEventLog events = diagnostics.Events;
        int meshIdBase = SceneObjectIds.Mesh(world.Lights.Count, 0);

        Stats.Clear();
        Stats.PaintTime();

        diagnostics.FrameNumber++;
        events.Clear();
        events.Add(GraphicsEventKind.FrameBegin, -1, diagnostics.FrameNumber);
        events.Add(GraphicsEventKind.RendererSetViewport, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

        // A probe re-runs the whole frame with pixel-history recording on; the front-end
        // keeps it set while a pixel stays selected, so the history follows the camera.
        PixelHistory? history = null;
        if (diagnostics.IsProbing && diagnostics.ProbeX < surface.Width && diagnostics.ProbeY < surface.Height)
        {
            history = new PixelHistory(diagnostics.ProbeX, diagnostics.ProbeY, diagnostics.FrameNumber);
            surface.BeginProbe(history);
        }
        diagnostics.PixelHistory = history;

        // Both have to be set before the clear, which allocates and resets what they own.
        surface.SetHighDynamicRange(scene.HighDynamicRange);
        surface.SetOverdrawCounting(rendererSettings.DebugView == DebugView.Overdraw);
        surface.SetMipLevelRecording(rendererSettings.DebugView == DebugView.MipLevel);

        // Asked of the stack now rather than when it runs: the reflection pass reads a channel
        // only the fill can write, so the decision has to be made before a triangle is drawn.
        surface.SetReflectanceRecording(PostProcess?.NeedsReflectance ?? false);

        // Match the depth buffer to the projection's clip planes for this frame. A parallel
        // projection carries its depth in z rather than w, so it needs the other mapping.
        if (projection.IsOrthographic)
        {
            surface.SetLinearDepthRange();
        }
        else
        {
            surface.SetDepthRange(projection.ZNear, projection.ZFar);
        }

        events.Add(GraphicsEventKind.FrameBufferSetDepthRange, SceneObjectIds.DepthBuffer, projection.ZNear, projection.ZFar);

        var clearEvent = events.Add(GraphicsEventKind.FrameBufferClearRenderTarget, SceneObjectIds.RenderTarget, surface.Width, surface.Height);
        events.Add(GraphicsEventKind.FrameBufferClearDepthBuffer, SceneObjectIds.DepthBuffer, surface.Width, surface.Height);

        // Reads the pre-clear content, so it has to run before the clear itself.
        surface.RecordProbeClear(clearEvent);

        surface.Clear();
        Stats.CalculationTime();

        // model => worldMatrix => world => viewMatrix => view => projectionMatrix => projection => toNdc => ndc => toScreen => screen

        // The shadow pass runs first: painters pick the map up in Prepare and every
        // subsequent shade reads it, so it has to be complete before any of them start.
        scene.ShadowMap = RenderShadowMap(scene, events, painter);

        painter?.Prepare(scene);
        events.Add(GraphicsEventKind.PainterPrepare, SceneObjectIds.Painter);

        var viewMatrix = camera.ViewMatrix;
        var projectionMatrix = projection.ProjectionMatrix(surface.Width, surface.Height);

        // Anything temporal needs to know where every surface was last frame, and the velocity pass
        // has to measure that against the *unjittered* projection — a jitter is a change to where the
        // frame is sampled, not to where anything is, and folding it into a velocity would send every
        // pixel to read its history a third of a pixel away.
        var temporal = rendererSettings.TemporalAntiAliasing;
        var motionBlur = rendererSettings.MotionBlur;
        var needsVelocity = temporal || motionBlur || rendererSettings.DebugView == DebugView.Velocity;

        if (needsVelocity)
        {
            _velocity.Resize(surface.Width, surface.Height);
            _velocityPass.Render(world, _velocity, viewMatrix * projectionMatrix, _motion);

            events.Add(GraphicsEventKind.VelocityBufferRender, SceneObjectIds.DepthBuffer,
                _velocity.Width, _velocity.Height, _velocity.IsFilled ? 1f : 0f);
        }
        else
        {
            _velocity.Clear();
        }

        if (temporal)
        {
            // The jitter goes in after the velocities are measured and before anything is projected,
            // so every stage of this frame — culling, binning, the fill, the gizmos — agrees on where
            // the samples are.
            projectionMatrix = TemporalJitter.Apply(
                projectionMatrix,
                TemporalJitter.Offset(diagnostics.FrameNumber),
                surface.Width,
                surface.Height);
        }

        var eye = camera.Position;
        events.Add(GraphicsEventKind.CameraSetViewMatrix, SceneObjectIds.Camera, eye.X, eye.Y, eye.Z);
        events.Add(GraphicsEventKind.ProjectionSetProjectionMatrix, SceneObjectIds.Projection,
            projection.ZNear, projection.ZFar, surface.Width / (float)surface.Height);

        // View-space frustum planes for whole-mesh bounding-sphere culling.
        Span<Vector4> frustumPlanes = stackalloc Vector4[Frustum.PlaneCount];
        Frustum.Build(projectionMatrix, frustumPlanes);

        // The occlusion pass, before anything is transformed — which is the only point at
        // which rejecting a mesh saves the whole of its cost rather than the tail of it.
        //
        // A probed frame skips it, exactly as the tile's coarse depth bound does and for the
        // same reason: the pixel history has to show the writes the depth test rejects, and a
        // mesh dropped here never attempts them.
        var occlusion = rendererSettings.OcclusionCulling && !surface.IsProbing ? _occlusion : null;

        if (occlusion is not null)
        {
            occlusion.Prepare(world, viewMatrix, projectionMatrix, frustumPlanes, surface.Width, surface.Height);

            Stats.OccluderMeshCount = occlusion.OccluderCount;

            var (occlusionWidth, occlusionHeight) = (occlusion.Buffer.Width, occlusion.Buffer.Height);

            events.Add(GraphicsEventKind.OcclusionBufferRender, SceneObjectIds.DepthBuffer,
                occlusionWidth, occlusionHeight, occlusion.OccluderCount);
        }
        else
        {
            _occlusion.Reset();
        }

        // Arrays for the transformed vertices, kept across frames: rebuilding them is one
        // allocation per mesh, which at tens of thousands of meshes dominates the frame.
        if (_worldBuffer is null || !_worldBuffer.Fits(world))
        {
            _worldBuffer?.Dispose();
            _worldBuffer = new WorldBuffer(world);
        }
        else
        {
            _worldBuffer.Reset();
        }
        var worldBuffer = _worldBuffer;

        List<IMesh> meshes = world.Meshes;
        int volumeCount = meshes.Count;

        // Phase 1: transform, cull and project; collect visible triangles. Divided across the
        // cores in Renderer.CullPhase.cs, which is also where the reasons it cannot simply be a
        // parallel loop are written down.

        // Nearest mesh first, so the opaque fill draws what will still be visible before what
        // it is about to cover. See NearestFirstMeshOrder for why this is at the mesh and not
        // at the triangle. Null means draw order, which is what a probed or captured frame
        // gets: both report what happened in the order it happened.
        // One mesh has nothing to order against, and the fall-through leaves phase 1 walking
        // the world exactly as it did before there was an ordering to walk.
        var meshOrder = volumeCount > 1 && rendererSettings.NearestMeshesFirst &&
            !surface.IsProbing && !events.IsEnabled
                ? NearestFirstMeshOrder(meshes, volumeCount, viewMatrix)
                : null;

        CullPhase(
            meshes,
            volumeCount,
            meshOrder,
            worldBuffer,
            events,
            meshIdBase,
            viewMatrix,
            projectionMatrix,
            frustumPlanes,
            occlusion,
            rendererSettings.BackFaceCulling);

        Stats.PaintTime();

        // One draw event per mesh, in the order phase 1 collected them. Emitted before the
        // fill so the event list keeps pipeline order even though the fill runs in parallel.
        var drawEvents = RecordDrawEvents(events, meshIdBase, volumeCount, history is not null);

        // Phase 2 (parallel): fill the visible triangles. Each worker owns one screen tile,
        // so pixel writes never overlap, and only the triangles binned into that tile are
        // drawn there.
        var parallelFill = painter is { SupportsTiles: true } && Environment.ProcessorCount > 1;

        if (painter is not null && _visible.Count > 0)
        {
            if (!parallelFill)
            {
                PaintAll(painter, surface, meshes, worldBuffer, drawEvents, meshIdBase);
            }
            else
            {
                // Binned before the decision rather than after it: how much fill there is to
                // divide is exactly what the bins measure, and binning costs about what one
                // pass over the same triangles would.
                BinTriangles(_opaqueBins, surface, worldBuffer, _visible, _visible.Count);

                if (_opaqueBins.TotalItems < ParallelFillThreshold)
                {
                    PaintAll(painter, surface, meshes, worldBuffer, drawEvents, meshIdBase);
                }
                else
                {
                    Parallel.For(0, _opaqueBins.TileCount, t =>
                        PaintOpaqueTile(painter, surface, meshes, worldBuffer, t, drawEvents, meshIdBase));
                }
            }
        }

        // Phase 2a: the sky fills whatever the opaque pass left untouched. It has to run
        // between the two fills — after the opaque one so it only shades pixels no surface
        // covered, before the transparent one because that blends without writing depth,
        // and a sky drawn afterwards would paint over the glass rather than behind it.
        if (scene.ShowSky && scene.Environment is { } environment)
        {
            var skyEvent = events.Add(GraphicsEventKind.SkyRender, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

            SkyRenderer.Render(scene, environment, skyEvent);
        }

        // Phase 2b: transparent triangles over the finished opaque image.
        //
        // Two ways to get the blend order right, and the setting chooses between them. Sorting
        // the triangles farthest-first and blending each as it is drawn is the cheap one, and
        // tiles still parallelize safely because every tile walks the same sorted order. Storing
        // each fragment and blending the lists afterwards is the correct one, because the order
        // it decides is per pixel — see FragmentBuffer for what a per-triangle sort cannot do.
        var orderIndependent = rendererSettings.OrderIndependentTransparency;
        var transparentCount = SortTransparent(worldBuffer, orderIndependent);

        if (painter is not null && transparentCount > 0)
        {
            PaintTransparent(
                painter, surface, meshes, worldBuffer, transparentCount,
                parallelFill, orderIndependent, drawEvents, meshIdBase, events);
        }

        // The wireframe overlay draws lines across arbitrary rows, so it runs after the
        // parallel fills, sequentially. Drawing last also keeps the lines visible on top.
        if (rendererSettings.ShowTriangles)
        {
            var wireFrameEvent = events.Add(GraphicsEventKind.WireFrameOverlayDraw, -1, _visible.Count + transparentCount);

            DrawWireframeOverlay(surface, worldBuffer, _visible, wireFrameEvent, drawEvents, meshIdBase);
            DrawWireframeOverlay(surface, worldBuffer, _transparent, wireFrameEvent, drawEvents, meshIdBase);
        }

        if (rendererSettings.ShowXZGrid)
        {
            const float gridFrom = -10f;
            const float gridTo = 10f;

            // DrawGrid walks the range once, drawing a line along each axis per step.
            var gridLines = ((int)(gridTo - gridFrom) + 1) * 2;

            var gridEvent = events.Add(GraphicsEventKind.GizmoDrawGrid, -1, gridLines, gridFrom, gridTo);
            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(gridEvent, PixelWriteSource.Grid, -1, -1, null);
            }

            GizmoRenderer.DrawGrid(surface, viewMatrix * projectionMatrix, gridFrom, gridTo);
        }

        if (rendererSettings.ShowAxes)
        {
            var axesEvent = events.Add(GraphicsEventKind.GizmoDrawAxes);
            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(axesEvent, PixelWriteSource.Axes, -1, -1, null);
            }

            GizmoRenderer.DrawAxes(surface, viewMatrix * projectionMatrix);
        }

        if (rendererSettings.ShowSkeleton && world.Root is { } skeletonRoot)
        {
            var jointCount = 0;
            foreach (var _ in skeletonRoot.SelfAndDescendants())
            {
                jointCount++;
            }

            var skeletonEvent = events.Add(GraphicsEventKind.GizmoDrawSkeleton, -1, jointCount);
            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(skeletonEvent, PixelWriteSource.Skeleton, -1, -1, null);
            }

            GizmoRenderer.DrawSkeleton(surface, viewMatrix * projectionMatrix, skeletonRoot, rendererSettings.SkeletonTickSize);
        }

        // The picked mesh, outlined over everything else. Drawn before the post-process so
        // it lives in the image rather than on top of it — the same choice the wireframe
        // overlay makes, and the reason a highlighted edge blooms and tone-maps with the
        // frame instead of floating above it.
        foreach (var highlighted in rendererSettings.HighlightedMeshes)
        {
            if (highlighted >= 0)
            {
                DrawHighlight(surface, worldBuffer, events, highlighted, drawEvents, meshIdBase);
            }
        }

        // The lights, drawn into the frame like the grid and for the same reason: they describe where
        // something is, so being hidden behind the geometry they are lighting is correct.
        if (rendererSettings.ShowLights && world.Lights.Count > 0)
        {
            events.Add(GraphicsEventKind.GizmoDrawAxes, -1, world.Lights.Count);

            LightGizmo.Draw(
                surface,
                viewMatrix * projectionMatrix,
                world.Lights,
                MathF.Max(rendererSettings.SkeletonTickSize, 1e-4f) * 2f);
        }

        // The transform handles, last of the overlays so nothing draws over what you have to
        // click on. Sized here rather than by the front-end: the conversion from a fraction of
        // the viewport to world units needs the projection this frame actually used.
        if (rendererSettings.Gizmo is { IsActive: true } gizmo)
        {
            var gizmoOrigin = gizmo.Origin;
            var gizmoScale = TransformGizmo.HandleScale(scene, gizmoOrigin);

            var gizmoEvent = events.Add(
                GraphicsEventKind.GizmoDrawTransform, -1, (int)gizmo.Mode, gizmoScale);

            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(gizmoEvent, PixelWriteSource.TransformGizmo, -1, -1, null);
            }

            GizmoRenderer.DrawTransformGizmo(
                surface,
                viewMatrix * projectionMatrix,
                gizmo.Mode,
                gizmoOrigin,
                gizmoScale,
                gizmo.IsDragging ? gizmo.ActiveAxis : gizmo.HoveredAxis);
        }

        // Temporal work goes here: after the frame is shaded and before it is tone mapped. The
        // history has to be of the shaded image rather than of a bloomed and compressed one, or
        // every effect in the post chain would be re-applied to its own output next frame.
        if (temporal)
        {
            Temporal.Resolve(surface, _velocity);
            events.Add(GraphicsEventKind.PostProcessApply, SceneObjectIds.PostProcess, 1, surface.Width, surface.Height);
        }

        if (motionBlur)
        {
            MotionBlur.Apply(surface, _velocity);
            events.Add(GraphicsEventKind.PostProcessApply, SceneObjectIds.PostProcess, 1, surface.Width, surface.Height);
        }

        ResolveFrame(surface, projection, events);

        // Last of all: swap the finished image for one of the buffers that produced it. The
        // occlusion buffer is passed only when the pass actually ran, so the view says "nothing
        // to show" rather than presenting last frame's pyramid.
        if (rendererSettings.DebugView != DebugView.Off)
        {
            RenderDebugView(surface, scene, projection, events, rendererSettings.DebugView, occlusion?.Buffer);
        }

        // The frame becomes the one the next frame measures against — recorded even when nothing
        // temporal is on, so switching it on mid-flight has a previous frame to work from rather
        // than costing a frame of ghosting to acquire one.
        _motion.Advance(world, viewMatrix * projection.ProjectionMatrix(surface.Width, surface.Height));

        Stats.StopTime();

        events.Add(GraphicsEventKind.FramePresent, SceneObjectIds.RenderTarget, Stats.DrawnPixelCount, Stats.BehindZPixelCount);

        if (history is not null)
        {
            history.FinalColor = surface.GetColor(history.X, history.Y);
            history.FinalDepth = surface.GetDepth(history.X, history.Y);
            surface.EndProbe();
        }

        // Last of all, after the event list is complete and the clocks have stopped: file the
        // frame, if anyone asked for a history. Does nothing when nobody did.
        diagnostics.CaptureFrame(Stats);
    }

    /// <summary>
    /// The colour a picked mesh is outlined in. Amber rather than the wireframe overlay's
    /// magenta, so the two can be on at once and still be told apart.
    /// </summary>
    private static readonly ColorRGB HighlightColor = new(255, 190, 60);

    /// <summary>
    /// Outlines one mesh's triangles over the finished image. It walks the frame's own draw
    /// lists rather than the mesh, so what is outlined is exactly what was drawn — a mesh
    /// culled out of the frame highlights nothing, which is the honest answer.
    /// </summary>
    private void DrawHighlight(
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        GraphicsEventLog events,
        int meshIndex,
        int[]? drawEvents,
        int meshIdBase)
    {
        var highlightEvent = events.Add(GraphicsEventKind.WireFrameOverlayDraw, meshIdBase + meshIndex);

        DrawHighlightList(surface, worldBuffer, _visible, meshIndex, highlightEvent, drawEvents, meshIdBase);
        DrawHighlightList(surface, worldBuffer, _transparent, meshIndex, highlightEvent, drawEvents, meshIdBase);
    }

    private void DrawHighlightList(
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        List<(int MeshIndex, int TriangleIndex)> list,
        int meshIndex,
        int highlightEvent,
        int[]? drawEvents,
        int meshIdBase)
    {
        foreach (var (mesh, triangleIndex) in list)
        {
            if (mesh != meshIndex)
            {
                continue;
            }

            var vbx = worldBuffer.VertexBuffers[mesh];

            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(
                    highlightEvent,
                    PixelWriteSource.WireFrame,
                    meshIdBase + mesh,
                    vbx.SourceTriangleIndex(triangleIndex),
                    vbx);
            }

            _internalWireFramePainter.DrawTriangle(surface, HighlightColor, vbx, triangleIndex, ScreenTile.Full);
        }
    }

    /// <summary>
    /// Presents one of the frame's buffers in place of the shaded image. A probed pixel gets
    /// one more history entry for it, as the post-process pass does: it replaces the whole
    /// image at once, so there is no per-triangle write to attribute.
    /// </summary>
    private void RenderDebugView(
        FrameBuffer surface,
        Scene scene,
        IProjection projection,
        GraphicsEventLog events,
        DebugView view,
        OcclusionBuffer? occlusion)
    {
        _visualizer ??= new BufferVisualizer();

        var before = surface.IsProbing ? surface.GetProbedColor() : 0;

        // The event is logged after the fact, carrying whether the buffer could actually be
        // shown: a view the frame has nothing for leaves the image alone, and a log claiming
        // it redrew the frame would send you looking for a pass that never ran.
        var drawn = _visualizer.Render(surface, projection, scene.ShadowMap, view, occlusion, _velocity);

        var eventIndex = events.Add(
            GraphicsEventKind.DebugViewRender, SceneObjectIds.RenderTarget, (int)view, drawn ? 1f : 0f);

        if (drawn && surface.IsProbing)
        {
            surface.RecordProbeOverwrite(eventIndex, PixelWriteSource.DebugView, SceneObjectIds.RenderTarget, before);
        }
    }

    /// <summary>
    /// Renders the world's depth from the scene's first light, or returns null when the
    /// scene casts no shadows — disabled, no lights, or nothing opaque to cast one.
    /// </summary>
    private ShadowMap? RenderShadowMap(Scene scene, GraphicsEventLog events, IPainter? painter)
    {
        var settings = scene.Shadows;

        if (settings is null || !settings.Enabled || settings.Strength <= 0f)
        {
            return null;
        }

        _shadowRenderer ??= new ShadowMapRenderer();

        // Cascades are slices of the camera's own frustum, so the pass is told what the frame
        // is about to look at. A parallel projection has no frustum to slice — its shadow map
        // already covers the view uniformly, which is the thing cascades exist to fix — so it
        // is left to the single-map path.
        ShadowView? view = null;

        if (settings.CascadeCount > 1 && !scene.Projection.IsOrthographic)
        {
            view = new ShadowView(
                scene.Camera.ViewMatrix,
                scene.Projection.ProjectionMatrix(scene.Surface.Width, scene.Surface.Height),
                scene.Projection.ZNear,
                scene.Projection.ZFar);
        }

        // The same resolution the lit painters use, so the scene is shadowed from wherever
        // it is lit from — including the fallback light of a world that declares none, which
        // is why the painter's own fallback is passed rather than left to default. A painter
        // built around a particular light and pointed at a world with none of its own would
        // otherwise shade from that light and cast shadows from a different one.
        var map = _shadowRenderer.Render(
            scene.World,
            SceneLights.Resolve(scene.World, painter?.FallbackLight),
            settings,
            view);

        if (map is not null)
        {
            events.Add(GraphicsEventKind.ShadowMapRender, SceneObjectIds.ShadowMap,
                map.Resolution, _shadowRenderer.TriangleCount, map.CascadeCount);
        }

        return map;
    }

    /// <summary>
    /// Turns whatever the rasterizer produced into the packed sRGB image that gets
    /// presented. Normally that is the post-process stack, which reads the target, runs its
    /// effects in linear light and encodes the result. With no stack there is still work to
    /// do on an HDR target, whose pixels are floats nothing has encoded yet.
    ///
    /// A probed pixel gets one more history entry for the whole pass: it works on the image
    /// as a whole rather than pixel by pixel, so there is no per-triangle write to attribute.
    /// </summary>
    private void ResolveFrame(FrameBuffer surface, IProjection projection, GraphicsEventLog events)
    {
        var stack = PostProcess is { HasEffects: true } candidate ? candidate : null;

        if (stack is null && !surface.IsHighDynamicRange)
        {
            return;
        }

        var eventIndex = stack is not null
            ? events.Add(GraphicsEventKind.PostProcessApply, SceneObjectIds.PostProcess,
                stack.EnabledCount, surface.Width, surface.Height)
            : events.Add(GraphicsEventKind.PostProcessApply, SceneObjectIds.PostProcess,
                0, surface.Width, surface.Height);

        var before = surface.IsProbing ? surface.GetProbedColor() : 0;

        if (stack is not null)
        {
            // The projection goes with it so depth-reading effects can turn the depth
            // buffer back into positions in view space.
            stack.Apply(surface, projection);
        }
        else
        {
            surface.ResolveToScreen();
        }

        if (surface.IsProbing)
        {
            surface.RecordProbeOverwrite(eventIndex, PixelWriteSource.PostProcess, SceneObjectIds.PostProcess, before);
        }
    }

    /// <summary>
    /// Emits one <see cref="GraphicsEventKind.PainterDrawTriangles"/> event per mesh that
    /// survived culling. Returns the per-mesh event indices when a pixel is being probed
    /// (the paint phase tags its writes with them), otherwise null.
    /// </summary>
    /// <summary>
    /// The mesh indices in nearest-first order, and the world matrix composed for each along
    /// the way.
    ///
    /// <para>
    /// It is what makes <see cref="RendererSettings.HierarchicalZ"/> worth having on a scene
    /// nobody authored front to back. A tile rejects triangles against the farthest depth
    /// already drawn into it, so that bound can only be as near as the nearest thing already
    /// there: a pass that happens to work backwards fills most of the tile with pixels it is
    /// about to cover and only tightens the bound once there is nothing left to reject.
    /// Nearest-first, the surfaces that will still be visible when the tile is finished are
    /// the ones drawn first, and everything behind them meets a bound that is already final.
    /// </para>
    ///
    /// <para>
    /// At the mesh and not at the triangle, which was measured and is the whole point. Sorting
    /// each tile's binned triangles orders the fill perfectly and costs more than it saves
    /// twice over: the sort itself, and then the fill reading its vertices in depth order out
    /// of arrays far larger than the cache, which on a dense model is most of the frame's time.
    /// A mesh keeps its triangles contiguous and in their original order, so the reads stay
    /// sequential and the sort is over thousands of meshes rather than hundreds of thousands
    /// of triangle-in-tile pairs.
    /// </para>
    ///
    /// <para>
    /// It cannot change the picture: the depth test, not the visit order, decides which of two
    /// opaque surfaces is seen. Transparent meshes are collected into their own list and sorted
    /// back-to-front afterwards regardless, so what this does to their order is discarded.
    /// </para>
    /// </summary>
    private int[] NearestFirstMeshOrder(List<IMesh> meshes, int meshCount, in Matrix4x4 viewMatrix)
    {
        if (_meshOrder.Length < meshCount)
        {
            var capacity = System.Math.Max(meshCount, _meshOrder.Length * 2);

            _meshOrder = new int[capacity];
            _meshDepth = new float[capacity];
            _meshWorld = new Matrix4x4[capacity];
        }

        for (var i = 0; i < meshCount; i++)
        {
            var worldMatrix = meshes[i].WorldMatrix;
            _meshWorld[i] = worldMatrix;

            // View-space depth of the mesh's origin, negated. View space is right-handed —
            // the camera looks down -Z — so the nearest mesh has the *largest* Z, and the
            // negation is what makes an ascending sort put it first. Sorting the raw Z draws
            // the scene strictly back to front and costs about half the frame rate on
            // anything with depth to it, which is the same measurement upside down.
            //
            // The centre rather than the nearest point of the bounding sphere: measuring that
            // means walking the vertices, which is the work this ordering exists to save. A
            // visit order that is slightly wrong costs a rejection that did not happen, never
            // a pixel that came out wrong.
            _meshDepth[i] = -Vector3.Transform(Vector3.Zero, worldMatrix * viewMatrix).Z;
            _meshOrder[i] = i;
        }

        _meshDepth.AsSpan(0, meshCount).Sort(_meshOrder.AsSpan(0, meshCount));

        return _meshOrder;
    }

    private int[]? RecordDrawEvents(GraphicsEventLog events, int meshIdBase, int meshCount, bool probing)
    {
        if (!events.IsEnabled && !probing)
        {
            return null;
        }

        if (_meshDrawEvent.Length < meshCount)
        {
            _meshDrawEvent = new int[System.Math.Max(meshCount, _meshDrawEvent.Length * 2)];
        }

        Array.Fill(_meshDrawEvent, -1, 0, meshCount);

        // A mesh is either fully opaque or fully transparent, so the two lists never
        // record an event for the same mesh index.
        RecordDrawEventRuns(events, meshIdBase, _visible);
        RecordDrawEventRuns(events, meshIdBase, _transparent);

        return probing ? _meshDrawEvent : null;
    }

    private void RecordDrawEventRuns(GraphicsEventLog events, int meshIdBase, List<(int MeshIndex, int TriangleIndex)> list)
    {
        var count = list.Count;
        var i = 0;
        while (i < count)
        {
            var meshIndex = list[i].MeshIndex;

            var run = i;
            while (run < count && list[run].MeshIndex == meshIndex)
            {
                run++;
            }

            _meshDrawEvent[meshIndex] = events.Add(GraphicsEventKind.PainterDrawTriangles, meshIdBase + meshIndex, run - i);
            i = run;
        }
    }

    /// <summary>
    /// Triangles below which binning on the calling thread beats spreading it across the
    /// cores. Well under what any scene the tiled fill is worth running for produces.
    /// </summary>
    private const int ParallelBinThreshold = 2048;

    /// <summary>How many triangles one bounds worker takes at a time.</summary>
    private const int BinBandSize = 512;

    // The draw list being binned, and the screen-space bounds measured from it: minX, minY,
    // maxX, maxY, minZ per triangle. Both are reused across frames, and across the opaque and
    // transparent passes — those never overlap, since the whole opaque fill has finished by
    // the time anything transparent is binned.
    private (int MeshIndex, int TriangleIndex)[] _binSource = [];
    private float[] _binBounds = [];

    /// <summary>
    /// Bins a draw list's triangles by the screen tiles their bounding boxes touch. The
    /// projection to screen space is repeated by the painter that eventually fills the
    /// triangle, but doing it here — once — is what lets a tile skip the triangles that never
    /// reach it.
    ///
    /// <para>
    /// Measuring a triangle and filing it are separated because only one of them has to happen
    /// in order. Measuring is a pure map — three vertices in, a rectangle out — and it is the
    /// expensive half: each triangle reaches into its mesh's vertex buffer at three unrelated
    /// indices, and on a dense model those arrays are far larger than the cache, so the pass is
    /// bound by memory latency rather than by the arithmetic. Latency is exactly what spreading
    /// the work over the cores hides, so the rectangles are measured in parallel bands. Filing
    /// them stays sequential: the bins are a counting sort, and a triangle's ordinal is its
    /// position in draw order, which is what the transparent blend and the pixel probe both
    /// read back.
    /// </para>
    /// </summary>
    private void BinTriangles(
        TileBinner bins,
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        ReadOnlySpan<(int MeshIndex, int TriangleIndex)> list,
        int count)
    {
        bins.Reset(surface.Width, surface.Height);

        if (count < ParallelBinThreshold || Environment.ProcessorCount <= 1)
        {
            for (var i = 0; i < count; i++)
            {
                var (minX, minY, maxX, maxY, minZ) = Bounds(surface, worldBuffer, list[i]);
                bins.Add(minX, minY, maxX, maxY, minZ);
            }

            bins.Build();
            return;
        }

        // Copied into a field rather than measured straight off the span: a span cannot be
        // captured by the worker below, and the copy is a few bytes per triangle of sequential
        // memory against three scattered vertex reads it lets run concurrently.
        if (_binSource.Length < count)
        {
            _binSource = new (int, int)[System.Math.Max(count, _binSource.Length * 2)];
            _binBounds = new float[_binSource.Length * 5];
        }

        var source = _binSource;
        var bounds = _binBounds;

        list[..count].CopyTo(source);

        var bands = (count + BinBandSize - 1) / BinBandSize;

        Parallel.For(0, bands, band =>
        {
            var from = band * BinBandSize;
            var to = System.Math.Min(from + BinBandSize, count);

            for (var i = from; i < to; i++)
            {
                var (minX, minY, maxX, maxY, minZ) = Bounds(surface, worldBuffer, source[i]);

                var slot = i * 5;
                bounds[slot] = minX;
                bounds[slot + 1] = minY;
                bounds[slot + 2] = maxX;
                bounds[slot + 3] = maxY;
                bounds[slot + 4] = minZ;
            }
        });

        for (var i = 0; i < count; i++)
        {
            var slot = i * 5;
            bins.Add(bounds[slot], bounds[slot + 1], bounds[slot + 2], bounds[slot + 3], bounds[slot + 4]);
        }

        bins.Build();
    }

    /// <summary>The screen-space bounding box of one draw-list entry, and its nearest depth.</summary>
    private static (float MinX, float MinY, float MaxX, float MaxY, float MinZ) Bounds(
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        (int MeshIndex, int TriangleIndex) entry)
    {
        var vbx = worldBuffer.VertexBuffers[entry.MeshIndex];
        var t = vbx.GetTriangle(entry.TriangleIndex);

        var p0 = surface.ToScreen3(vbx.GetVertex(t.I0).Proj);
        var p1 = surface.ToScreen3(vbx.GetVertex(t.I1).Proj);
        var p2 = surface.ToScreen3(vbx.GetVertex(t.I2).Proj);

        return (
            MathF.Min(p0.X, MathF.Min(p1.X, p2.X)),
            MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)),
            MathF.Max(p0.X, MathF.Max(p1.X, p2.X)),
            MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)),
            MathF.Min(p0.Z, MathF.Min(p1.Z, p2.Z)));
    }

    private void BinTriangles(TileBinner bins, FrameBuffer surface, WorldBuffer worldBuffer, List<(int MeshIndex, int TriangleIndex)> list, int count) =>
        BinTriangles(bins, surface, worldBuffer, CollectionsMarshal.AsSpan(list), count);

    private void BinTriangles(TileBinner bins, FrameBuffer surface, WorldBuffer worldBuffer, (int MeshIndex, int TriangleIndex)[] list, int count) =>
        BinTriangles(bins, surface, worldBuffer, list.AsSpan(0, count), count);

    private void PaintOpaqueTile(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int tileIndex, int[]? drawEvents, int meshIdBase)
    {
        var ordinals = _opaqueBins.TrianglesIn(tileIndex);
        if (ordinals.Length == 0)
        {
            return;
        }

        var tile = _opaqueBins.TileAt(tileIndex);

        // Coarse depth rejection. The bound is the farthest depth anywhere in the tile, so a
        // triangle whose nearest point is behind it cannot be seen here — no rows walked, no
        // pixels tested. It is refreshed every few triangles rather than after each one: the
        // scan costs a tile's worth of reads, and one bound covers a run of triangles.
        //
        // A refresh that buys no rejection at all doubles the interval to the next one, so a
        // scene with no depth complexity — where the bound can never reject anything — stops
        // paying for the scans after a few of them instead of on every triangle.
        //
        // A probed frame skips this: the pixel history has to show the writes the depth test
        // rejects, and a triangle dropped here never attempts them.
        var hierarchicalZ = Settings.HierarchicalZ && !surface.IsProbing;
        var bound = int.MaxValue;
        var interval = DepthBoundRefreshInterval;
        var sinceRefresh = 0;
        var rejectedSinceRefresh = 0;
        var rejected = 0;

        foreach (var ordinal in ordinals)
        {
            if (_opaqueBins.NearestDepth(ordinal) > bound)
            {
                rejected++;
                rejectedSinceRefresh++;
                continue;
            }

            var (meshIndex, triangleIndex) = _visible[ordinal];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, tile, drawEvents, meshIdBase);

            if (hierarchicalZ && ++sinceRefresh >= interval)
            {
                bound = surface.MaxDepthIn(tile.XFrom, tile.YFrom, tile.XTo, tile.YTo);

                interval = rejectedSinceRefresh == 0 ? interval * 2 : DepthBoundRefreshInterval;
                sinceRefresh = 0;
                rejectedSinceRefresh = 0;
            }
        }

        if (rejected > 0)
        {
            Stats.AddOccludedTriangles(rejected);
        }
    }

    /// <summary>
    /// Draws this frame's transparent triangles over the finished opaque image, either blending
    /// each as it is filled or storing its fragments for a resolve that blends them per pixel.
    ///
    /// <para>
    /// The two share everything except where the blend happens: the same draw list, the same
    /// binning, the same choice between a tiled fill and a sequential one, and the same depth
    /// test against opaque geometry. What the order-independent path adds is an arena per tile,
    /// set on the worker before it fills and taken off it afterwards, and the resolve at the end.
    /// </para>
    /// </summary>
    private void PaintTransparent(
        IPainter painter,
        FrameBuffer surface,
        List<IMesh> meshes,
        WorldBuffer worldBuffer,
        int transparentCount,
        bool parallelFill,
        bool orderIndependent,
        int[]? drawEvents,
        int meshIdBase,
        GraphicsEventLog events)
    {
        // Whether the pass is tiled decides how many arenas there are, so it has to be settled
        // before the first fragment is stored rather than discovered while storing them.
        var tiled = false;

        if (parallelFill)
        {
            BinTriangles(_transparentBins, surface, worldBuffer, _transparentOrder, transparentCount);
            tiled = _transparentBins.TotalItems >= ParallelFillThreshold;
        }

        if (!orderIndependent)
        {
            if (tiled)
            {
                Parallel.For(0, _transparentBins.TileCount, t =>
                    PaintTransparentTile(painter, surface, meshes, worldBuffer, t, drawEvents, meshIdBase));
            }
            else
            {
                PaintTransparentAll(painter, surface, meshes, worldBuffer, transparentCount, drawEvents, meshIdBase);
            }

            return;
        }

        _fragments.Begin(tiled ? _transparentBins.TileCount : 1, surface.IsProbing);

        if (tiled)
        {
            Parallel.For(0, _transparentBins.TileCount, t =>
            {
                // A tile no transparent triangle reaches gets no arena at all: an arena carries
                // a slot per pixel of its rectangle, and a screen's worth of them for the two
                // tiles a window actually covers is the allocation this check avoids.
                if (_transparentBins.TrianglesIn(t).Length == 0)
                {
                    return;
                }

                var tile = _transparentBins.TileAt(t);

                FrameBuffer.SetFragmentArena(
                    _fragments.ArenaFor(t, tile.XFrom, tile.YFrom, tile.XTo, tile.YTo));

                try
                {
                    PaintTransparentTile(painter, surface, meshes, worldBuffer, t, drawEvents, meshIdBase);
                }
                finally
                {
                    // Always, and on the worker that set it: the pool hands these threads back
                    // out, and one still pointing at last frame's arena would store into it.
                    FrameBuffer.SetFragmentArena(null);
                }
            });
        }
        else
        {
            FrameBuffer.SetFragmentArena(_fragments.ArenaFor(0, 0, 0, surface.Width, surface.Height));

            try
            {
                PaintTransparentAll(painter, surface, meshes, worldBuffer, transparentCount, drawEvents, meshIdBase);
            }
            finally
            {
                FrameBuffer.SetFragmentArena(null);
            }
        }

        _fragments.Resolve(surface);

        Stats.TransparentFragmentCount = _fragments.FragmentCount;
        Stats.TransparentPixelCount = _fragments.CoveredPixelCount;
        Stats.TransparentOverflowCount = _fragments.OverflowCount;

        events.Add(
            GraphicsEventKind.TransparencyResolve,
            SceneObjectIds.RenderTarget,
            _fragments.FragmentCount,
            _fragments.CoveredPixelCount,
            _fragments.OverflowCount);
    }

    private void PaintTransparentTile(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int tileIndex, int[]? drawEvents, int meshIdBase)
    {
        var ordinals = _transparentBins.TrianglesIn(tileIndex);
        if (ordinals.Length == 0)
        {
            return;
        }

        var tile = _transparentBins.TileAt(tileIndex);

        // Transparent geometry is depth-tested but never depth-written, so the bound cannot
        // move while this pass runs: one scan of the finished opaque depth covers the tile.
        var bound = Settings.HierarchicalZ && !surface.IsProbing
            ? surface.MaxDepthIn(tile.XFrom, tile.YFrom, tile.XTo, tile.YTo)
            : int.MaxValue;

        var rejected = 0;

        foreach (var ordinal in ordinals)
        {
            if (_transparentBins.NearestDepth(ordinal) > bound)
            {
                rejected++;
                continue;
            }

            var (meshIndex, triangleIndex) = _transparentOrder[ordinal];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, tile, drawEvents, meshIdBase);
        }

        if (rejected > 0)
        {
            Stats.AddOccludedTriangles(rejected);
        }
    }

    private void PaintAll(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int[]? drawEvents, int meshIdBase)
    {
        var count = _visible.Count;
        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _visible[i];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, ScreenTile.Full, drawEvents, meshIdBase);
        }
    }

    private void PaintTransparentAll(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int count, int[]? drawEvents, int meshIdBase)
    {
        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _transparentOrder[i];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, ScreenTile.Full, drawEvents, meshIdBase);
        }
    }

    private static void PaintTriangle(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int meshIndex, int triangleIndex, in ScreenTile tile, int[]? drawEvents, int meshIdBase)
    {
        var vbx = worldBuffer.VertexBuffers[meshIndex];

        // Clipped sub-triangles keep the color and diagnostics identity of the mesh
        // triangle they came from.
        var sourceIndex = vbx.SourceTriangleIndex(triangleIndex);

        if (drawEvents is not null)
        {
            FrameBuffer.SetProbeContext(drawEvents[meshIndex], PixelWriteSource.Triangle, meshIdBase + meshIndex, sourceIndex, vbx);
        }

        painter.DrawTriangle(
            surface,
            meshes[meshIndex].TriangleColors[sourceIndex],
            vbx,
            triangleIndex,
            tile);
    }

    /// <summary>
    /// Orders this frame's transparent triangles farthest-first by the mean view-space
    /// depth (clip-space w) of their vertices, into <see cref="_transparentOrder"/>.
    /// Returns the number of entries.
    ///
    /// <para>
    /// <paramref name="orderIndependent"/> skips the sort itself and only fills the array. A
    /// resolve that orders the fragments of each pixel does not care what order the triangles
    /// that produced them arrived in — that is what it is for — so the sort would be a pass over
    /// the draw list computing a key nothing reads.
    /// </para>
    /// </summary>
    private int SortTransparent(WorldBuffer worldBuffer, bool orderIndependent)
    {
        var count = _transparent.Count;
        if (count == 0)
        {
            return 0;
        }

        if (orderIndependent)
        {
            if (_transparentOrder.Length < count)
            {
                var size = System.Math.Max(count, _transparentOrder.Length * 2);
                _transparentKeys = new float[size];
                _transparentOrder = new (int, int)[size];
            }

            CollectionsMarshal.AsSpan(_transparent).CopyTo(_transparentOrder);
            return count;
        }

        if (_transparentKeys.Length < count)
        {
            var capacity = System.Math.Max(count, _transparentKeys.Length * 2);
            _transparentKeys = new float[capacity];
            _transparentOrder = new (int, int)[capacity];
        }

        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _transparent[i];
            var vbx = worldBuffer.VertexBuffers[meshIndex];
            var t = vbx.GetTriangle(triangleIndex);

            // Negated, so an ascending sort puts the farthest triangle first.
            _transparentKeys[i] = -(vbx.GetVertex(t.I0).Proj.W + vbx.GetVertex(t.I1).Proj.W + vbx.GetVertex(t.I2).Proj.W);
            _transparentOrder[i] = (meshIndex, triangleIndex);
        }

        Array.Sort(_transparentKeys, _transparentOrder, 0, count);
        return count;
    }

    private void DrawWireframeOverlay(FrameBuffer surface, WorldBuffer worldBuffer, List<(int MeshIndex, int TriangleIndex)> list, int wireFrameEvent, int[]? drawEvents, int meshIdBase)
    {
        foreach (var (meshIndex, triangleIndex) in list)
        {
            var vbx = worldBuffer.VertexBuffers[meshIndex];

            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(
                    wireFrameEvent,
                    PixelWriteSource.WireFrame,
                    meshIdBase + meshIndex, vbx.SourceTriangleIndex(triangleIndex),
                    vbx);
            }

            _internalWireFramePainter.DrawTriangle(surface, ColorRGB.Magenta, vbx, triangleIndex, ScreenTile.Full);
        }
    }
}
