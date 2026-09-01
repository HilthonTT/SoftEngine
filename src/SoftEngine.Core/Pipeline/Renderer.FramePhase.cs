using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Pipeline.Temporal;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

public sealed partial class Renderer
{
    private PixelHistory? BeginFrame(
        Scene scene,
        FrameBuffer surface,
        IProjection projection,
        RendererSettings rendererSettings,
        RenderDiagnostics diagnostics,
        GraphicsEventLog events)
    {
        Stats.Clear();
        Stats.PaintTime();

        diagnostics.FrameNumber++;
        events.Clear();
        events.Add(GraphicsEventKind.FrameBegin, -1, diagnostics.FrameNumber);
        events.Add(GraphicsEventKind.RendererSetViewport, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

        PixelHistory? history = null;
        if (diagnostics.IsProbing && diagnostics.ProbeX < surface.Width && diagnostics.ProbeY < surface.Height)
        {
            history = new PixelHistory(diagnostics.ProbeX, diagnostics.ProbeY, diagnostics.FrameNumber);
            surface.BeginProbe(history);
        }
        diagnostics.PixelHistory = history;

        surface.SetHighDynamicRange(scene.HighDynamicRange);
        surface.SetOverdrawCounting(rendererSettings.DebugView == DebugView.Overdraw);
        surface.SetMipLevelRecording(rendererSettings.DebugView == DebugView.MipLevel);

        surface.SetReflectanceRecording(PostProcess?.NeedsReflectance ?? false);

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

        surface.RecordProbeClear(clearEvent);

        surface.Clear();
        Stats.CalculationTime();

        return history;
    }

    private void EndFrame(
        FrameBuffer surface,
        IWorld world,
        in Matrix4x4 viewMatrix,
        IProjection projection,
        RenderDiagnostics diagnostics,
        GraphicsEventLog events,
        PixelHistory? history)
    {
        _motion.Advance(world, viewMatrix * projection.ProjectionMatrix(surface.Width, surface.Height));

        Stats.StopTime();

        events.Add(GraphicsEventKind.FramePresent, SceneObjectIds.RenderTarget, Stats.DrawnPixelCount, Stats.BehindZPixelCount);

        if (history is not null)
        {
            history.FinalColor = surface.GetColor(history.X, history.Y);
            history.FinalDepth = surface.GetDepth(history.X, history.Y);
            surface.EndProbe();
        }

        diagnostics.CaptureFrame(Stats);
    }

    private ShadowMap? RenderShadowMap(Scene scene, GraphicsEventLog events, IPainter? painter)
    {
        var settings = scene.Shadows;

        if (settings is null || !settings.Enabled || settings.Strength <= 0f)
        {
            return null;
        }

        _shadowRenderer ??= new ShadowMapRenderer();

        ShadowView? view = null;

        if (settings.CascadeCount > 1 && !scene.Projection.IsOrthographic)
        {
            view = new ShadowView(
                scene.Camera.ViewMatrix,
                scene.Projection.ProjectionMatrix(scene.Surface.Width, scene.Surface.Height),
                scene.Projection.ZNear,
                scene.Projection.ZFar);
        }

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

    private void RenderVelocity(
        IWorld world,
        FrameBuffer surface,
        in Matrix4x4 viewProjection,
        GraphicsEventLog events,
        bool needed)
    {
        if (!needed)
        {
            _velocity.Clear();
            return;
        }

        _velocity.Resize(surface.Width, surface.Height);
        _velocityPass.Render(world, _velocity, viewProjection, _motion);

        events.Add(GraphicsEventKind.VelocityBufferRender, SceneObjectIds.DepthBuffer,
            _velocity.Width, _velocity.Height, _velocity.IsFilled ? 1f : 0f);
    }

    private static void RecordCameraEvents(
        ICamera camera,
        IProjection projection,
        FrameBuffer surface,
        GraphicsEventLog events)
    {
        var eye = camera.Position;

        events.Add(GraphicsEventKind.CameraSetViewMatrix, SceneObjectIds.Camera, eye.X, eye.Y, eye.Z);
        events.Add(GraphicsEventKind.ProjectionSetProjectionMatrix, SceneObjectIds.Projection,
            projection.ZNear, projection.ZFar, surface.Width / (float)surface.Height);
    }

    private OcclusionCuller? PrepareOcclusion(
        IWorld world,
        FrameBuffer surface,
        RendererSettings rendererSettings,
        in Matrix4x4 viewMatrix,
        in Matrix4x4 projectionMatrix,
        ReadOnlySpan<Vector4> frustumPlanes,
        GraphicsEventLog events)
    {
        if (!rendererSettings.OcclusionCulling || surface.IsProbing)
        {
            _occlusion.Reset();
            return null;
        }

        _occlusion.Prepare(world, viewMatrix, projectionMatrix, frustumPlanes, surface.Width, surface.Height);

        Stats.OccluderMeshCount = _occlusion.OccluderCount;

        var (occlusionWidth, occlusionHeight) = (_occlusion.Buffer.Width, _occlusion.Buffer.Height);

        events.Add(GraphicsEventKind.OcclusionBufferRender, SceneObjectIds.DepthBuffer,
            occlusionWidth, occlusionHeight, _occlusion.OccluderCount);

        return _occlusion;
    }

    private WorldBuffer EnsureWorldBuffer(IWorld world)
    {
        if (_worldBuffer is null || !_worldBuffer.Fits(world))
        {
            _worldBuffer?.Dispose();
            _worldBuffer = new WorldBuffer(world);
        }
        else
        {
            _worldBuffer.Reset();
        }

        return _worldBuffer;
    }

    private void PaintOpaque(
        IPainter? painter,
        FrameBuffer surface,
        List<IMesh> meshes,
        WorldBuffer worldBuffer,
        int[]? drawEvents,
        int meshIdBase,
        bool parallelFill)
    {
        if (painter is null || _visible.Count == 0)
        {
            return;
        }

        if (!parallelFill)
        {
            PaintAll(painter, surface, meshes, worldBuffer, drawEvents, meshIdBase);
            return;
        }

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

    private static void DrawSky(Scene scene, FrameBuffer surface, GraphicsEventLog events)
    {
        if (!scene.ShowSky || scene.Environment is not { } environment)
        {
            return;
        }

        var skyEvent = events.Add(GraphicsEventKind.SkyRender, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

        SkyRenderer.Render(scene, environment, skyEvent);
    }

    private void ApplyTemporalEffects(FrameBuffer surface, GraphicsEventLog events, bool temporal, bool motionBlur)
    {
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
    }
}
